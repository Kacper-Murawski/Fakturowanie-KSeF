using System.ComponentModel.DataAnnotations;

namespace FakturowanieKSeF.Models;

/// <summary>
/// Firma użytkownika (sprzedawca na wystawianych fakturach, nabywca na fakturach odbieranych z KSeF).
/// W MVP jedno konto użytkownika ma dokładnie jedną firmę (decyzja D4) - wymusza to unikalny indeks na <see cref="OwnerId"/>.
/// </summary>
public class Company : BaseEntity
{
    [Required, MaxLength(250)]
    public string Name { get; set; } = string.Empty;

    /// <summary>NIP - 10 cyfr, bez myślników.</summary>
    [Required, MaxLength(10)]
    public string Nip { get; set; } = string.Empty;

    public Address Address { get; set; } = new();

    [MaxLength(255)]
    public string? Email { get; set; }

    [MaxLength(20)]
    public string? Phone { get; set; }

    /// <summary>Domyślny numer rachunku bankowego podpowiadany na nowych fakturach.</summary>
    [MaxLength(34)]
    public string? BankAccountNumber { get; set; }

    // --- KSeF ---

    /// <summary>
    /// Token KSeF firmy (wygenerowany w Aplikacji Podatnika), przechowywany WYŁĄCZNIE w postaci zaszyfrowanej (etap 6).
    /// </summary>
    public string? KsefTokenEncrypted { get; set; }

    public DateTime? KsefTokenUpdatedAt { get; set; }

    /// <summary>Moment, do którego pobrano już faktury zakupowe z KSeF (pobieranie przyrostowe, etap 7).</summary>
    public DateTime? ReceivedInvoicesSyncedUntil { get; set; }

    // --- Właściciel i relacje ---

    [Required]
    public string OwnerId { get; set; } = string.Empty;
    public ApplicationUser Owner { get; set; } = null!;

    public List<Customer> Customers { get; set; } = [];
    public List<Invoice> Invoices { get; set; } = [];
    public List<ReceivedInvoice> ReceivedInvoices { get; set; } = [];
}
