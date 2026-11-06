using System.Text;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Endpoints;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Api.Services.Email;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Models;
using Microsoft.EntityFrameworkCore;

namespace FakturowanieKSeF.Api.Services;

/// <summary>
/// Faktury cykliczne (D39): tworzenie szkiców według harmonogramów. Szkic powstaje tą samą ścieżką co faktura
/// z formularza (<see cref="InvoiceEndpoints.ApplyAsync"/>): aktualne dane kontrahenta z książki adresowej, numeracja,
/// obliczenia. Do KSeF szkic wysyła użytkownik.
/// </summary>
public sealed class RecurringInvoices(
    AppDbContext db,
    InvoiceNumbering numbering,
    EmailOutbox outbox,
    ReceivedInvoiceNotifier notifier,
    TimeProvider time,
    IConfiguration configuration,
    ILogger<RecurringInvoices> logger)
{
    /// <summary>Data wystawienia w kolejnym okresie: ten sam dzień miesiąca, w krótszym miesiącu - ostatni dzień.</summary>
    public static DateOnly Next(DateOnly date, int intervalMonths, int dayOfMonth)
    {
        var month = new DateOnly(date.Year, date.Month, 1).AddMonths(intervalMonths);
        return new DateOnly(month.Year, month.Month, Math.Min(dayOfMonth, DateTime.DaysInMonth(month.Year, month.Month)));
    }

    /// <summary>
    /// Tworzy szkice dla harmonogramów, których termin nadszedł. Po przestoju (np. kilka pominiętych okresów)
    /// powstaje JEDEN szkic, a termin przesuwa się na najbliższy przyszły - bez lawiny zaległych faktur.
    /// Zwraca liczbę utworzonych szkiców.
    /// </summary>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var today = PolishDate.Today(time);
        var due = await db.RecurringInvoices
            .Include(r => r.Company)
            .Where(r => r.IsActive && r.NextIssueDate <= today)
            .OrderBy(r => r.CompanyId).ThenBy(r => r.Id)
            .ToListAsync(ct);

        var created = new Dictionary<Company, List<(RecurringInvoice Schedule, Invoice Invoice)>>();
        foreach (var schedule in due)
        {
            if (schedule.EndDate is { } end && schedule.NextIssueDate > end)
            {
                schedule.IsActive = false;
                await db.SaveChangesAsync(ct);
                continue;
            }

            var (invoice, error) = await CreateDraftAsync(schedule, today, ct);
            if (invoice is null)
            {
                // Wzór usunięty lub niepoprawny - harmonogram wstrzymany, użytkownik widzi przyczynę
                schedule.IsActive = false;
                schedule.LastError = error;
                await db.SaveChangesAsync(ct);
                logger.LogWarning("Harmonogram faktury cyklicznej {Id} wstrzymany: {Error}", schedule.Id, error);
                continue;
            }

            schedule.IssuedCount++;
            schedule.LastIssuedOn = today;
            schedule.LastInvoiceId = invoice.Id;
            schedule.LastError = null;
            do schedule.NextIssueDate = Next(schedule.NextIssueDate, schedule.IntervalMonths, schedule.DayOfMonth);
            while (schedule.NextIssueDate <= today);
            if (schedule.EndDate is { } last && schedule.NextIssueDate > last) schedule.IsActive = false;
            await db.SaveChangesAsync(ct);

            if (schedule.NotifyByEmail)
            {
                if (!created.TryGetValue(schedule.Company, out var list)) created[schedule.Company] = list = [];
                list.Add((schedule, invoice));
            }
        }

        foreach (var (company, list) in created) await NotifyAsync(company, list, ct);
        return due.Count(s => s.LastIssuedOn == today && s.LastError is null);
    }

    private async Task<(Invoice? Invoice, string? Error)> CreateDraftAsync(RecurringInvoice schedule, DateOnly today, CancellationToken ct)
    {
        var template = await db.Invoices.AsNoTracking().Include(i => i.Items)
            .SingleOrDefaultAsync(i => i.Id == schedule.TemplateInvoiceId && i.CompanyId == schedule.CompanyId, ct);
        if (template is null) return (null, "Faktura-wzór została usunięta.");

        var customerExists = template.CustomerId is { } cid
            && await db.Customers.AnyAsync(c => c.Id == cid && c.CompanyId == schedule.CompanyId, ct);

        var request = InvoiceTemplates.RequestFrom(template, customerExists, today);

        for (var attempt = 1; ; attempt++)
        {
            var invoice = new Invoice { CompanyId = schedule.CompanyId };
            if (await InvoiceEndpoints.ApplyAsync(invoice, request, schedule.Company, db, ct) is not null)
                return (null, "Nie udało się utworzyć faktury na podstawie wzoru - sprawdź fakturę-wzór.");
            invoice.Number = await numbering.NextAsync(schedule.CompanyId, today, InvoiceKind.Vat, ct);
            db.Invoices.Add(invoice);
            try
            {
                await db.SaveChangesAsync(ct);
                return (invoice, null);
            }
            catch (DbUpdateException ex) when (attempt < 3 && InvoiceEndpoints.IsDuplicateNumber(ex))
            {
                db.Entry(invoice).State = EntityState.Detached;
                foreach (var item in invoice.Items) db.Entry(item).State = EntityState.Detached;
            }
        }
    }

    private async Task NotifyAsync(Company company, List<(RecurringInvoice Schedule, Invoice Invoice)> list, CancellationToken ct)
    {
        if (!outbox.IsConfigured || await notifier.RecipientAsync(company, ct) is not { Length: > 0 } to) return;
        try
        {
            var b = new StringBuilder();
            b.AppendLine("Dzień dobry,").AppendLine();
            b.AppendLine("przygotowano szkice faktur cyklicznych - sprawdź je i wyślij do KSeF:").AppendLine();
            foreach (var (schedule, invoice) in list)
                b.AppendLine($"- {invoice.Number}: {invoice.Buyer.Name}, {EmailTemplates.Money(invoice.TotalGross, invoice.Currency)}"
                             + (schedule.Name is null ? "" : $" ({schedule.Name})"));
            var appUrl = configuration["App:PublicUrl"];
            b.AppendLine().AppendLine(string.IsNullOrWhiteSpace(appUrl)
                ? "Szkice są na liście faktur w aplikacji."
                : $"Lista faktur: {appUrl.TrimEnd('/')}/faktury?status=Draft");
            b.AppendLine().AppendLine("--").AppendLine("Fakturowanie KSeF (wersja testowa - faktury trafiają do testowego środowiska KSeF).");

            await outbox.EnqueueAsync(company.Id, EmailKind.RecurringInvoices, to,
                $"Faktury cykliczne do wysłania: {list.Count} - {company.Name}", b.ToString(), ct: ct);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Nie udało się dodać powiadomienia o fakturach cyklicznych firmy {CompanyId}", company.Id);
        }
    }

    public static RecurringInvoiceDto ToDto(RecurringInvoice r) => new(
        r.Id, r.Name, r.TemplateInvoiceId, r.TemplateInvoice.Number, r.TemplateInvoice.Buyer.Name, r.TemplateInvoice.TotalGross,
        r.TemplateInvoice.Currency, r.IntervalMonths, r.NextIssueDate, r.EndDate, r.IsActive, r.NotifyByEmail, r.IssuedCount,
        r.LastIssuedOn, r.LastInvoiceId, r.LastError);
}

/// <summary>Usługa w tle: faktury cykliczne (domyślnie co godzinę).</summary>
public sealed class RecurringInvoiceWorker(IServiceScopeFactory scopes, EmailWorkSignal signal, IConfiguration configuration,
    ILogger<RecurringInvoiceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Recurring:Worker:Enabled", true))
        {
            logger.LogInformation("Faktury cykliczne są wyłączone w konfiguracji.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue("Recurring:Worker:IntervalMinutes", 60)));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (await scope.ServiceProvider.GetRequiredService<RecurringInvoices>().RunAsync(stoppingToken) > 0)
                    signal.Pulse();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Błąd usługi faktur cyklicznych");
            }
            await Task.Delay(interval, stoppingToken).ContinueWith(_ => { });
        }
    }
}
