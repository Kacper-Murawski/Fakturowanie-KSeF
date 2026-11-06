using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Models;
using Microsoft.EntityFrameworkCore;

namespace FakturowanieKSeF.Api.Services.Email;

/// <summary>
/// Powiadomienie użytkownika o nowych fakturach kosztowych pobranych z KSeF (D38) - jedna zbiorcza wiadomość
/// na synchronizację. Adres: ustawiony w powiadomieniach albo adres konta właściciela firmy.
/// </summary>
public sealed class ReceivedInvoiceNotifier(AppDbContext db, EmailOutbox outbox, EmailWorkSignal signal, IConfiguration configuration)
{
    /// <summary>Adres powiadomień dla użytkownika (ustawiony albo adres konta).</summary>
    public async Task<string?> RecipientAsync(Company company, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(company.NotificationEmail)
            ? await db.Users.Where(u => u.Id == company.OwnerId).Select(u => u.Email).SingleOrDefaultAsync(ct)
            : company.NotificationEmail;

    public async Task NotifyAsync(Company company, IReadOnlyList<ReceivedInvoice> invoices, CancellationToken ct = default)
    {
        if (invoices.Count == 0 || !company.NotifyReceivedInvoices || !outbox.IsConfigured) return;
        if (await RecipientAsync(company, ct) is not { Length: > 0 } to) return;

        var (subject, body) = EmailTemplates.ReceivedInvoices(company, invoices, configuration["App:PublicUrl"]);
        await outbox.EnqueueAsync(company.Id, EmailKind.ReceivedInvoices, to, subject, body, ct: ct);
        await db.SaveChangesAsync(ct);
        signal.Pulse();
    }
}
