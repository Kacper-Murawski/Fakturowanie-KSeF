using System.Text.Encodings.Web;
using System.Text;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Api.Services.Email;
using Microsoft.AspNetCore.WebUtilities;
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
                if (await AccountLockout.LockedOutAsync(users, user) is { } locked) return locked;
                if (!await VerifyTotpAsync(users, user, request.Code))
                    return await AccountLockout.FailedAttemptAsync(users, user, nameof(request.Code),
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
                if (await AccountLockout.LockedOutAsync(users, user) is { } locked) return locked;
                if (!await users.CheckPasswordAsync(user, request.Password))
                    return await AccountLockout.FailedAttemptAsync(users, user, nameof(request.Password), "Nieprawidłowe hasło.");
                if (!await VerifyTotpAsync(users, user, request.Code)
                    && !(await users.RedeemTwoFactorRecoveryCodeAsync(user, request.Code.Replace(" ", "").Trim())).Succeeded)
                    return await AccountLockout.FailedAttemptAsync(users, user, nameof(request.Code), "Nieprawidłowy kod.");
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
                if (await AccountLockout.LockedOutAsync(users, user) is { } locked) return locked;
                if (!await users.CheckPasswordAsync(user, request.Password))
                    return await AccountLockout.FailedAttemptAsync(users, user, nameof(request.Password), "Nieprawidłowe hasło.");
                if (!await VerifyTotpAsync(users, user, request.Code))
                    return await AccountLockout.FailedAttemptAsync(users, user, nameof(request.Code), "Nieprawidłowy kod.");
                await users.ResetAccessFailedCountAsync(user);

                var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodes);
                return Results.Ok(new RecoveryCodesDto(codes!.ToList()));
            })
            .RequireRateLimiting(RateLimits.TwoFactor)
            .WithSummary("Nowe kody odzyskiwania (poprzednie przestają działać) - wymaga hasła i kodu z aplikacji");
    }

    /// <summary>
    /// Zmiana adresu e-mail konta (D53). Wbudowana zmiana przez <c>POST /api/auth/manage/info</c> jest zablokowana
    /// (Program.cs), bo nie wymaga hasła - przejęta sesja pozwoliłaby przejąć konto (zmiana adresu → reset hasła).
    /// Odpowiedź jest zawsze taka sama (202), także gdy adres należy do innego konta - nie da się sprawdzić, kto ma konto.
    /// </summary>
    public static void MapEmailChangeEndpoint(this RouteGroupBuilder api) =>
        api.MapPost("/account/email", async (ChangeEmailRequest request, HttpContext http, UserManager<ApplicationUser> users,
                IEmailSender<ApplicationUser> emails) =>
            {
                if (await users.GetUserAsync(http.User) is not { } user) return Results.Unauthorized();
                if (await AccountLockout.LockedOutAsync(users, user) is { } locked) return locked;
                if (!await users.CheckPasswordAsync(user, request.Password))
                    return await AccountLockout.FailedAttemptAsync(users, user, nameof(request.Password), "Nieprawidłowe hasło.");
                if (await users.GetTwoFactorEnabledAsync(user)
                    && (string.IsNullOrWhiteSpace(request.Code) || !await VerifyTotpAsync(users, user, request.Code)))
                    return await AccountLockout.FailedAttemptAsync(users, user, nameof(request.Code), "Podaj aktualny kod z aplikacji uwierzytelniającej.");
                await users.ResetAccessFailedCountAsync(user);

                var newEmail = request.NewEmail.Trim();
                if (users.NormalizeEmail(newEmail) == user.NormalizedEmail)
                    return ApiErrors.Validation(nameof(request.NewEmail), "To Twój obecny adres e-mail.");

                if (await users.FindByEmailAsync(newEmail) is null)
                {
                    var token = await users.GenerateChangeEmailTokenAsync(user, newEmail);
                    var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
                    // Ten sam format linku, który buduje Identity - AccountEmails zamienia go na stronę klienta
                    var link = $"{http.Request.Scheme}://{http.Request.Host}/api/auth/confirmEmail?userId={Uri.EscapeDataString(user.Id)}"
                               + $"&code={Uri.EscapeDataString(code)}&changedEmail={Uri.EscapeDataString(newEmail)}";
                    await emails.SendConfirmationLinkAsync(user, newEmail, link);
                    if (emails is AccountEmails account && user.Email is { } oldEmail)
                        await account.SendEmailChangeNoticeAsync(oldEmail, newEmail);
                }

                return Results.Accepted();
            })
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.TwoFactor)
            .WithTags("Konto")
            .WithSummary("Zmiana adresu e-mail: wymaga hasła (i kodu 2FA); link potwierdzający na nowy adres, powiadomienie na stary");

    private static Task<bool> VerifyTotpAsync(UserManager<ApplicationUser> users, ApplicationUser user, string code) =>
        users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, Normalize(code));

    /// <summary>Kod TOTP bez spacji i myślników (aplikacje pokazują go np. jako „123 456”).</summary>
    private static string Normalize(string code) => code.Replace(" ", "").Replace("-", "").Trim();

    /// <summary>Klucz w grupach po 4 znaki - łatwiej przepisać do aplikacji.</summary>
    private static string FormatKey(string key) =>
        string.Join(" ", Enumerable.Range(0, (key.Length + 3) / 4).Select(i => key.Substring(i * 4, Math.Min(4, key.Length - i * 4)))).ToLowerInvariant();
}
