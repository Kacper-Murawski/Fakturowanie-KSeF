using System.Text.Json.Serialization;

namespace FakturowanieKSeF.Contracts;

/// <summary>Stan rozliczenia faktury (wystawionej - należność, otrzymanej - zobowiązanie).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PaymentStatus>))]
public enum PaymentStatus
{
    /// <summary>Brak zarejestrowanych płatności.</summary>
    Unpaid,

    /// <summary>Zapłacono część kwoty brutto.</summary>
    PartiallyPaid,

    /// <summary>Zapłacono całą kwotę brutto.</summary>
    Paid
}

/// <summary>Filtr list faktur według rozliczenia.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PaymentFilter>))]
public enum PaymentFilter
{
    /// <summary>Nieopłacone w całości (także częściowo opłacone).</summary>
    Unpaid,

    /// <summary>Opłacone w całości.</summary>
    Paid,

    /// <summary>Nieopłacone w całości, z upływającym terminem płatności (po terminie).</summary>
    Overdue
}

/// <summary>Reguły rozliczeń - wspólne dla API i interfejsu.</summary>
public static class PaymentRules
{
    public static PaymentStatus Status(decimal totalGross, decimal paid) =>
        paid <= 0 && totalGross > 0 ? PaymentStatus.Unpaid
        : paid < totalGross ? PaymentStatus.PartiallyPaid
        : PaymentStatus.Paid;

    /// <summary>Po terminie: nieopłacona w całości, a termin płatności minął (dzień terminu jeszcze nie jest zaległością).</summary>
    public static bool IsOverdue(DateOnly? dueDate, decimal totalGross, decimal paid, DateOnly today) =>
        dueDate is { } due && due < today && paid < totalGross;

    /// <summary>Płatności można rejestrować dla faktur wystawionych - przekazanych do KSeF (nie dla szkiców i odrzuconych).</summary>
    public static bool AcceptsPayments(InvoiceStatus status) => status is InvoiceStatus.Pending or InvoiceStatus.Accepted;
}
