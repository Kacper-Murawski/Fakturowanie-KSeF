using Microsoft.AspNetCore.DataProtection;

namespace FakturowanieKSeF.Api.Services;

/// <summary>
/// Szyfrowanie tokenów KSeF przechowywanych w bazie (ASP.NET Core Data Protection: AES-256-CBC + HMAC-SHA256,
/// klucze rotowane automatycznie). Token jest odszyfrowywany wyłącznie w pamięci serwera, w chwili komunikacji
/// z KSeF; nigdy nie jest zwracany do przeglądarki ani zapisywany w logach.
/// </summary>
public sealed class KsefTokenProtector(IDataProtectionProvider provider)
{
    // Osobny „cel” (purpose) - klucze pochodne są inne niż np. dla ciasteczek sesji
    private readonly IDataProtector _protector = provider.CreateProtector("FakturowanieKSeF.KsefToken.v1");

    public string Protect(string token) => _protector.Protect(token);

    public string Unprotect(string protectedToken) => _protector.Unprotect(protectedToken);
}
