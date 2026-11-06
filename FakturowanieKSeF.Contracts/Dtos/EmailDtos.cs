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
