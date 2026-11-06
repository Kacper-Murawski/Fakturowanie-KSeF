using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace FakturowanieKSeF.Client.Services;

/// <summary>
/// Stan uwierzytelnienia w przeglądarce oparty na ciasteczku sesji wydawanym przez ASP.NET Core Identity.
/// O tym, czy użytkownik jest zalogowany, rozstrzyga serwer (/api/auth/manage/info) - klient nie przechowuje
/// żadnych danych uwierzytelniających.
/// </summary>
public sealed class AuthService(HttpClient http) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));
    private AuthenticationState? _cached;

    private sealed record UserInfo(string Email);

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_cached is not null) return _cached;

        try
        {
            var response = await http.GetAsync("api/auth/manage/info");
            if (response.IsSuccessStatusCode && await response.Content.ReadFromJsonAsync<UserInfo>() is { } info)
            {
                var identity = new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, info.Email), new Claim(ClaimTypes.Email, info.Email)],
                    authenticationType: "Cookie");
                return _cached = new AuthenticationState(new ClaimsPrincipal(identity));
            }
        }
        catch (HttpRequestException)
        {
            // brak połączenia - traktujemy jak niezalogowanego
        }

        return _cached = Anonymous;
    }

    /// <summary>Wynik logowania: sukces, wymagany drugi składnik (kod z aplikacji) albo błąd z komunikatem.</summary>
    public sealed record LoginResult(bool Succeeded, bool RequiresTwoFactor = false, string? Error = null);

    /// <summary>
    /// Logowanie. Przy włączonym 2FA pierwsza próba (samo hasło) zwraca <see cref="LoginResult.RequiresTwoFactor"/>;
    /// kolejna przesyła hasło razem z kodem z aplikacji lub kodem odzyskiwania (D41).
    /// </summary>
    public async Task<LoginResult> LoginAsync(string email, string password, string? twoFactorCode = null, string? recoveryCode = null)
    {
        var response = await http.PostAsJsonAsync("api/auth/login?useCookies=true", new
        {
            email, password,
            twoFactorCode = Clean(twoFactorCode),
            twoFactorRecoveryCode = string.IsNullOrWhiteSpace(recoveryCode) ? null : recoveryCode.Replace(" ", "").Trim()
        });
        if (!response.IsSuccessStatusCode)
        {
            var problem = await ApiClient.ReadProblemAsync(response);
            return problem.Detail switch
            {
                "LockedOut" => new(false, Error: "Konto zostało tymczasowo zablokowane po kilku nieudanych próbach. Spróbuj za 15 minut."),
                "RequiresTwoFactor" when twoFactorCode is null && recoveryCode is null => new(false, RequiresTwoFactor: true),
                // Błędny drugi składnik: Identity zwraca „Failed” - zostajemy na kroku kodu
                _ when twoFactorCode is not null || recoveryCode is not null =>
                    new(false, RequiresTwoFactor: true, Error: "Nieprawidłowy kod. Wpisz aktualny kod z aplikacji."),
                _ => new(false, Error: "Nieprawidłowy e-mail lub hasło.")
            };
        }

        Refresh();
        return new(true);
    }

    private static string? Clean(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : code.Replace(" ", "").Replace("-", "").Trim();

    /// <summary>Rejestracja; zwraca listę błędów (pustą przy sukcesie).</summary>
    public async Task<IReadOnlyList<string>> RegisterAsync(string email, string password)
    {
        var response = await http.PostAsJsonAsync("api/auth/register", new { email, password });
        if (response.IsSuccessStatusCode) return [];

        var problem = await ApiClient.ReadProblemAsync(response);
        var errors = problem.Errors.SelectMany(e => e.Value).Distinct().ToList();
        return errors.Count > 0 ? errors : [problem.Title];
    }

    public async Task LogoutAsync()
    {
        await http.PostAsync("api/auth/logout", null);
        Refresh();
    }

    public void Refresh()
    {
        _cached = null;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
