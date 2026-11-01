using System.ComponentModel.DataAnnotations;

namespace FakturowanieKSeF.Models;

/// <summary>
/// Pojedyncza próba wysłania faktury do KSeF wraz z jej wynikiem. Faktura może mieć kilka prób
/// (np. odrzucenie z powodu błędu walidacji, poprawka, ponowna wysyłka) - historia jest zachowywana.
/// Pola wynikają z przepływu potwierdzonego w spike'u (docs/spike-ksef).
/// </summary>
public class KsefSubmission
{
    public int Id { get; set; }

    public int InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;

    /// <summary>Moment wysłania faktury do KSeF (UTC).</summary>
    public DateTime SentAt { get; set; }

    /// <summary>Dokładnie ten XML FA(3), który został wysłany - prawnie wiążąca postać faktury.</summary>
    [Required]
    public string SentXml { get; set; } = string.Empty;

    /// <summary>Skrót SHA-256 wysłanego XML (Base64) - potrzebny m.in. do kodu QR weryfikacji faktury.</summary>
    [Required, MaxLength(64)]
    public string InvoiceHash { get; set; } = string.Empty;

    /// <summary>Numer referencyjny sesji interaktywnej KSeF.</summary>
    [MaxLength(64)]
    public string? SessionReferenceNumber { get; set; }

    /// <summary>Numer referencyjny faktury nadany przez KSeF przy przyjęciu do przetwarzania.</summary>
    [MaxLength(64)]
    public string? InvoiceReferenceNumber { get; set; }

    /// <summary>Kod statusu przetwarzania z KSeF (np. 150 - w toku, 200 - sukces, 4xx - błąd).</summary>
    public int? StatusCode { get; set; }

    [MaxLength(500)]
    public string? StatusDescription { get; set; }

    /// <summary>Szczegóły błędów zwrócone przez KSeF (np. błędy walidacji XML).</summary>
    public string? StatusDetails { get; set; }

    /// <summary>Numer KSeF - nadawany przy sukcesie.</summary>
    [MaxLength(40)]
    public string? KsefNumber { get; set; }

    /// <summary>Data nadania numeru KSeF.</summary>
    public DateTime? KsefAssignedAt { get; set; }

    /// <summary>UPO - urzędowe poświadczenie odbioru (XML podpisany przez MF).</summary>
    public string? UpoXml { get; set; }

    /// <summary>Moment zakończenia przetwarzania (sukces lub odrzucenie).</summary>
    public DateTime? CompletedAt { get; set; }
}
