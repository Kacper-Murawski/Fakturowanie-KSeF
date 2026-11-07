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

    /// <summary>
    /// Punkt kontrolny synchronizacji faktur otrzymanych: data trwałego zapisu w KSeF, do której pobrano już
    /// wszystkie faktury (wartość HWM z KSeF). Kolejna synchronizacja zaczyna się od tego momentu.
    /// </summary>
    public DateTime? ReceivedInvoicesSyncedUntil { get; set; }

    /// <summary>Moment ostatniej próby synchronizacji (także ograniczenie częstotliwości synchronizacji ręcznej).</summary>
    public DateTime? ReceivedInvoicesLastSyncAt { get; set; }

    /// <summary>Błąd ostatniej synchronizacji (null - zakończona poprawnie).</summary>
    [MaxLength(1000)]
    public string? ReceivedInvoicesLastSyncError { get; set; }

    // ---------------- import faktur sprzedażowych wystawionych poza aplikacją (D44) ----------------

    /// <summary>Punkt kontrolny importu (HWM) faktur, w których firma jest sprzedawcą.</summary>
    public DateTime? IssuedInvoicesSyncedUntil { get; set; }

    public DateTime? IssuedInvoicesLastSyncAt { get; set; }

    [MaxLength(1000)]
    public string? IssuedInvoicesLastSyncError { get; set; }

    // ---------------- powiadomienia e-mail (D37) ----------------

    /// <summary>Automatyczne przypomnienia o płatności dla nabywców faktur nieopłaconych po terminie.</summary>
    public bool AutoRemindersEnabled { get; set; }

    /// <summary>Po ilu dniach od terminu płatności wysłać pierwsze automatyczne przypomnienie.</summary>
    public int ReminderDaysAfterDue { get; set; } = 3;

    /// <summary>Powiadomienie e-mail o nowych fakturach kosztowych pobranych z KSeF (D38).</summary>
    public bool NotifyReceivedInvoices { get; set; }

    /// <summary>Adres powiadomień dla użytkownika; puste - adres e-mail konta właściciela.</summary>
    [MaxLength(254)]
    public string? NotificationEmail { get; set; }

    // --- Właściciel i relacje ---

    [Required]
    public string OwnerId { get; set; } = string.Empty;
    public ApplicationUser Owner { get; set; } = null!;

    public List<Customer> Customers { get; set; } = [];
    public List<Invoice> Invoices { get; set; } = [];
    public List<ReceivedInvoice> ReceivedInvoices { get; set; } = [];
}
