using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Models;
using Microsoft.EntityFrameworkCore;

namespace FakturowanieKSeF.Api.Services.Email;

/// <summary>
/// Przypomnienia o płatności (D37): ręczne (przycisk przy fakturze) i automatyczne dla faktur nieopłaconych
/// po terminie. Automatycznie wysyłane jest najwyżej <see cref="MaxReminders"/> przypomnień do faktury, nie częściej
/// niż co <see cref="RepeatDays"/> dni (licząc także przypomnienia ręczne) - żeby nie nękać kontrahenta.
/// </summary>
public sealed class PaymentReminders(AppDbContext db, EmailOutbox outbox, TimeProvider time, ILogger<PaymentReminders> logger)
{
    public const int MaxReminders = 3;
    public const int RepeatDays = 7;

    /// <summary>Faktura podlegająca przypomnieniu: faktura VAT przyjęta w KSeF (z numerem KSeF, KODEM QR na PDF).</summary>
    public static bool CanRemind(Invoice invoice) => invoice is { Kind: InvoiceKind.Vat, Status: InvoiceStatus.Accepted };

    /// <summary>Pozostało do zapłaty (wymaga załadowanych płatności i korekt).</summary>
    public static decimal Remaining(Invoice invoice) => invoice.TotalDue() - invoice.Payments.Sum(p => p.Amount);

    /// <summary>Adres nabywcy: z książki adresowej, a w drugiej kolejności - ostatni adres, na który wysłano fakturę.</summary>
    public async Task<string?> RecipientAsync(Invoice invoice, CancellationToken ct = default)
    {
        if (invoice.CustomerId is { } customerId
            && await db.Customers.Where(c => c.Id == customerId && c.CompanyId == invoice.CompanyId).Select(c => c.Email).SingleOrDefaultAsync(ct) is { Length: > 0 } email)
            return email;

        return await db.EmailMessages
            .Where(m => m.InvoiceId == invoice.Id && m.CompanyId == invoice.CompanyId && m.Kind == EmailKind.Invoice)
            .OrderByDescending(m => m.Id).Select(m => m.To).FirstOrDefaultAsync(ct);
    }

    public async Task<EmailMessage> EnqueueAsync(Invoice invoice, Company company, string to, string? message, CancellationToken ct = default)
    {
        var (subject, body) = EmailTemplates.PaymentReminder(invoice, company, Remaining(invoice), PolishDate.Today(time), message);
        return await outbox.EnqueueAsync(company.Id, EmailKind.PaymentReminder, to, subject, body, invoice.Id, ct);
    }

    /// <summary>
    /// Automatyczne przypomnienia wszystkich firm z włączoną funkcją. Zwraca liczbę dodanych wiadomości.
    /// Bezpieczne do wielokrotnego uruchamiania - warunek odstępu <see cref="RepeatDays"/> dni wyklucza duplikaty.
    /// </summary>
    public async Task<int> RunAutomaticAsync(CancellationToken ct = default)
    {
        if (!outbox.IsConfigured) return 0;

        var today = PolishDate.Today(time);
        var repeatAfter = time.GetUtcNow().UtcDateTime.AddDays(-RepeatDays).AddHours(1); // margines na porę uruchomienia
        var queued = 0;

        var companies = await db.Companies.Where(c => c.AutoRemindersEnabled).ToListAsync(ct);
        foreach (var company in companies)
        {
            var lastDue = today.AddDays(-company.ReminderDaysAfterDue);
            var invoices = await db.Invoices
                .Include(i => i.Payments).Include(i => i.Corrections)
                .Where(i => i.CompanyId == company.Id && i.Kind == InvoiceKind.Vat && i.Status == InvoiceStatus.Accepted
                            && i.DueDate != null && i.DueDate <= lastDue)
                .AsSplitQuery()
                .ToListAsync(ct);

            foreach (var invoice in invoices.Where(i => Remaining(i) > 0))
            {
                var history = await db.EmailMessages
                    .Where(m => m.InvoiceId == invoice.Id && m.Kind == EmailKind.PaymentReminder)
                    .Select(m => m.CreatedAt).ToListAsync(ct);
                if (history.Count >= MaxReminders || history.Any(c => c > repeatAfter)) continue;
                if (await RecipientAsync(invoice, ct) is not { } to) continue;

                try
                {
                    await EnqueueAsync(invoice, company, to, null, ct);
                    await db.SaveChangesAsync(ct);
                    queued++;
                }
                catch (EmailLimitException)
                {
                    logger.LogWarning("Firma {CompanyId}: dzienny limit wiadomości - pozostałe przypomnienia jutro", company.Id);
                    break;
                }
            }
        }
        return queued;
    }
}

/// <summary>Usługa w tle: automatyczne przypomnienia o płatności (domyślnie co godzinę).</summary>
public sealed class PaymentReminderWorker(IServiceScopeFactory scopes, EmailWorkSignal signal, IConfiguration configuration,
    ILogger<PaymentReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Reminders:Worker:Enabled", true))
        {
            logger.LogInformation("Automatyczne przypomnienia o płatności są wyłączone w konfiguracji.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue("Reminders:Worker:IntervalMinutes", 60)));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (await scope.ServiceProvider.GetRequiredService<PaymentReminders>().RunAutomaticAsync(stoppingToken) > 0)
                    signal.Pulse();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Błąd automatycznych przypomnień o płatności");
            }
            await Task.Delay(interval, stoppingToken).ContinueWith(_ => { });
        }
    }
}
