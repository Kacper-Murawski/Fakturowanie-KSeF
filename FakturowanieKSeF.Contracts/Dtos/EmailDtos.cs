using System.ComponentModel.DataAnnotations;

namespace FakturowanieKSeF.Contracts.Dtos;

/// <summary>Wiadomość z historii wysyłek (bez treści).</summary>
public sealed record EmailMessageDto(
    int Id,
    EmailKind Kind,
    string To,
    string Subject,
    EmailStatus Status,
    DateTime CreatedAt,
    DateTime? SentAt,
    int Attempts,
    string? LastError);

/// <summary>Wysłanie faktury e-mailem do nabywcy.</summary>
public sealed class SendInvoiceEmailRequest
{
    [Required(ErrorMessage = "Podaj adres e-mail odbiorcy.")]
    [EmailAddress(ErrorMessage = "Nieprawidłowy adres e-mail.")]
    [MaxLength(254)]
    public string To { get; set; } = string.Empty;

    /// <summary>Dodatkowa treść od wystawcy, umieszczana w wiadomości.</summary>
    [MaxLength(2000)]
    public string? Message { get; set; }
}

/// <summary>Ustawienia powiadomień e-mail firmy (D37).</summary>
/// <param name="AccountEmail">Adres konta - domyślny adres powiadomień, gdy <paramref name="NotificationEmail"/> jest pusty.</param>
public sealed record NotificationSettingsDto(
    bool AutoReminders,
    int ReminderDaysAfterDue,
    bool EmailConfigured,
    bool NotifyReceivedInvoices = false,
    string? NotificationEmail = null,
    string? AccountEmail = null);

/// <summary>Zmiana ustawień powiadomień.</summary>
public sealed class SaveNotificationSettingsRequest
{
    /// <summary>Automatyczne przypomnienia o płatności po terminie.</summary>
    public bool AutoReminders { get; set; }

    [Range(0, 60, ErrorMessage = "Podaj liczbę dni od 0 do 60.")]
    public int ReminderDaysAfterDue { get; set; } = 3;

    /// <summary>Powiadomienia o nowych fakturach kosztowych z KSeF.</summary>
    public bool NotifyReceivedInvoices { get; set; }

    /// <summary>Adres powiadomień (puste - adres konta).</summary>
    [EmailAddress(ErrorMessage = "Nieprawidłowy adres e-mail.")]
    [MaxLength(254)]
    public string? NotificationEmail { get; set; }
}
