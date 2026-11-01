using System.ComponentModel.DataAnnotations;
using FakturowanieKSeF.Contracts.Validation;

namespace FakturowanieKSeF.Contracts.Dtos;

public sealed record CustomerDto(
    int Id,
    string Name,
    string? Nip,
    bool IsCompany,
    AddressDto Address,
    string? Email);

public sealed class SaveCustomerRequest : IValidatableObject
{
    [Required(ErrorMessage = "Podaj nazwę kontrahenta.")]
    [MaxLength(250)]
    public string Name { get; set; } = string.Empty;

    /// <summary>true = firma (wymagany NIP), false = osoba prywatna.</summary>
    public bool IsCompany { get; set; } = true;

    [Nip]
    public string? Nip { get; set; }

    [Required]
    public AddressDto Address { get; set; } = new();

    [EmailAddress(ErrorMessage = "Nieprawidłowy adres e-mail.")]
    [MaxLength(255)]
    public string? Email { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (IsCompany && string.IsNullOrWhiteSpace(Nip))
            yield return new ValidationResult("Dla kontrahenta będącego firmą podaj NIP.", [nameof(Nip)]);
    }
}
