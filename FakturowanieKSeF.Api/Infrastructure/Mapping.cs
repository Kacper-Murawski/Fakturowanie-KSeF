using FakturowanieKSeF.Contracts.Calculation;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Contracts.Validation;
using FakturowanieKSeF.Models;

namespace FakturowanieKSeF.Api.Infrastructure;

/// <summary>Mapowanie encji na DTO i odwrotnie (ręczne - jawne i łatwe do prześledzenia).</summary>
public static class Mapping
{
    public static AddressDto ToDto(this Address a) => new()
    {
        Street = a.Street, PostalCode = a.PostalCode, City = a.City, CountryCode = a.CountryCode
    };

    public static Address ToEntity(this AddressDto a) => new()
    {
        Street = a.Street.Trim(), PostalCode = a.PostalCode.Trim(), City = a.City.Trim(), CountryCode = a.CountryCode
    };

    public static Address Copy(this Address a) => new()
    {
        Street = a.Street, PostalCode = a.PostalCode, City = a.City, CountryCode = a.CountryCode
    };

    public static string? NormalizeNip(string? nip) =>
        string.IsNullOrWhiteSpace(nip) ? null : NipValidator.Normalize(nip);

    public static string? NormalizeBankAccount(string? account) =>
        string.IsNullOrWhiteSpace(account) ? null : BankAccountAttribute.Normalize(account);

    public static string? EmptyToNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static CompanyDto ToDto(this Company c) =>
        new(c.Name, c.Nip, c.Address.ToDto(), c.Email, c.Phone, c.BankAccountNumber, c.KsefTokenEncrypted is not null);

    public static CustomerDto ToDto(this Customer c) =>
        new(c.Id, c.Name, c.Nip, c.IsCompany, c.Address.ToDto(), c.Email);

    public static InvoicePartyDto ToDto(this InvoiceParty p) => new(p.Name, p.Nip, p.Address.ToDto());

    /// <summary>Czy faktura podlega rozliczeniu (wpłatom): faktura VAT przekazana do KSeF; korekty zmieniają kwotę należną faktury korygowanej.</summary>
    public static bool Settles(this Invoice i) => i.Kind == InvoiceKind.Vat && PaymentRules.AcceptsPayments(i.Status);

    /// <summary>Korekty wliczane do kwoty należnej - przekazane do KSeF (wysłane lub przyjęte).</summary>
    public static bool CountsAsIssued(this Invoice correction) => correction.Status is InvoiceStatus.Pending or InvoiceStatus.Accepted;

    /// <summary>Kwota należna: brutto faktury powiększone o różnice z korekt (wymaga załadowanych <see cref="Invoice.Corrections"/>).</summary>
    public static decimal TotalDue(this Invoice i) => i.TotalGross + i.Corrections.Where(CountsAsIssued).Sum(c => c.TotalGross);

    /// <param name="totalDue">Kwota należna po korektach (z zapytania); domyślnie brutto faktury.</param>
    public static InvoiceListItemDto ToListItemDto(this Invoice i, decimal paid = 0, DateOnly? today = null, decimal? totalDue = null)
    {
        var settles = i.Settles() && today is not null;
        var due = totalDue ?? i.TotalGross;
        return new(i.Id, i.Number, i.IssueDate, i.Buyer.Name, i.Buyer.Nip, i.TotalNet, i.TotalGross, i.Currency, i.Status,
            i.KsefNumber, i.DueDate,
            settles ? PaymentRules.Status(due, paid) : null, paid,
            settles && PaymentRules.IsOverdue(i.DueDate, due, paid, today!.Value),
            i.Kind);
    }

    /// <summary>Rozliczenie faktury na podstawie zarejestrowanych płatności.</summary>
    public static PaymentInfoDto ToPaymentInfo(decimal totalDue, DateOnly? dueDate, IEnumerable<Payment> payments, DateOnly today)
    {
        var list = payments.OrderBy(p => p.PaidOn).ThenBy(p => p.Id).ToList();
        var paid = list.Sum(p => p.Amount);
        return new PaymentInfoDto(PaymentRules.Status(totalDue, paid), paid, Math.Max(totalDue - paid, 0),
            PaymentRules.IsOverdue(dueDate, totalDue, paid, today),
            list.Select(p => new PaymentDto(p.Id, p.PaidOn, p.Amount, p.Note, p.FromInvoice)).ToList(),
            totalDue, Math.Max(paid - totalDue, 0));
    }

    private static InvoiceItemDto ToDto(this InvoiceItem x, int position) =>
        new(position, x.Name, x.Unit, x.Quantity, x.UnitNetPrice, x.VatRate, x.NetAmount, x.VatAmount, x.GrossAmount);

    /// <param name="today">Dzień (czas polski) do wyznaczenia zaległości; bez niego rozliczenie nie jest zwracane.</param>
    public static InvoiceDto ToDto(this Invoice i, DateOnly? today = null)
    {
        var ordered = i.Items.OrderBy(x => x.Position).ToList();
        var before = ordered.Where(x => x.BeforeCorrection).ToList();
        var after = ordered.Where(x => !x.BeforeCorrection).ToList();
        LineInput Input(InvoiceItem x) => new(x.Quantity, x.UnitNetPrice, x.VatRate);

        // Faktura korygująca: podsumowanie per stawka to różnica po - przed (jak w P_13_x / P_14_x)
        IReadOnlyList<VatRateSummary> vatSummary;
        CorrectionInfoDto? correction = null;
        if (i.Kind == InvoiceKind.Correction)
        {
            var totals = InvoiceCalculator.CalculateCorrection(before.Select(Input), after.Select(Input));
            vatSummary = totals.Difference;
            correction = new CorrectionInfoDto(i.CorrectedInvoiceId ?? 0, i.CorrectedInvoiceNumber ?? "",
                i.CorrectedInvoiceIssueDate ?? default, i.CorrectedInvoiceKsefNumber, i.CorrectionReason ?? "", i.CorrectionType,
                before.Select((x, n) => x.ToDto(n + 1)).ToList(), totals.Before.TotalGross, totals.After.TotalGross);
        }
        else
        {
            vatSummary = InvoiceCalculator.Calculate(after.Select(Input)).ByVatRate;
        }

        return new InvoiceDto(
            i.Id, i.Number, i.IssueDate, i.IssuePlace, i.SaleDate, i.Currency, i.PaymentMethod, i.DueDate,
            i.BankAccountNumber, i.ExemptionBasis, i.Seller.ToDto(), i.Buyer.ToDto(), i.CustomerId,
            after.Select((x, n) => x.ToDto(n + 1)).ToList(),
            vatSummary, i.TotalNet, i.TotalVat, i.TotalGross,
            i.Status, i.KsefNumber, i.KsefAcceptedAt,
            i.KsefSubmissions.OrderByDescending(s => s.Id).FirstOrDefault()?.ToDto(),
            i.Version,
            i.Settles() && today is { } day ? ToPaymentInfo(i.TotalDue(), i.DueDate, i.Payments, day) : null,
            i.Kind,
            correction,
            i.Corrections.OrderBy(c => c.Id)
                .Select(c => new CorrectionSummaryDto(c.Id, c.Number, c.IssueDate, c.Status, c.TotalGross)).ToList(),
            i.PaidOn,
            ExchangeRate: i.ExchangeRate is { } rate && Currencies.IsForeign(i.Currency)
                ? new ExchangeRateDto(i.Currency, rate, i.ExchangeRateDate ?? default, i.ExchangeRateTable ?? "")
                : null,
            VatSummaryPln: i.ExchangeRate is { } r && Currencies.IsForeign(i.Currency) ? InvoiceCalculator.VatInPln(vatSummary, r) : null);
    }

    public static KsefSubmissionDto ToDto(this KsefSubmission s) => new(
        s.SentAt, s.StatusCode, s.StatusDescription,
        string.IsNullOrEmpty(s.StatusDetails) ? [] : s.StatusDetails.Split('\n'),
        s.KsefNumber, s.KsefAssignedAt, s.UpoXml is not null, s.CompletedAt, s.FailedAttempts, s.LastError);
}
