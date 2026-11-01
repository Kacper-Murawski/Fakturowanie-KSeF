using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FakturowanieKSeF.Models;

/// <summary>
/// Płatność zarejestrowana przez użytkownika: wpłata nabywcy za fakturę wystawioną albo zapłata za fakturę otrzymaną.
/// Dotyczy dokładnie jednej z nich (ograniczenie CHECK w bazie). Płatność to zapis pomocniczy użytkownika,
/// a nie dokument podatkowy - błędnie zarejestrowaną można usunąć (usunięcie fizyczne).
/// </summary>
public class Payment
{
    public int Id { get; set; }

    /// <summary>Firma właściciel - warunek izolacji danych w zapytaniach.</summary>
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public int? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    public int? ReceivedInvoiceId { get; set; }
    public ReceivedInvoice? ReceivedInvoice { get; set; }

    public DateOnly PaidOn { get; set; }

    /// <summary>Kwota w walucie faktury.</summary>
    [Column(TypeName = "numeric(18,2)")]
    public decimal Amount { get; set; }

    [MaxLength(200)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }
}
