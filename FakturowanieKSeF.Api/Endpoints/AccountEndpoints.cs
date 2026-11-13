using System.Text.Encodings.Web;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Ksef;
using FakturowanieKSeF.Models;
using Microsoft.AspNetCore.Identity;

namespace FakturowanieKSeF.Api.Endpoints;

/// <summary>
/// Logowanie dwuskładnikowe (TOTP, RFC 6238) - D41. Klucz i weryfikacja kodów: ASP.NET Core Identity.
/// W odróżnieniu od wbudowanego <c>/api/auth/manage/2fa</c> (zablokowanego w Program.cs) WYŁĄCZENIE 2FA i nowe kody
/// odzyskiwania wymagają hasła i aktualnego kodu - przejęta sesja nie wystarczy, żeby zdjąć zabezpieczenie.
/// Każdy błędny kod lub hasło liczy się do tej samej blokady konta co logowanie (5 prób → 15 minut), a operacje z kodem
/// mają dodatkowo limit żądań na użytkownika - kodu nie da się odgadnąć metodą prób (D52, SEC-1).
/// Logowanie z kodem: wbudowany <c>/api/auth/login</c> (pola <c>twoFactorCode</c> / <c>twoFactorRecoveryCode</c>).
/// </summary>
public static class AccountEndpoints
{
    public const string Issuer = "Fakturowanie KSeF";
    public const int RecoveryCodes = 10;

    public static void MapAccountEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/account/2fa").WithTags("Konto").RequireAuthorization();

        group.MapGet("/", async (HttpContext http, UserManager<ApplicationUser> users) =>
            await users.GetUserAsync(http.User) is not { } user
                ? Results.Unauthorized()
                : Results.Ok(new TwoFactorStatusDto(await users.GetTwoFactorEnabledAsync(user), await users.CountRecoveryCodesAsync(user))))
            .WithSummary("Stan logowania dwuskładnikowego");

        group.MapPost("/setup", async (HttpContext http, UserManager<ApplicationUser> users) =>
            {
                if (await users.GetUserAsync(http.User) is not { } user) return Results.Unauthorized();
                if (await users.GetTwoFactorEnabledAsync(user))
                    return ApiErrors.Conflict("Logowanie dwuskładnikowe jest już włączone.");

                // Nowy klucz przy każdej konfiguracji - poprzednio pokazany (niepotwierdzony) klucz przestaje działać
                await users.ResetAuthenticatorKeyAsync(user);
                var key = (await users.GetAuthenticatorKeyAsync(user))!;
                var email = await users.GetEmailAsync(user) ?? "";
                var uri = $"otpauth://totp/{UrlEncoder.Default.Encode(Issuer)}:{UrlEncoder.Default.Encode(email)}"
                          + $"?secret={key}&issuer={UrlEncoder.Default.Encode(Issuer)}&digits=6";
                return Results.Ok(new TwoFactorSetupDto(FormatKey(key), uri, Convert.ToBase64String(KsefQr.GeneratePng(uri, 5))));
            })
            .WithSummary("Nowy klucz aplikacji uwierzytelniającej (kod QR i klucz do wpisania)");

        group.MapPost("/enable", async (TwoFactorCodeRequest request, HttpContext http, UserManager<ApplicationUser> users) =>
            {
                if (await users.GetUserAsync(http.User) is not { } user) return Results.Unauthorized();
                if (await users.GetTwoFactorEnabledAsync(user))
                    return ApiErrors.Conflict("Logowanie dwuskładnikowe jest już włączone.");
                if (await LockedOutAsync(users, user) is { } locked) return locked;
                if (!await VerifyTotpAsync(users, user, request.Code))
                    return await FailedAttemptAsync(users, user, nameof(request.Code),
                        "Nieprawidłowy kod - sprawdź czas w telefonie i wpisz aktualny kod.");
                await users.ResetAccessFailedCountAsync(user);

                await users.SetTwoFactorEnabledAsync(user, true);
                var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodes);
                return Results.Ok(new RecoveryCodesDto(codes!.ToList()));
            })
            .RequireRateLimiting(RateLimits.TwoFactor)
            .WithSummary("Włączenie 2FA po potwierdzeniu kodem z aplikacji; zwraca kody odzyskiwania");

        group.MapPost("/disable", async (TwoFactorConfirmRequest request, HttpContext http, UserManager<ApplicationUser> users) =>
            {
                if (await users.GetUserAsync(http.User) is not { } user) return Results.Unauthorized();
                if (!await users.GetTwoFactorEnabledAsync(user)) return ApiErrors.Conflict("Logowanie dwuskładnikowe nie jest włączone.");
                if (await LockedOutAsync(users, user) is { } locked) return locked;
                if (!await users.CheckPasswordAsync(user, request.Password))
                    return await FailedAttemptAsync(users, user, nameof(request.Password), "Nieprawidłowe hasło.");
                if (!await VerifyTotpAsync(users, user, request.Code)
                    && !(await users.RedeemTwoFactorRecoveryCodeAsync(user, request.Code.Replace(" ", "").Trim())).Succeeded)
                    return await FailedAttemptAsync(users, user, nameof(request.Code), "Nieprawidłowy kod.");
                await users.ResetAccessFailedCountAsync(user);

                await users.SetTwoFactorEnabledAsync(user, false);
                await users.ResetAuthenticatorKeyAsync(user); // stary klucz w telefonie przestaje być ważny
                return Results.NoContent();
            })
            .RequireRateLimiting(RateLimits.TwoFactor)
            .WithSummary("Wyłączenie 2FA - wymaga hasła oraz kodu z aplikacji lub kodu odzyskiwania");

        group.MapPost("/recovery-codes", async (TwoFactorConfirmRequest request, HttpContext http, UserManager<ApplicationUser> users) =>
            {
                if (await users.GetUserAsync(http.User) is not { } user) return Results.Unauthorized();
                if (!await users.GetTwoFactorEnabledAsync(user)) return ApiErrors.Conflict("Logowanie dwuskładnikowe nie jest włączone.");
                if (await LockedOutAsync(users, user) is { } locked) return locked;
                if (!await users.CheckPasswordAsync(user, request.Password))
                    return await FailedAttemptAsync(users, user, nameof(request.Password), "Nieprawidłowe hasło.");
                if (!await VerifyTotpAsync(users, user, request.Code))
                    return await FailedAttemptAsync(users, user, nameof(request.Code), "Nieprawidłowy kod.");
                await users.ResetAccessFailedCountAsync(user);

                var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodes);
                return Results.Ok(new RecoveryCodesDto(codes!.ToList()));
            })
            .RequireRateLimiting(RateLimits.TwoFactor)
            .WithSummary("Nowe kody odzyskiwania (poprzednie przestają działać) - wymaga hasła i kodu z aplikacji");
    }

    /// <summary>Konto zablokowane po zbyt wielu błędnych próbach (logowania lub kodu) - 429 z czasem odblokowania.</summary>
    private static async Task<IResult?> LockedOutAsync(UserManager<ApplicationUser> users, ApplicationUser user)
    {
        if (!await users.IsLockedOutAsync(user)) return null;
        var end = await users.GetLockoutEndDateAsync(user);
        var seconds = end is { } e ? Math.Max(1, (int)Math.Ceiling((e - DateTimeOffset.UtcNow).TotalSeconds)) : 900;
        return new LockedOutResult(seconds);
    }

    /// <summary>Błędne hasło lub kod: zwiększa licznik blokady konta (ten sam co przy logowaniu) i zwraca błąd pola.</summary>
    private static async Task<IResult> FailedAttemptAsync(UserManager<ApplicationUser> users, ApplicationUser user, string field, string message)
    {
        await users.AccessFailedAsync(user);
        return await LockedOutAsync(users, user) ?? ApiErrors.Validation(field, message);
    }

    private sealed class LockedOutResult(int retryAfterSeconds) : IResult
    {
        public Task ExecuteAsync(HttpContext http)
        {
            http.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
            return Results.Problem(
                title: "Zbyt wiele nieudanych prób. Konto jest tymczasowo zablokowane - spróbuj ponownie za kilkanaście minut.",
                statusCode: StatusCodes.Status429TooManyRequests).ExecuteAsync(http);
        }
    }

    private static Task<bool> VerifyTotpAsync(UserManager<ApplicationUser> users, ApplicationUser user, string code) =>
        users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, Normalize(code));

    /// <summary>Kod TOTP bez spacji i myślników (aplikacje pokazują go np. jako „123 456”).</summary>
    private static string Normalize(string code) => code.Replace(" ", "").Replace("-", "").Trim();

    /// <summary>Klucz w grupach po 4 znaki - łatwiej przepisać do aplikacji.</summary>
    private static string FormatKey(string key) =>
        string.Join(" ", Enumerable.Range(0, (key.Length + 3) / 4).Select(i => key.Substring(i * 4, Math.Min(4, key.Length - i * 4)))).ToLowerInvariant();
}
