using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Tests.Infrastructure;

namespace FakturowanieKSeF.Tests;

/// <summary>Logowanie dwuskładnikowe (TOTP): włączenie, logowanie z kodem i kodem odzyskiwania, wyłączenie.</summary>
public class TwoFactorTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record LoginResponse(string AccessToken);

    private static object Credentials(string email, string? code = null, string? recovery = null) =>
        new { email, password = "Haslo123!", twoFactorCode = code, twoFactorRecoveryCode = recovery };

    private static TwoFactorConfirmRequest Confirm(string code, string password = "Haslo123!") => new() { Password = password, Code = code };

    /// <summary>Zalogowany użytkownik (token Bearer) i jego e-mail.</summary>
    private async Task<(HttpClient Client, string Email)> UserAsync()
    {
        var client = factory.CreateClient();
        var email = $"user_{Guid.NewGuid():N}@example.com";
        (await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Haslo123!" })).EnsureSuccessStatusCode();
        var token = (await (await client.PostAsJsonAsync("/api/auth/login", Credentials(email))).Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return (client, email);
    }

    /// <summary>Włącza 2FA i zwraca klucz oraz kody odzyskiwania.</summary>
    private static async Task<(string Key, IReadOnlyList<string> Recovery)> EnableAsync(HttpClient client)
    {
        var setup = (await (await client.PostAsJsonAsync("/api/account/2fa/setup", new { })).Content.ReadFromJsonAsync<TwoFactorSetupDto>())!;
        var response = await client.PostAsJsonAsync("/api/account/2fa/enable", new TwoFactorCodeRequest { Code = Totp.Code(setup.SharedKey) });
        response.EnsureSuccessStatusCode();
        return (setup.SharedKey, (await response.Content.ReadFromJsonAsync<RecoveryCodesDto>())!.RecoveryCodes);
    }

    [Fact]
    public async Task Konfiguracja_KodQrIUriZgodneZeStandardem_IZlyKodNieWlacza()
    {
        var (client, email) = await UserAsync();
        Assert.False((await client.GetFromJsonAsync<TwoFactorStatusDto>("/api/account/2fa"))!.Enabled);

        var setup = (await (await client.PostAsJsonAsync("/api/account/2fa/setup", new { })).Content.ReadFromJsonAsync<TwoFactorSetupDto>())!;
        Assert.StartsWith("otpauth://totp/Fakturowanie%20KSeF:", setup.AuthenticatorUri);
        Assert.Contains($"secret={setup.SharedKey.Replace(" ", "").ToUpperInvariant()}", setup.AuthenticatorUri);
        Assert.Contains(email, setup.AuthenticatorUri);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], Convert.FromBase64String(setup.QrPngBase64)[..4]); // PNG

        var wrong = await client.PostAsJsonAsync("/api/account/2fa/enable", new TwoFactorCodeRequest { Code = "000000" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.False((await client.GetFromJsonAsync<TwoFactorStatusDto>("/api/account/2fa"))!.Enabled);
    }

    [Fact]
    public async Task PoWlaczeniu_LogowanieWymagaKodu_KodOdzyskiwaniaDzialaRaz()
    {
        var (client, email) = await UserAsync();
        var (key, recovery) = await EnableAsync(client);
        Assert.Equal(10, recovery.Count);

        var anonymous = factory.CreateClient();

        // Samo hasło już nie wystarcza
        var passwordOnly = await anonymous.PostAsJsonAsync("/api/auth/login", Credentials(email));
        Assert.Equal(HttpStatusCode.Unauthorized, passwordOnly.StatusCode);
        Assert.Contains("RequiresTwoFactor", await passwordOnly.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/login", Credentials(email, code: "123456"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/auth/login", Credentials(email, code: Totp.Code(key)))).StatusCode);

        // Kod odzyskiwania - jednorazowy
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/auth/login", Credentials(email, recovery: recovery[0]))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/login", Credentials(email, recovery: recovery[0]))).StatusCode);
        Assert.Equal(9, (await client.GetFromJsonAsync<TwoFactorStatusDto>("/api/account/2fa"))!.RecoveryCodesLeft);
    }

    [Fact]
    public async Task Wylaczenie_INoweKodyWymagajaKodu_AWbudowanyEndpointJestZablokowany()
    {
        var (client, email) = await UserAsync();
        var (key, recovery) = await EnableAsync(client);

        // Przejęta sesja bez kodu nie zdejmie zabezpieczenia - także przez wbudowany endpoint Identity
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/auth/manage/2fa", new { enable = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/account/2fa/disable", Confirm("000000"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/account/2fa/recovery-codes", Confirm(recovery[1]))).StatusCode);

        var fresh = await client.PostAsJsonAsync("/api/account/2fa/recovery-codes", Confirm(Totp.Code(key)));
        var newCodes = (await fresh.Content.ReadFromJsonAsync<RecoveryCodesDto>())!.RecoveryCodes;
        Assert.DoesNotContain(recovery[1], newCodes);

        // Wyłączenie kodem odzyskiwania (np. po utracie telefonu)
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/account/2fa/disable", Confirm(newCodes[0]))).StatusCode);
        Assert.False((await client.GetFromJsonAsync<TwoFactorStatusDto>("/api/account/2fa"))!.Enabled);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().PostAsJsonAsync("/api/auth/login", Credentials(email))).StatusCode);
    }

    [Fact]
    public async Task WylaczenieINoweKody_WymagajaHasla_SamKodNieWystarcza()
    {
        var (client, _) = await UserAsync();
        var (key, _) = await EnableAsync(client);

        var wrongPassword = await client.PostAsJsonAsync("/api/account/2fa/disable", Confirm(Totp.Code(key), "ZleHaslo1!"));
        Assert.Equal(HttpStatusCode.BadRequest, wrongPassword.StatusCode);
        Assert.Contains("Nieprawidłowe hasło", await wrongPassword.Content.ReadAsStringAsync());

        var noPassword = await client.PostAsJsonAsync("/api/account/2fa/recovery-codes", new { code = Totp.Code(key) });
        Assert.Equal(HttpStatusCode.BadRequest, noPassword.StatusCode);
        Assert.True((await client.GetFromJsonAsync<TwoFactorStatusDto>("/api/account/2fa"))!.Enabled);
    }

    [Fact]
    public async Task Wylaczenie_PoPieciuZlychKodach_KontoZablokowane_PoprawnyKodTezOdrzucony()
    {
        var (client, email) = await UserAsync();
        var (key, _) = await EnableAsync(client);

        // Przejęta sesja próbuje zgadnąć kod: każda błędna próba liczy się do blokady konta (jak logowanie) - SEC-1
        for (var i = 0; i < 4; i++)
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync("/api/account/2fa/disable", Confirm($"00000{i}"))).StatusCode);
        var fifth = await client.PostAsJsonAsync("/api/account/2fa/disable", Confirm("000009"));
        Assert.Equal(HttpStatusCode.TooManyRequests, fifth.StatusCode);
        Assert.True(fifth.Headers.Contains("Retry-After"));

        var correct = await client.PostAsJsonAsync("/api/account/2fa/disable", Confirm(Totp.Code(key)));
        Assert.Equal(HttpStatusCode.TooManyRequests, correct.StatusCode);
        Assert.True((await client.GetFromJsonAsync<TwoFactorStatusDto>("/api/account/2fa"))!.Enabled);

        // Blokada obejmuje też logowanie - to ta sama blokada konta
        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", Credentials(email, code: Totp.Code(key)));
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Contains("LockedOut", await login.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PoprawnyKod_ZerujeLicznikBlednychProb()
    {
        var (client, _) = await UserAsync();
        var (key, _) = await EnableAsync(client);

        for (var i = 0; i < 4; i++)
            await client.PostAsJsonAsync("/api/account/2fa/recovery-codes", Confirm($"00000{i}"));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/account/2fa/recovery-codes", Confirm(Totp.Code(key)))).StatusCode);

        // Po udanej operacji licznik wyzerowany - kolejna pomyłka nie blokuje konta
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/account/2fa/disable", Confirm("000000"))).StatusCode);
    }

    [Fact]
    public async Task ZmianaZabezpieczen_UniewaznaTokenOdswiezania()
    {
        var client = factory.CreateClient();
        var email = $"user_{Guid.NewGuid():N}@example.com";
        (await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Haslo123!" })).EnsureSuccessStatusCode();
        var tokens = (await (await client.PostAsJsonAsync("/api/auth/login", Credentials(email))).Content.ReadFromJsonAsync<Tokens>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        // Token odświeżania działa, dopóki konto się nie zmieni
        var refreshed = (await (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken }))
            .Content.ReadFromJsonAsync<Tokens>())!;

        // Włączenie 2FA zmienia znacznik bezpieczeństwa konta - wcześniej wydane tokeny odświeżania przestają działać (D52)
        await EnableAsync(client);
        var stale = await factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refreshed.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
    }

    private sealed record Tokens(string AccessToken, string RefreshToken);

    [Fact]
    public async Task BezLogowania_401()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/account/2fa")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/account/2fa/setup", new { })).StatusCode);
    }
}
