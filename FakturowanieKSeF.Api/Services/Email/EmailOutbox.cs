using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Ksef.Fa3;
using FakturowanieKSeF.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FakturowanieKSeF.Api.Services.Email;

/// <summary>Przekroczono dzienny limit wiadomości firmy.</summary>
public sealed class EmailLimitException(string message) : Exception(message);

/// <summary>Sygnał „jest nowa wiadomość” - budzi usługę wysyłki od razu.</summary>
public sealed class EmailWorkSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Pulse()
    {
        if (_signal.CurrentCount == 0)
        {
            try { _signal.Release(); } catch (SemaphoreFullException) { /* już zasygnalizowano */ }
        }
    }

    public Task WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}

/// <summary>
/// Kolejka wiadomości e-mail (D36): dodawanie wiadomości z kontrolą dziennego limitu i przetwarzanie kolejki
/// (wysyłka z ponawianiem po błędzie).
/// </summary>
public sealed class EmailOutbox(
    AppDbContext db,
    IEmailSender sender,
    IOptions<EmailOptions> options,
    Fa3XmlGenerator generator,
    InvoicePdfRenderer renderer,
    TimeProvider time,
    ILogger<EmailOutbox> logger)
{
    /// <summary>Odstępy kolejnych prób; po ich wyczerpaniu wiadomość ma status <see cref="EmailStatus.Failed"/>.</summary>
    public static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1)];

    public bool IsConfigured => options.Value.IsConfigured;

    /// <summary>Wiadomości na adresy podane przez użytkownika - podlegają limitom firmy i całej aplikacji (SEC-4).</summary>
    public static bool IsUserAddressed(EmailKind kind) => kind is EmailKind.Invoice or EmailKind.PaymentReminder or EmailKind.Invitation;

    /// <summary>Wiadomości systemowe konta (bez firmy) - osobny limit na adres (D53).</summary>
    public static bool IsSystem(EmailKind kind) =>
        kind is EmailKind.AccountConfirmation or EmailKind.PasswordReset or EmailKind.EmailChange or EmailKind.EmailChangeNotice;

    /// <summary>
    /// Dodaje wiadomość do kolejki (zapis razem z innymi zmianami w <see cref="AppDbContext"/> przy SaveChanges).
    /// Wiadomości do osób trzecich (faktura, przypomnienie, zaproszenie) podlegają dziennemu limitowi firmy i całej
    /// aplikacji. Liczniki są zwiększane atomowo w bazie od razu (także gdy zapis wiadomości się nie powiedzie - limit
    /// liczony „ostrożnie”), więc równoległe żądania nie przekroczą limitu (D53, SEC-4).
    /// </summary>
    public async Task<EmailMessage> EnqueueAsync(int companyId, EmailKind kind, string to, string subject, string body,
        int? invoiceId = null, CancellationToken ct = default)
    {
        if (IsSystem(kind)) throw new ArgumentException("Wiadomości systemowe - EnqueueSystemAsync.", nameof(kind));

        var now = time.GetUtcNow().UtcDateTime;
        if (IsUserAddressed(kind))
        {
            if (!await TryTakeQuotaAsync($"company:{companyId}", options.Value.DailyLimitPerCompany, ct))
                throw new EmailLimitException($"Osiągnięto limit {options.Value.DailyLimitPerCompany} wiadomości na dobę. Spróbuj później.");
            if (!await TryTakeQuotaAsync("global", options.Value.GlobalDailyLimit, ct))
            {
                logger.LogWarning("Wyczerpany dzienny limit wiadomości całej aplikacji ({Limit})", options.Value.GlobalDailyLimit);
                throw new EmailLimitException("Aplikacja osiągnęła dzienny limit wysyłanych wiadomości. Spróbuj jutro.");
            }
        }

        var message = new EmailMessage
        {
            CompanyId = companyId, Kind = kind, To = to.Trim(), Subject = subject, Body = body, InvoiceId = invoiceId,
            CreatedAt = now, NextAttemptAt = now
        };
        db.EmailMessages.Add(message);
        return message;
    }

    /// <summary>
    /// Wiadomość systemowa konta (bez firmy): zapisywana od razu i wysyłana przez usługę w tle. Limit na adres na godzinę -
    /// po jego przekroczeniu wiadomość jest pomijana bez błędu (odpowiedzi endpointów konta pozostają neutralne).
    /// Zwraca, czy wiadomość dodano.
    /// </summary>
    public async Task<bool> EnqueueSystemAsync(EmailKind kind, string to, string subject, string body, CancellationToken ct = default)
    {
        if (!IsSystem(kind)) throw new ArgumentException("Tylko wiadomości systemowe konta.", nameof(kind));

        var now = time.GetUtcNow().UtcDateTime;
        var address = to.Trim();
        var lastHour = await db.EmailMessages.CountAsync(m => m.CompanyId == null && m.To == address && m.CreatedAt > now.AddHours(-1), ct);
        if (lastHour >= options.Value.SystemPerAddressPerHour)
        {
            logger.LogWarning("Pominięto wiadomość systemową {Kind} - limit {Limit}/godz. na adres", kind, options.Value.SystemPerAddressPerHour);
            return false;
        }

        db.EmailMessages.Add(new EmailMessage
        {
            CompanyId = null, Kind = kind, To = address, Subject = subject, Body = body, CreatedAt = now, NextAttemptAt = now
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Atomowo zwiększa licznik dnia (czasu polskiego) dla klucza, jeśli nie osiągnął limitu. Jedno polecenie SQL
    /// (INSERT … ON CONFLICT DO UPDATE … WHERE) - bez wyścigu „sprawdź, potem zapisz”.
    /// </summary>
    private async Task<bool> TryTakeQuotaAsync(string key, int limit, CancellationToken ct)
    {
        if (limit <= 0) return false;
        var day = PolishDate.Today(time);
        var taken = await db.Database.SqlQuery<int>($"""
            INSERT INTO "EmailQuotas" ("Key", "Day", "Count") VALUES ({key}, {day}, 1)
            ON CONFLICT ("Key", "Day") DO UPDATE SET "Count" = "EmailQuotas"."Count" + 1
            WHERE "EmailQuotas"."Count" < {limit}
            RETURNING "Count" AS "Value"
            """).ToListAsync(ct);
        return taken.Count == 1;
    }

    /// <summary>Jeden przebieg kolejki: wysyła wiadomości, których czas próby nadszedł. Zwraca liczbę przetworzonych.</summary>
    public async Task<int> ProcessAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var batch = await db.EmailMessages
            .Include(m => m.Company)
            .Where(m => m.Status == EmailStatus.Pending && m.NextAttemptAt <= now)
            .OrderBy(m => m.NextAttemptAt).ThenBy(m => m.Id)
            .Take(20)
            .ToListAsync(ct);

        foreach (var message in batch)
        {
            try
            {
                await sender.SendAsync(await BuildAsync(message, ct), ct);
                message.Status = EmailStatus.Sent;
                message.SentAt = time.GetUtcNow().UtcDateTime;
                message.LastError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Nieudana wysyłka wiadomości {Id} (próba {Attempt})", message.Id, message.Attempts + 1);
                message.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                if (message.Attempts < RetryDelays.Length)
                    message.NextAttemptAt = time.GetUtcNow().UtcDateTime + RetryDelays[message.Attempts];
                else
                    message.Status = EmailStatus.Failed;
            }
            message.Attempts++;
            await db.SaveChangesAsync(ct);
        }
        return batch.Count;
    }

    private async Task<OutgoingEmail> BuildAsync(EmailMessage message, CancellationToken ct)
    {
        List<EmailAttachment> attachments = [];
        if (message.InvoiceId is { } invoiceId)
        {
            var invoice = await db.Invoices.AsNoTracking().Include(i => i.Items).Include(i => i.KsefSubmissions)
                .AsSplitQuery()
                .SingleAsync(i => i.Id == invoiceId && i.CompanyId == message.CompanyId, ct);
            attachments.Add(new(InvoiceFiles.FileName(invoice.Number, "pdf"), "application/pdf", InvoiceFiles.Pdf(invoice, generator, renderer)));
            if (InvoiceFiles.SentXml(invoice) is { } xml)
                attachments.Add(new(InvoiceFiles.FileName(invoice.Number, "xml"), "application/xml", xml));
        }

        // Wiadomości do kontrahentów: nadawca widoczny jako firma, odpowiedzi trafiają na jej adres
        var toThirdParty = message.Kind is EmailKind.Invoice or EmailKind.PaymentReminder;
        return new OutgoingEmail(message.To, message.Subject, message.Body,
            toThirdParty ? message.Company?.Name : null,
            toThirdParty ? message.Company?.Email : null,
            attachments);
    }

    public static EmailMessageDto ToDto(EmailMessage m) =>
        new(m.Id, m.Kind, m.To, m.Subject, m.Status, m.CreatedAt, m.SentAt, m.Attempts, m.LastError);
}

/// <summary>Usługa w tle wysyłająca wiadomości z kolejki (budzona sygnałem albo co kilkanaście sekund).</summary>
public sealed class EmailWorker(IServiceScopeFactory scopes, EmailWorkSignal signal, IConfiguration configuration,
    ILogger<EmailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Email:Worker:Enabled", true))
        {
            logger.LogInformation("Usługa wysyłki e-mail jest wyłączona w konfiguracji.");
            return;
        }

        var interval = TimeSpan.FromSeconds(configuration.GetValue("Email:Worker:IntervalSeconds", 15));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<EmailOutbox>().ProcessAsync(stoppingToken);
                await signal.WaitAsync(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Błąd usługi wysyłki e-mail");
                await Task.Delay(interval, stoppingToken).ContinueWith(_ => { });
            }
        }
    }
}
