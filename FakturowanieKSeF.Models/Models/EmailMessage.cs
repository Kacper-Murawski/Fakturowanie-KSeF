using System.ComponentModel.DataAnnotations;

namespace FakturowanieKSeF.Models;

/// <summary>
/// Wiadomość e-mail w kolejce wysyłki (wzorzec „outbox”, D36). Endpoint tylko zapisuje wiadomość, a wysyła ją
/// usługa w tle - chwilowa awaria serwera poczty nie gubi wiadomości, a próby są ponawiane. Załączniki
/// (PDF i XML faktury) nie są przechowywane: powstają przy wysyłce z faktury wskazanej w <see cref="InvoiceId"/>.
/// </summary>
public class EmailMessage
{
    public int Id { get; set; }

    /// <summary>Firma, w imieniu której wysłano wiadomość - izolacja danych i dzienny limit wysyłek.</summary>
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public EmailKind Kind { get; set; }

    [Required, MaxLength(254)]
    public string To { get; set; } = string.Empty;

    [Required, MaxLength(300)]
    public string Subject { get; set; } = string.Empty;

    /// <summary>Treść (zwykły tekst - bez HTML, więc bez ryzyka wstrzyknięcia treści aktywnej).</summary>
    [Required]
    public string Body { get; set; } = string.Empty;

    /// <summary>Faktura, której PDF i XML są dołączane jako załączniki (opcjonalnie).</summary>
    public int? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    public EmailStatus Status { get; set; } = EmailStatus.Pending;

    public int Attempts { get; set; }

    /// <summary>Najwcześniejsza chwila kolejnej próby (UTC) - odstępy rosną po każdym błędzie.</summary>
    public DateTime NextAttemptAt { get; set; }

    [MaxLength(1000)]
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? SentAt { get; set; }
}
