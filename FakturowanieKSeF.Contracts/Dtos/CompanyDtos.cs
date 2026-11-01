using System.ComponentModel.DataAnnotations;
using FakturowanieKSeF.Contracts.Validation;

namespace FakturowanieKSeF.Contracts.Dtos;

/// <summary>Dane firmy użytkownika (sprzedawcy).</summary>
public sealed record CompanyDto(
    string Name,
    string Nip,
    AddressDto Address,
    string? Email,
    string? Phone,
    string? BankAccountNumber,
    bool HasKsefToken);

/// <summary>Utworzenie lub aktualizacja danych firmy.</summary>
public sealed class SaveCompanyRequest
{
    [Required(ErrorMessage = "Podaj nazwę firmy.")]
    [MaxLength(250)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Podaj NIP.")]
    [Nip]
    public string Nip { get; set; } = string.Empty;

    [Required]
    public AddressDto Address { get; set; } = new();

    [EmailAddress(ErrorMessage = "Nieprawidłowy adres e-mail.")]
    [MaxLength(255)]
    public string? Email { get; set; }

    [MaxLength(20)]
    public string? Phone { get; set; }

    [BankAccount]
    public string? BankAccountNumber { get; set; }
}

/// <summary>Numer rachunku: 10-34 znaki alfanumeryczne (po usunięciu spacji), zgodnie z typem TNrRB w FA(3).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class BankAccountAttribute : ValidationAttribute
{
    public BankAccountAttribute() : base("Numer rachunku musi mieć od 10 do 34 znaków (litery i cyfry).") { }

    public static string Normalize(string value) => new(value.Where(c => !char.IsWhiteSpace(c)).ToArray());

    public override bool IsValid(object? value)
    {
        if (value is null || value is string { Length: 0 }) return true;
        if (value is not string s) return false;
        var n = Normalize(s);
        return n.Length is >= 10 and <= 34 && n.All(char.IsAsciiLetterOrDigit);
    }
}
