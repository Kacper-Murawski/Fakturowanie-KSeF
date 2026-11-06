using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
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
    string? KsefNumber,
    DateOnly? DueDate = null,
    /// <summary>Stan rozliczenia (null dla szkiców i faktur odrzuconych - nie są należnością).</summary>
    PaymentStatus? PaymentStatus = null,
    decimal PaidAmount = 0,
    bool IsOverdue = false,
    InvoiceKind Kind = InvoiceKind.Vat);

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
    string? ExemptionBasis,
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
    /// <summary>Ostatnia próba wysyłki do KSeF (null, jeśli faktury nie wysyłano).</summary>
    KsefSubmissionDto? LastSubmission,
    /// <summary>Wersja rekordu - przy zapisie zmian należy ją odesłać (kontrola współbieżności).</summary>
    uint Version,
    /// <summary>Rozliczenie (null dla szkiców, faktur odrzuconych i korygujących).</summary>
    PaymentInfoDto? Payment = null,
    InvoiceKind Kind = InvoiceKind.Vat,
    /// <summary>Dane korekty - tylko dla faktury korygującej (Items to wtedy stan po korekcie, sumy to różnica).</summary>
    CorrectionInfoDto? Correction = null,
    /// <summary>Faktury korygujące wystawione do tej faktury.</summary>
    IReadOnlyList<CorrectionSummaryDto>? Corrections = null,
    /// <summary>Data zapłaty, gdy faktura jest oznaczona jako zapłacona w chwili wystawienia (Zaplacono w FA(3)).</summary>
    DateOnly? PaidOn = null,
    /// <summary>Powiązanie proforma ↔ faktura VAT (D42): dla faktury VAT - proforma, z której powstała; dla proformy - wystawiona faktura.</summary>
    LinkedDocumentDto? Proforma = null,
    LinkedDocumentDto? IssuedFromProforma = null,
    /// <summary>Faktura w walucie obcej: kurs NBP i kwoty podatku w PLN per stawka (D43).</summary>
    ExchangeRateDto? ExchangeRate = null,
    IReadOnlyList<VatRateSummary>? VatSummaryPln = null);

/// <summary>Odwołanie do powiązanego dokumentu (numer i stan).</summary>
public sealed record LinkedDocumentDto(int Id, string Number, InvoiceStatus Status);

/// <summary>Faktura korygująca: dane faktury korygowanej, przyczyna, stan przed korektą i różnica per stawka.</summary>
public sealed record CorrectionInfoDto(
    int CorrectedInvoiceId,
    string CorrectedInvoiceNumber,
    DateOnly CorrectedInvoiceIssueDate,
    string? CorrectedInvoiceKsefNumber,
    string Reason,
    CorrectionType? Type,
    IReadOnlyList<InvoiceItemDto> ItemsBefore,
    decimal BeforeGross,
    decimal AfterGross);

/// <summary>Korekta widoczna przy fakturze korygowanej.</summary>
public sealed record CorrectionSummaryDto(int Id, string Number, DateOnly IssueDate, InvoiceStatus Status, decimal TotalGross);

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
    [SupportedVatRate]
    public VatRate VatRate { get; set; } = VatRate.Rate23;
}

/// <summary>
/// Stawki 4% i 3% występują w FA(3) wyłącznie dla ryczałtu dla taksówek osobowych (osobny sposób rozliczenia
/// podatku) - poza zakresem aplikacji.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SupportedVatRateAttribute : ValidationAttribute
{
    public SupportedVatRateAttribute() : base("Stawki 4% i 3% (ryczałt dla taksówek osobowych) nie są obsługiwane.") { }

    public override bool IsValid(object? value) => value is not (VatRate.Rate4 or VatRate.Rate3);
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

    /// <summary>
    /// Faktura zapłacona w całości w chwili wystawienia (np. gotówką, kartą) - data zapłaty trafia do XML FA(3)
    /// (Zaplacono, DataZaplaty). Puste = faktura do zapłaty. Nie dotyczy faktur korygujących.
    /// </summary>
    public DateOnly? PaidOn { get; set; }

    [BankAccount]
    public string? BankAccountNumber { get; set; }

    /// <summary>Podstawa prawna zwolnienia z VAT (wymagana przy pozycjach ze stawką „zw”).</summary>
    [MaxLength(256)]
    public string? ExemptionBasis { get; set; }

    public int? CustomerId { get; set; }

    public SaveInvoiceBuyerRequest? Buyer { get; set; }

    /// <summary>Pozycje faktury; dla faktury korygującej - stan PO korekcie (pusta lista = korekta do zera).</summary>
    [Required]
    [MaxLength(1000)]
    public List<SaveInvoiceItemRequest> Items { get; set; } = [];

    /// <summary>Faktura korygowana - ustawione oznacza fakturę korygującą (KOR). Nabywca i waluta pochodzą z niej.</summary>
    public int? CorrectedInvoiceId { get; set; }

    /// <summary>Przyczyna korekty (PrzyczynaKorekty), wymagana dla faktury korygującej.</summary>
    [MaxLength(256)]
    public string? CorrectionReason { get; set; }

    [EnumDataType(typeof(CorrectionType), ErrorMessage = "Nieznany typ korekty.")]
    public CorrectionType? CorrectionType { get; set; }

    [JsonIgnore]
    public bool IsCorrection => CorrectedInvoiceId is not null;

    /// <summary>Faktura proforma (tylko przy tworzeniu; rodzaju dokumentu nie można zmienić) - D42.</summary>
    public bool IsProforma { get; set; }

    /// <summary>Wersja rekordu z ostatniego odczytu (wymagana przy edycji).</summary>
    public uint? Version { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Faktura korygująca: nabywca z faktury korygowanej; pozycje mogą zniknąć (korekta do zera)
        if (IsCorrection)
        {
            if (string.IsNullOrWhiteSpace(CorrectionReason))
                yield return new ValidationResult("Podaj przyczynę korekty.", [nameof(CorrectionReason)]);
        }
        else
        {
            if (CustomerId is null && Buyer is null)
                yield return new ValidationResult("Wybierz kontrahenta lub podaj dane nabywcy.", [nameof(Buyer)]);
            if (Items.Count == 0)
                yield return new ValidationResult("Faktura musi mieć co najmniej jedną pozycję.", [nameof(Items)]);
        }

        if (DueDate is { } due && due < IssueDate)
            yield return new ValidationResult("Termin płatności nie może być wcześniejszy niż data wystawienia.", [nameof(DueDate)]);

        if (IsProforma && IsCorrection)
            yield return new ValidationResult("Faktura korygująca nie może być proformą.", [nameof(IsProforma)]);

        if (PaidOn is { } paidOn)
        {
            if (IsProforma)
                yield return new ValidationResult("Proforma nie jest fakturą - zapłatę oznacz na fakturze VAT wystawionej na jej podstawie.", [nameof(PaidOn)]);
            else if (IsCorrection)
                yield return new ValidationResult("Oznaczenie zapłaty dotyczy tylko faktur VAT, nie korekt.", [nameof(PaidOn)]);
            else if (paidOn > IssueDate)
                yield return new ValidationResult("Data zapłaty nie może być późniejsza niż data wystawienia - późniejsze wpłaty rejestruj w rozliczeniu faktury.", [nameof(PaidOn)]);
        }

        if (PaymentMethod == PaymentMethod.Transfer && DueDate is null && !IsCorrection && PaidOn is null)
            yield return new ValidationResult("Dla płatności przelewem podaj termin płatności.", [nameof(DueDate)]);

        // Waluty: PLN i waluty z tabeli A NBP (kurs pobierany przy zapisie - D43)
        if (!Currencies.Supported.Contains(Currency))
            yield return new ValidationResult("Nieobsługiwana waluta.", [nameof(Currency)]);

        if (Items.Any(i => i.VatRate == VatRate.Exempt) && string.IsNullOrWhiteSpace(ExemptionBasis))
            yield return new ValidationResult("Podaj podstawę prawną zwolnienia z VAT (np. art. 113 ust. 1 ustawy o VAT).", [nameof(ExemptionBasis)]);
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
