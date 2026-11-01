using System.ComponentModel.DataAnnotations;
using FakturowanieKSeF.Contracts.Calculation;
using FakturowanieKSeF.Contracts.Validation;

namespace FakturowanieKSeF.Contracts.Dtos;

/// <summary>Strona faktury (sprzedawca lub nabywca) - dane zapisane na fakturze.</summary>
public sealed record InvoicePartyDto(string Name, string? Nip, AddressDto Address);

public sealed record InvoiceItemDto(
    int Position,
    string Name,
    string? Unit,
    decimal Quantity,
    decimal UnitNetPrice,
    VatRate VatRate,
    decimal NetAmount,
    decimal VatAmount,
    decimal GrossAmount);

/// <summary>Wiersz listy faktur.</summary>
public sealed record InvoiceListItemDto(
    int Id,
    string Number,
    DateOnly IssueDate,
    string BuyerName,
    string? BuyerNip,
    decimal TotalNet,
    decimal TotalGross,
    string Currency,
    InvoiceStatus Status,
    string? KsefNumber);

/// <summary>Pełne dane faktury.</summary>
public sealed record InvoiceDto(
    int Id,
    string Number,
    DateOnly IssueDate,
    string? IssuePlace,
    DateOnly? SaleDate,
    string Currency,
    PaymentMethod PaymentMethod,
    DateOnly? DueDate,
    string? BankAccountNumber,
    InvoicePartyDto Seller,
    InvoicePartyDto Buyer,
    int? CustomerId,
    IReadOnlyList<InvoiceItemDto> Items,
    IReadOnlyList<VatRateSummary> VatSummary,
    decimal TotalNet,
    decimal TotalVat,
    decimal TotalGross,
    InvoiceStatus Status,
    string? KsefNumber,
    DateTime? KsefAcceptedAt,
    /// <summary>Wersja rekordu - przy zapisie zmian należy ją odesłać (kontrola współbieżności).</summary>
    uint Version);

public sealed class SaveInvoiceItemRequest
{
    [Required(ErrorMessage = "Podaj nazwę towaru lub usługi.")]
    [MaxLength(512)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Unit { get; set; }

    // Granice zakresu zapisane z kropką - parsowane niezależnie od języka (w pl-PL separatorem jest przecinek)
    [Range(typeof(decimal), "0.000001", "999999999999", ErrorMessage = "Ilość musi być większa od zera.",
        ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal Quantity { get; set; } = 1;

    [Range(typeof(decimal), "0", "99999999999999", ErrorMessage = "Cena nie może być ujemna.",
        ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal UnitNetPrice { get; set; }

    [EnumDataType(typeof(VatRate), ErrorMessage = "Nieznana stawka VAT.")]
    public VatRate VatRate { get; set; } = VatRate.Rate23;
}

/// <summary>
/// Utworzenie lub edycja faktury (szkicu). Nabywcę wskazuje się przez <see cref="CustomerId"/>
/// (dane kopiowane z książki adresowej) albo podaje bezpośrednio w <see cref="Buyer"/>.
/// Dane sprzedawcy pobierane są z profilu firmy. Kwoty liczy serwer.
/// </summary>
public sealed class SaveInvoiceRequest : IValidatableObject
{
    /// <summary>Numer faktury; puste = nadanie automatyczne (np. FV/1/11/2026).</summary>
    [MaxLength(256)]
    public string? Number { get; set; }

    public DateOnly IssueDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [MaxLength(100)]
    public string? IssuePlace { get; set; }

    public DateOnly? SaleDate { get; set; }

    [Required]
    [RegularExpression("^[A-Z]{3}$", ErrorMessage = "Kod waluty to trzy wielkie litery, np. PLN.")]
    public string Currency { get; set; } = "PLN";

    [EnumDataType(typeof(PaymentMethod), ErrorMessage = "Nieznana forma płatności.")]
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Transfer;

    public DateOnly? DueDate { get; set; }

    [BankAccount]
    public string? BankAccountNumber { get; set; }

    public int? CustomerId { get; set; }

    public SaveInvoiceBuyerRequest? Buyer { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Faktura musi mieć co najmniej jedną pozycję.")]
    [MaxLength(1000)]
    public List<SaveInvoiceItemRequest> Items { get; set; } = [];

    /// <summary>Wersja rekordu z ostatniego odczytu (wymagana przy edycji).</summary>
    public uint? Version { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CustomerId is null && Buyer is null)
            yield return new ValidationResult("Wybierz kontrahenta lub podaj dane nabywcy.", [nameof(Buyer)]);

        if (DueDate is { } due && due < IssueDate)
            yield return new ValidationResult("Termin płatności nie może być wcześniejszy niż data wystawienia.", [nameof(DueDate)]);

        if (PaymentMethod == PaymentMethod.Transfer && DueDate is null)
            yield return new ValidationResult("Dla płatności przelewem podaj termin płatności.", [nameof(DueDate)]);
    }
}

public sealed class SaveInvoiceBuyerRequest
{
    [Required(ErrorMessage = "Podaj nazwę nabywcy.")]
    [MaxLength(250)]
    public string Name { get; set; } = string.Empty;

    [Nip]
    public string? Nip { get; set; }

    [Required]
    public AddressDto Address { get; set; } = new();
}
