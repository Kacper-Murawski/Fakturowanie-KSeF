using System.ComponentModel.DataAnnotations;

namespace FakturowanieKSeF.Contracts.Dtos;

/// <summary>Stan logowania dwuskładnikowego konta (D41).</summary>
public sealed record TwoFactorStatusDto(bool Enabled, int RecoveryCodesLeft);

/// <summary>Dane do dodania konta w aplikacji uwierzytelniającej: klucz (do wpisania ręcznie) i kod QR (PNG, Base64).</summary>
public sealed record TwoFactorSetupDto(string SharedKey, string AuthenticatorUri, string QrPngBase64);

/// <summary>Jednorazowe kody odzyskiwania - pokazywane tylko raz, po włączeniu lub wygenerowaniu nowych.</summary>
public sealed record RecoveryCodesDto(IReadOnlyList<string> RecoveryCodes);

/// <summary>Potwierdzenie operacji kodem z aplikacji uwierzytelniającej (lub kodem odzyskiwania tam, gdzie dozwolone).</summary>
public sealed class TwoFactorCodeRequest
{
    [Required(ErrorMessage = "Podaj kod.")]
    [MaxLength(20)]
    public string Code { get; set; } = string.Empty;
}
