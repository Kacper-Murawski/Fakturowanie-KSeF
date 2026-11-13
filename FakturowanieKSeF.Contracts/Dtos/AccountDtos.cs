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

/// <summary>
/// Operacja zdejmująca lub osłabiająca zabezpieczenie konta (wyłączenie 2FA, nowe kody odzyskiwania): wymaga hasła
/// i kodu - sama przejęta sesja nie wystarczy (D41, D52).
/// </summary>
public sealed class TwoFactorConfirmRequest
{
    [Required(ErrorMessage = "Podaj hasło.")]
    [MaxLength(200)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Podaj kod.")]
    [MaxLength(20)]
    public string Code { get; set; } = string.Empty;
}

/// <summary>
/// Zmiana adresu e-mail konta (D53): wymaga hasła i - przy włączonym 2FA - kodu z aplikacji. Nowy adres zaczyna
/// obowiązywać dopiero po kliknięciu linku wysłanego na niego; dotychczasowy adres dostaje powiadomienie.
/// </summary>
public sealed class ChangeEmailRequest
{
    [Required(ErrorMessage = "Podaj nowy adres e-mail.")]
    [EmailAddress(ErrorMessage = "Nieprawidłowy adres e-mail.")]
    [MaxLength(254)]
    public string NewEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Podaj hasło.")]
    [MaxLength(200)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Kod z aplikacji uwierzytelniającej - wymagany, gdy konto ma włączone 2FA.</summary>
    [MaxLength(20)]
    public string? Code { get; set; }
}
