using System.ComponentModel.DataAnnotations;

namespace FakturowanieKSeF.Contracts.Dtos;

/// <summary>Zarejestrowana płatność (wpłata od nabywcy lub zapłata sprzedawcy).</summary>
public sealed record PaymentDto(int Id, DateOnly PaidOn, decimal Amount, string? Note);

/// <summary>Rozliczenie faktury: stan, kwoty i historia płatności.</summary>
public sealed record PaymentInfoDto(
    PaymentStatus Status,
    decimal PaidAmount,
    decimal Remaining,
    bool IsOverdue,
    IReadOnlyList<PaymentDto> Payments);

/// <summary>Rejestracja płatności faktury.</summary>
public sealed class AddPaymentRequest
{
    [Required(ErrorMessage = "Podaj datę płatności.")]
    public DateOnly? PaidOn { get; set; }

    [Range(typeof(decimal), "0.01", "99999999999999", ErrorMessage = "Kwota musi być większa od zera.",
        ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal Amount { get; set; }

    [MaxLength(200)]
    public string? Note { get; set; }
}

/// <summary>Podsumowanie rozliczeń na pulpicie (kwoty w PLN).</summary>
/// <param name="ReceivablesTotal">Należności: pozostało do zapłaty przez nabywców.</param>
/// <param name="PayablesTotal">Zobowiązania: pozostało do zapłaty sprzedawcom (faktury otrzymane w PLN).</param>
public sealed record PaymentsSummaryDto(
    decimal ReceivablesTotal, int ReceivablesCount, decimal ReceivablesOverdue, int ReceivablesOverdueCount,
    decimal PayablesTotal, int PayablesCount, decimal PayablesOverdue, int PayablesOverdueCount);
