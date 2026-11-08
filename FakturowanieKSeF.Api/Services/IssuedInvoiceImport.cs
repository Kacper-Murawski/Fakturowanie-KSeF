using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Contracts.Calculation;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Ksef;
using FakturowanieKSeF.Ksef.Fa3;
using FakturowanieKSeF.Models;
using Microsoft.EntityFrameworkCore;

namespace FakturowanieKSeF.Api.Services;

/// <summary>
/// Import faktur sprzedażowych wystawionych poza aplikacją (D44): faktury, w których firma jest sprzedawcą
/// (Podmiot1), pobierane z KSeF przyrostowo do HWM - tak jak faktury otrzymane (D26). Zapisywane jako faktury
/// przyjęte w KSeF (<see cref="InvoiceOrigin.KsefImport"/>, tylko do odczytu) z dokumentem XML z KSeF, więc obejmują je
/// rozliczenia, eksport, wykresy, PDF i korekty. Faktury wysłane przez aplikację są rozpoznawane i pomijane.
/// </summary>
public sealed partial class IssuedInvoiceImport(
    AppDbContext db,
    IKsefGateway gateway,
    KsefTokenProtector tokenProtector,
    TimeProvider time,
    IConfiguration configuration,
    ILogger<IssuedInvoiceImport> logger)
{
    public const int PageSize = 100;
    public const int MaxPagesPerSync = 3;
    public const int MaxDownloadsPerSync = 40;

    private int InitialDays => configuration.GetValue("Ksef:Receive:InitialDays", 30);

    private static readonly Dictionary<string, PaymentMethod> PaymentMethods = new()
    {
        ["Gotówka"] = PaymentMethod.Cash, ["Karta"] = PaymentMethod.Card, ["Bon"] = PaymentMethod.Voucher, ["Czek"] = PaymentMethod.Cheque,
        ["Kredyt"] = PaymentMethod.Credit, ["Przelew"] = PaymentMethod.Transfer, ["Płatność mobilna"] = PaymentMethod.Mobile
    };

    public async Task<SyncResultDto> SyncAsync(Company company, CancellationToken ct = default)
    {
        if (company.KsefTokenEncrypted is null)
            throw new KsefAuthenticationException("Firma nie ma skonfigurowanego tokenu KSeF.");

        var token = tokenProtector.Unprotect(company.KsefTokenEncrypted);
        var now = time.GetUtcNow();
        var from = company.IssuedInvoicesSyncedUntil is { } synced
            ? new DateTimeOffset(DateTime.SpecifyKind(synced, DateTimeKind.Utc))
            : now.AddDays(-InitialDays);

        company.IssuedInvoicesLastSyncAt = now.UtcDateTime;
        DateTimeOffset? checkpoint = null;
        int downloaded = 0, skipped = 0;
        var complete = false;

        try
        {
            for (var pageNo = 0; pageNo < MaxPagesPerSync; pageNo++)
            {
                var page = await gateway.QueryIssuedInvoicesAsync(company.Nip, token, from, pageNo, PageSize, ct);
                logger.LogInformation("KSeF: faktury wystawione firmy {CompanyId} od {From:o}: {Count} faktur, HWM {Hwm:o}, kolejne strony: {HasMore}",
                    company.Id, from, page.Invoices.Count, page.PermanentStorageHwmDate, page.HasMore);
                var budgetExhausted = false;

                foreach (var meta in page.Invoices)
                {
                    if (!await KnownAsync(company.Id, meta, ct))
                    {
                        if (downloaded + skipped >= MaxDownloadsPerSync)
                        {
                            budgetExhausted = true;
                            break;
                        }

                        var xml = await gateway.DownloadInvoiceAsync(company.Nip, token, meta.KsefNumber, ct);
                        if (await ToInvoiceAsync(company, meta, xml, ct) is { } invoice)
                        {
                            db.Invoices.Add(invoice);
                            await db.SaveChangesAsync(ct); // każda faktura zapisana trwale - przerwanie niczego nie gubi
                            downloaded++;
                        }
                        else
                        {
                            skipped++;
                            logger.LogInformation("Pominięto fakturę {KsefNumber} - rodzaj lub stawka nieobsługiwane przy imporcie", meta.KsefNumber);
                        }
                    }
                    checkpoint = meta.PermanentStorageDate;
                }

                if (budgetExhausted) break;
                if (!page.HasMore)
                {
                    if (!page.IsTruncated && page.PermanentStorageHwmDate is { } hwm && (checkpoint is null || hwm > checkpoint))
                        checkpoint = hwm;
                    complete = true;
                    break;
                }
            }
            company.IssuedInvoicesLastSyncError = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            company.IssuedInvoicesLastSyncError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            logger.LogWarning(ex, "Import faktur sprzedażowych firmy {CompanyId} nie powiódł się", company.Id);
        }

        if (checkpoint is { } c && (company.IssuedInvoicesSyncedUntil is null || c.UtcDateTime > company.IssuedInvoicesSyncedUntil))
            company.IssuedInvoicesSyncedUntil = c.UtcDateTime;

        await db.SaveChangesAsync(ct);
        return new SyncResultDto(downloaded, complete, skipped);
    }

    /// <summary>
    /// Faktura jest już w aplikacji: ma ten numer KSeF albo to dokument wysłany przez aplikację (ten sam skrót SHA-256),
    /// któremu usługa wysyłki nie zdążyła jeszcze przypisać numeru - ochrona przed duplikatem.
    /// </summary>
    private async Task<bool> KnownAsync(int companyId, KsefInvoiceMetadata meta, CancellationToken ct) =>
        await db.Invoices.IgnoreQueryFilters().AnyAsync(i => i.CompanyId == companyId && i.KsefNumber == meta.KsefNumber, ct)
        || (meta.InvoiceHash is { } hash
            && await db.KsefSubmissions.IgnoreQueryFilters().AnyAsync(s => s.Invoice.CompanyId == companyId && s.InvoiceHash == hash, ct));

    /// <summary>Faktura z dokumentu XML; <c>null</c> - dokument nieobsługiwany (rodzaj inny niż VAT/KOR, nieznana stawka).</summary>
    private async Task<Invoice?> ToInvoiceAsync(Company company, KsefInvoiceMetadata meta, string xml, CancellationToken ct)
    {
        InvoiceDocumentDto doc;
        try
        {
            doc = Fa3InvoiceReader.Read(xml);
        }
        catch (Exception ex) when (ex is FormatException or System.Xml.XmlException)
        {
            return null;
        }

        var kind = doc.InvoiceType switch { "VAT" => InvoiceKind.Vat, "KOR" => InvoiceKind.Correction, _ => (InvoiceKind?)null };
        if (kind is null) return null;

        var items = new List<InvoiceItem>();
        foreach (var line in doc.Lines)
        {
            if (VatRateExtensions.FromFaCode(line.VatRate) is not { } rate) return null;
            var quantity = line.Quantity ?? 1;
            var price = line.UnitNetPrice ?? (line.NetAmount is { } net && quantity != 0 ? net / quantity : 0);
            var calc = InvoiceCalculator.CalculateLine(new LineInput(quantity, price, rate));
            items.Add(new InvoiceItem
            {
                Position = items.Count + 1, BeforeCorrection = line.BeforeCorrection, Name = Truncate(line.Name, 512) ?? "",
                Unit = Truncate(line.Unit, 50), Quantity = quantity, UnitNetPrice = price, VatRate = rate,
                NetAmount = line.NetAmount ?? calc.NetAmount, VatAmount = calc.VatAmount, GrossAmount = calc.GrossAmount
            });
        }

        var accepted = meta.AcquisitionDate.UtcDateTime;
        var invoice = new Invoice
        {
            CompanyId = company.Id,
            Origin = InvoiceOrigin.KsefImport,
            Kind = kind.Value,
            Status = InvoiceStatus.Accepted,
            Number = Truncate(doc.InvoiceNumber, 256) ?? meta.InvoiceNumber,
            IssueDate = doc.IssueDate,
            SaleDate = doc.SaleDate,
            Currency = doc.Currency,
            ExchangeRate = Currencies.IsForeign(doc.Currency) ? doc.ExchangeRate : null,
            PaymentMethod = doc.PaymentMethod is { } m && PaymentMethods.TryGetValue(m, out var method) ? method : PaymentMethod.Transfer,
            DueDate = doc.DueDate,
            PaidOn = doc.PaidOn,
            BankAccountNumber = Truncate(doc.BankAccount?.Replace(" ", ""), 34),
            Seller = Party(doc.Seller),
            Buyer = Party(doc.Buyer),
            TotalNet = doc.VatSummary.Sum(v => v.Net),
            TotalVat = doc.VatSummary.Sum(v => v.Vat ?? 0),
            TotalGross = doc.TotalGross,
            KsefNumber = meta.KsefNumber,
            KsefAcceptedAt = accepted,
            Items = items,
            KsefSubmissions =
            [
                new KsefSubmission
                {
                    SentAt = accepted, SentXml = xml,
                    InvoiceHash = meta.InvoiceHash ?? Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(xml))),
                    StatusCode = 200, StatusDescription = "Faktura wystawiona poza aplikacją - pobrana z KSeF",
                    KsefNumber = meta.KsefNumber, KsefAssignedAt = accepted, CompletedAt = time.GetUtcNow().UtcDateTime
                }
            ]
        };

        if (kind == InvoiceKind.Correction && doc.CorrectedInvoices?.FirstOrDefault() is { } corrected)
        {
            invoice.CorrectionReason = Truncate(doc.CorrectionReason, 256) ?? "";
            invoice.CorrectedInvoiceNumber = Truncate(corrected.Number, 256);
            invoice.CorrectedInvoiceIssueDate = corrected.IssueDate;
            invoice.CorrectedInvoiceKsefNumber = corrected.KsefNumber;
            // Faktura korygowana w aplikacji (wystawiona w niej lub wcześniej pobrana) - korekta zmienia jej należność
            invoice.CorrectedInvoiceId = await db.Invoices
                .Where(i => i.CompanyId == company.Id && i.KsefNumber != null && i.KsefNumber == corrected.KsefNumber)
                .Select(i => (int?)i.Id).FirstOrDefaultAsync(ct);
        }
        return invoice;
    }

    [GeneratedRegex(@"^(\d{2}-\d{3})\s+(.+)$")]
    private static partial Regex PostalAndCity();

    /// <summary>Strona z dokumentu: adres FA(3) to dwie linie - druga zwykle „00-001 Miasto”.</summary>
    private static InvoiceParty Party(DocumentPartyDto p)
    {
        var line2 = p.AddressLine2?.Trim() ?? "";
        var m = PostalAndCity().Match(line2);
        return new InvoiceParty
        {
            Name = Truncate(p.Name, 250) ?? "",
            Nip = p.Nip is { Length: 10 } nip && nip.All(char.IsAsciiDigit) ? nip : null,
            Address = new Address
            {
                Street = Truncate(p.AddressLine1, 200) ?? "",
                PostalCode = m.Success ? m.Groups[1].Value : "",
                City = Truncate(m.Success ? m.Groups[2].Value : line2, 100) ?? "",
                CountryCode = p.CountryCode ?? "PL"
            }
        };
    }

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
