using FakturowanieKSeF.Models;
using Microsoft.AspNetCore.Identity;

namespace FakturowanieKSeF.Api.Infrastructure;

/// <summary>
/// Blokada konta przy operacjach wymagających hasła lub kodu 2FA na zalogowanym koncie (2FA, zmiana adresu e-mail):
/// każda pomyłka liczy się do tej samej blokady co logowanie (5 prób → 15 minut) - przejęta sesja nie pozwala
/// zgadywać hasła ani kodu (D52).
/// </summary>
public static class AccountLockout
{
    /// <summary>Konto zablokowane po zbyt wielu błędnych próbach (logowania lub kodu) - 429 z czasem odblokowania.</summary>
    public static async Task<IResult?> LockedOutAsync(UserManager<ApplicationUser> users, ApplicationUser user)
    {
        if (!await users.IsLockedOutAsync(user)) return null;
        var end = await users.GetLockoutEndDateAsync(user);
        var seconds = end is { } e ? Math.Max(1, (int)Math.Ceiling((e - DateTimeOffset.UtcNow).TotalSeconds)) : 900;
        return new LockedOutResult(seconds);
    }

    /// <summary>Błędne hasło lub kod: zwiększa licznik blokady konta (ten sam co przy logowaniu) i zwraca błąd pola.</summary>
    public static async Task<IResult> FailedAttemptAsync(UserManager<ApplicationUser> users, ApplicationUser user, string field, string message)
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

}
