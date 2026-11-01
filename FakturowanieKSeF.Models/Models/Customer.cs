using System.ComponentModel.DataAnnotations;

namespace FakturowanieKSeF.Models;

/// <summary>
/// Kontrahent (nabywca) z książki adresowej firmy. Dane kontrahenta są KOPIOWANE na fakturę w chwili jej
/// wystawienia (<see cref="Invoice.Buyer"/>), więc późniejsza zmiana kontrahenta nie zmienia wystawionych faktur.
/// </summary>
public class Customer : BaseEntity
{
    [Required, MaxLength(250)]
    public string Name { get; set; } = string.Empty;

    /// <summary>NIP (10 cyfr) - wymagany dla firm (B2B), pusty dla osób prywatnych (B2C).</summary>
    [MaxLength(10)]
    public string? Nip { get; set; }

    /// <summary>true = firma (B2B), false = osoba prywatna (B2C).</summary>
    public bool IsCompany { get; set; } = true;

    public Address Address { get; set; } = new();

    [MaxLength(255)]
    public string? Email { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
}
