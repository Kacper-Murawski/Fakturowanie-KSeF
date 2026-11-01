using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace FakturowanieKSeF.Contracts.Dtos;

/// <summary>Adres w formacie zgodnym z KSeF (kod kraju ISO, ulica, kod pocztowy, miejscowość).</summary>
public sealed class AddressDto : IValidatableObject
{
    [Required(ErrorMessage = "Podaj ulicę i numer.")]
    [MaxLength(200)]
    public string Street { get; set; } = string.Empty;

    [Required(ErrorMessage = "Podaj kod pocztowy.")]
    [MaxLength(10)]
    public string PostalCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Podaj miejscowość.")]
    [MaxLength(100)]
    public string City { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^[A-Z]{2}$", ErrorMessage = "Kod kraju to dwie wielkie litery, np. PL.")]
    public string CountryCode { get; set; } = "PL";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CountryCode == "PL" && !Regex.IsMatch(PostalCode ?? "", "^[0-9]{2}-[0-9]{3}$"))
            yield return new ValidationResult("Polski kod pocztowy ma format 00-000.", [nameof(PostalCode)]);
    }
}

/// <summary>Strona wyników listy (stronicowanie).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
