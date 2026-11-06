using System.ComponentModel.DataAnnotations;

namespace FakturowanieKSeF.Contracts.Dtos;

/// <summary>Towar lub usługa z katalogu (cena netto i stawka VAT podpowiadane na fakturze).</summary>
public sealed record ProductDto(int Id, string Name, string? Unit, decimal UnitNetPrice, VatRate VatRate);

/// <summary>Dodanie lub edycja pozycji katalogu. Ograniczenia jak dla pozycji faktury (P_7, P_8A, P_9A).</summary>
public sealed class SaveProductRequest
{
    [Required(ErrorMessage = "Podaj nazwę towaru lub usługi.")]
    [MaxLength(512)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Unit { get; set; }

    [Range(typeof(decimal), "0", "99999999999999", ErrorMessage = "Cena nie może być ujemna.",
        ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal UnitNetPrice { get; set; }

    [EnumDataType(typeof(VatRate), ErrorMessage = "Nieznana stawka VAT.")]
    [SupportedVatRate]
    public VatRate VatRate { get; set; } = VatRate.Rate23;
}
