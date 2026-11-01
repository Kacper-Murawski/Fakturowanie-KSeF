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

    public static InvoiceListItemDto ToListItemDto(this Invoice i, decimal paid = 0, DateOnly? today = null)
    {
        var settles = PaymentRules.AcceptsPayments(i.Status) && today is not null;
        return new(i.Id, i.Number, i.IssueDate, i.Buyer.Name, i.Buyer.Nip, i.TotalNet, i.TotalGross, i.Currency, i.Status,
            i.KsefNumber, i.DueDate,
            settles ? PaymentRules.Status(i.TotalGross, paid) : null, paid,
            settles && PaymentRules.IsOverdue(i.DueDate, i.TotalGross, paid, today!.Value));
    }

    /// <summary>Rozliczenie faktury na podstawie zarejestrowanych płatności.</summary>
    public static PaymentInfoDto ToPaymentInfo(decimal totalGross, DateOnly? dueDate, IEnumerable<Payment> payments, DateOnly today)
    {
        var list = payments.OrderBy(p => p.PaidOn).ThenBy(p => p.Id).ToList();
        var paid = list.Sum(p => p.Amount);
        return new PaymentInfoDto(PaymentRules.Status(totalGross, paid), paid, Math.Max(totalGross - paid, 0),
            PaymentRules.IsOverdue(dueDate, totalGross, paid, today),
            list.Select(p => new PaymentDto(p.Id, p.PaidOn, p.Amount, p.Note)).ToList());
    }

    /// <param name="today">Dzień (czas polski) do wyznaczenia zaległości; bez niego rozliczenie nie jest zwracane.</param>
    public static InvoiceDto ToDto(this Invoice i, DateOnly? today = null)
    {
        var items = i.Items.OrderBy(x => x.Position).ToList();
        var totals = InvoiceCalculator.Calculate(items.Select(x => new LineInput(x.Quantity, x.UnitNetPrice, x.VatRate)));

        return new InvoiceDto(
            i.Id, i.Number, i.IssueDate, i.IssuePlace, i.SaleDate, i.Currency, i.PaymentMethod, i.DueDate,
            i.BankAccountNumber, i.ExemptionBasis, i.Seller.ToDto(), i.Buyer.ToDto(), i.CustomerId,
            items.Select(x => new InvoiceItemDto(x.Position, x.Name, x.Unit, x.Quantity, x.UnitNetPrice, x.VatRate,
                x.NetAmount, x.VatAmount, x.GrossAmount)).ToList(),
            totals.ByVatRate, i.TotalNet, i.TotalVat, i.TotalGross,
            i.Status, i.KsefNumber, i.KsefAcceptedAt,
            i.KsefSubmissions.OrderByDescending(s => s.Id).FirstOrDefault()?.ToDto(),
            i.Version,
            PaymentRules.AcceptsPayments(i.Status) && today is { } day ? ToPaymentInfo(i.TotalGross, i.DueDate, i.Payments, day) : null);
    }

    public static KsefSubmissionDto ToDto(this KsefSubmission s) => new(
        s.SentAt, s.StatusCode, s.StatusDescription,
        string.IsNullOrEmpty(s.StatusDetails) ? [] : s.StatusDetails.Split('\n'),
        s.KsefNumber, s.KsefAssignedAt, s.UpoXml is not null, s.CompletedAt, s.FailedAttempts, s.LastError);
}
