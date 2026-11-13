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
    public sealed record LoginResult(bool Succeeded, bool RequiresTwoFactor = false, string? Error = null, bool EmailNotConfirmed = false);

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
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            return new(false, RequiresTwoFactor: twoFactorCode is not null || recoveryCode is not null,
                Error: "Zbyt wiele prób logowania z tego adresu. Odczekaj chwilę (około minuty) i spróbuj ponownie.");
        if (!response.IsSuccessStatusCode)
        {
            var problem = await ApiClient.ReadProblemAsync(response);
            return problem.Detail switch
            {
                "LockedOut" => new(false, Error: "Konto zostało tymczasowo zablokowane po kilku nieudanych próbach. Spróbuj za 15 minut."),
                // Poprawne hasło, ale adres e-mail nie został jeszcze potwierdzony (D53)
                "NotAllowed" => new(false, Error: "Najpierw potwierdź adres e-mail - kliknij link z wiadomości wysłanej po rejestracji.",
                    EmailNotConfirmed: true),
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

    /// <summary>Ponowna wysyłka linku potwierdzającego adres (odpowiedź zawsze neutralna - nie zdradza, czy konto istnieje).</summary>
    public async Task<bool> ResendConfirmationAsync(string email) =>
        (await http.PostAsJsonAsync("api/auth/resendConfirmationEmail", new { email })).IsSuccessStatusCode;

    /// <summary>„Nie pamiętam hasła” - link wysyłany tylko na potwierdzony adres istniejącego konta; odpowiedź zawsze ta sama.</summary>
    public async Task<bool> ForgotPasswordAsync(string email) =>
        (await http.PostAsJsonAsync("api/auth/forgotPassword", new { email })).IsSuccessStatusCode;

    /// <summary>Nowe hasło z kodem z wiadomości; zwraca listę błędów (pustą przy sukcesie).</summary>
    public async Task<IReadOnlyList<string>> ResetPasswordAsync(string email, string code, string newPassword)
    {
        var response = await http.PostAsJsonAsync("api/auth/resetPassword", new { email, resetCode = code, newPassword });
        if (response.IsSuccessStatusCode) return [];

        var problem = await ApiClient.ReadProblemAsync(response);
        if (problem.Errors.ContainsKey("InvalidToken"))
            return ["Link do zmiany hasła wygasł albo został już użyty. Poproś o nowy."];
        var errors = problem.Errors.SelectMany(e => e.Value).Distinct().ToList();
        return errors.Count > 0 ? errors : [problem.Title];
    }

    /// <summary>Potwierdzenie adresu (lub zmiany adresu) kodem z linku w wiadomości.</summary>
    public async Task<bool> ConfirmEmailAsync(string userId, string code, string? changedEmail)
    {
        var url = $"api/auth/confirmEmail?userId={Uri.EscapeDataString(userId)}&code={Uri.EscapeDataString(code)}";
        if (!string.IsNullOrEmpty(changedEmail)) url += $"&changedEmail={Uri.EscapeDataString(changedEmail)}";
        // Bez odświeżania stanu logowania: przebudowałoby stronę i ponowiło potwierdzenie (kod jest jednorazowy).
        // Po zmianie adresu dotychczasowe sesje wygasają same (zmiana znacznika bezpieczeństwa konta - D52).
        return (await http.GetAsync(url)).IsSuccessStatusCode;
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
