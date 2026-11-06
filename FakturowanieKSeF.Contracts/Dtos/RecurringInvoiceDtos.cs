using System.ComponentModel.DataAnnotations;

namespace FakturowanieKSeF.Contracts.Dtos;

/// <summary>Harmonogram faktury cyklicznej z danymi faktury-wzoru.</summary>
public sealed record RecurringInvoiceDto(
    int Id,
    string? Name,
    int TemplateInvoiceId,
    string TemplateNumber,
    string BuyerName,
    decimal TotalGross,
    string Currency,
    int IntervalMonths,
    DateOnly NextIssueDate,
    DateOnly? EndDate,
    bool IsActive,
    bool NotifyByEmail,
    int IssuedCount,
    DateOnly? LastIssuedOn,
    int? LastInvoiceId,
    string? LastError);

/// <summary>Utworzenie lub zmiana harmonogramu faktury cyklicznej.</summary>
public sealed class SaveRecurringInvoiceRequest : IValidatableObject
{
    public static readonly int[] AllowedIntervals = [1, 3, 6, 12];

    [MaxLength(200)]
    public string? Name { get; set; }

    /// <summary>Faktura-wzór (przy zmianie harmonogramu nie można jej zmienić).</summary>
    public int TemplateInvoiceId { get; set; }

    public int IntervalMonths { get; set; } = 1;

    [Required(ErrorMessage = "Podaj datę pierwszego wystawienia.")]
    public DateOnly? NextIssueDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public bool IsActive { get; set; } = true;

    public bool NotifyByEmail { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!AllowedIntervals.Contains(IntervalMonths))
            yield return new ValidationResult("Wybierz okres: co 1, 3, 6 lub 12 miesięcy.", [nameof(IntervalMonths)]);
        if (EndDate is { } end && NextIssueDate is { } next && end < next)
            yield return new ValidationResult("Data końcowa nie może być wcześniejsza niż data wystawienia.", [nameof(EndDate)]);
    }
}
