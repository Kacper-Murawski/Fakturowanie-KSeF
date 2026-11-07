using System.Text;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Ksef;
using FakturowanieKSeF.Models;
using Microsoft.EntityFrameworkCore;

namespace FakturowanieKSeF.Api.Services;

/// <summary>
/// Przetwarzanie wysyłek faktur do KSeF - maszyna stanów, której każdy krok jest zapisywany w bazie
/// (restart serwera niczego nie gubi; kolejny przebieg kontynuuje od ostatniego zapisanego kroku):
/// <code>
/// [1] brak numeru referencyjnego  → wysłanie faktury w sesji interaktywnej
/// [2] status 150 (w toku)          → odpytanie KSeF o status
///     ├─ 200 → faktura przyjęta: numer KSeF, status Accepted, zamknięcie sesji
///     └─ błąd → faktura odrzucona: szczegóły błędów, status Rejected, zamknięcie sesji
/// [3] przyjęta, brak UPO           → pobranie UPO (KSeF generuje je po zamknięciu sesji)
/// </code>
/// Błędy komunikacji (sieć, niedostępność KSeF) są ponawiane. Zasada bezpieczeństwa: faktury, która mogła już
/// trafić do KSeF, nigdy nie oznaczamy jako odrzuconej z powodu własnego błędu komunikacji - ponowna wysyłka
/// mogłaby utworzyć duplikat dokumentu podatkowego.
/// </summary>
public sealed class KsefSubmissionProcessor(
    AppDbContext db,
    IKsefGateway gateway,
    KsefTokenProtector tokenProtector,
    TimeProvider time,
    ILogger<KsefSubmissionProcessor> logger)
{
    /// <summary>Po tylu nieudanych próbach wysłania (zanim faktura trafiła do KSeF) wysyłka jest przerywana.</summary>
    public const int MaxSendAttempts = 10;

    /// <summary>Jak długo próbujemy pobrać UPO dla przyjętej faktury.</summary>
    public static readonly TimeSpan UpoRetrievalWindow = TimeSpan.FromDays(7);

    public async Task<int> ProcessAsync(CancellationToken ct = default)
    {
        var upoSince = time.GetUtcNow().UtcDateTime - UpoRetrievalWindow;

        var open = await db.KsefSubmissions
            .Include(s => s.Invoice).ThenInclude(i => i.Company)
            // Faktury pobrane z KSeF (wystawione poza aplikacją, D44) nie mają sesji aplikacji - UPO jest u wystawcy
            .Where(s => s.CompletedAt == null
                        || (s.KsefNumber != null && s.UpoXml == null && s.SessionReferenceNumber != null && s.SentAt > upoSince))
            .OrderBy(s => s.Id)
            .Take(50)
            .ToListAsync(ct);

        foreach (var submission in open)
        {
            try
            {
                await StepAsync(submission, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RecordFailure(submission, ex);
            }

            await db.SaveChangesAsync(ct);
        }

        return open.Count;
    }

    private async Task StepAsync(KsefSubmission s, CancellationToken ct)
    {
        var invoice = s.Invoice;
        var company = invoice.Company;
        var nip = invoice.Seller.Nip!;

        if (company.KsefTokenEncrypted is null)
            throw new KsefAuthenticationException("Firma nie ma skonfigurowanego tokenu KSeF.");
        var token = tokenProtector.Unprotect(company.KsefTokenEncrypted);

        // [1] Wysłanie
        if (s.InvoiceReferenceNumber is null)
        {
            var sent = await gateway.SendInvoiceAsync(nip, token, Encoding.UTF8.GetBytes(s.SentXml), ct);
            s.SessionReferenceNumber = sent.SessionReferenceNumber;
            s.InvoiceReferenceNumber = sent.InvoiceReferenceNumber;
            s.StatusCode = KsefInvoiceStatus.Processing;
            s.StatusDescription = "Faktura przyjęta do przetwarzania przez KSeF.";
            s.LastError = null;
            logger.LogInformation("Faktura {InvoiceId} wysłana do KSeF, numer referencyjny {Reference}", invoice.Id, sent.InvoiceReferenceNumber);
            return;
        }

        // [2] Status przetwarzania
        if (s.CompletedAt is null)
        {
            var status = await gateway.GetInvoiceStatusAsync(nip, token, s.SessionReferenceNumber!, s.InvoiceReferenceNumber, ct);
            s.StatusCode = status.Code;
            s.StatusDescription = Truncate(status.Description, 500);
            s.StatusDetails = status.Details.Count > 0 ? string.Join('\n', status.Details) : null;
            s.LastError = null;

            if (status.IsProcessing) return;

            var now = time.GetUtcNow().UtcDateTime;
            s.CompletedAt = now;

            if (status.IsAccepted)
            {
                s.KsefNumber = status.KsefNumber;
                s.KsefAssignedAt = status.AcceptedAt?.UtcDateTime ?? now;
                invoice.Status = Contracts.InvoiceStatus.Accepted;
                invoice.KsefNumber = status.KsefNumber;
                invoice.KsefAcceptedAt = s.KsefAssignedAt;
                logger.LogInformation("Faktura {InvoiceId} przyjęta w KSeF: {KsefNumber}", invoice.Id, status.KsefNumber);
            }
            else
            {
                invoice.Status = Contracts.InvoiceStatus.Rejected;
                logger.LogWarning("Faktura {InvoiceId} odrzucona przez KSeF: {Code} {Description}", invoice.Id, status.Code, status.Description);
            }

            await CloseSessionQuietlyAsync(nip, token, s, ct);
            return;
        }

        // [3] UPO
        if (s.KsefNumber is not null && s.UpoXml is null)
        {
            var upo = await gateway.GetUpoAsync(nip, token, s.SessionReferenceNumber!, s.KsefNumber, ct);
            if (upo is not null) s.UpoXml = upo;
        }
    }

    private async Task CloseSessionQuietlyAsync(string nip, string token, KsefSubmission s, CancellationToken ct)
    {
        try
        {
            await gateway.CloseSessionAsync(nip, token, s.SessionReferenceNumber!, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sesja wygaśnie sama; brak zamknięcia opóźnia jedynie wygenerowanie UPO
            logger.LogWarning(ex, "Nie udało się zamknąć sesji KSeF {Session}", s.SessionReferenceNumber);
        }
    }

    private void RecordFailure(KsefSubmission s, Exception ex)
    {
        s.FailedAttempts++;
        s.LastError = Truncate(ex.Message, 1000);
        logger.LogWarning(ex, "Błąd komunikacji z KSeF (wysyłka {SubmissionId}, próba {Attempt})", s.Id, s.FailedAttempts);

        // Przerwanie wysyłki jest dopuszczalne WYŁĄCZNIE, gdy faktura na pewno nie trafiła do KSeF
        var notSentYet = s.InvoiceReferenceNumber is null && s.CompletedAt is null;
        if (notSentYet && (ex is KsefAuthenticationException || s.FailedAttempts >= MaxSendAttempts))
        {
            s.CompletedAt = time.GetUtcNow().UtcDateTime;
            s.StatusDescription = ex is KsefAuthenticationException
                ? "Nie wysłano: " + ex.Message
                : $"Nie wysłano: KSeF niedostępny po {s.FailedAttempts} próbach. Spróbuj ponownie później.";
            s.Invoice.Status = Contracts.InvoiceStatus.Rejected;
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
