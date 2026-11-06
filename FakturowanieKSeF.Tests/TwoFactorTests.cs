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
            (await client.PostAsJsonAsync("/api/account/2fa/disable", new TwoFactorCodeRequest { Code = "000000" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/account/2fa/recovery-codes", new TwoFactorCodeRequest { Code = recovery[1] })).StatusCode);

        var fresh = await client.PostAsJsonAsync("/api/account/2fa/recovery-codes", new TwoFactorCodeRequest { Code = Totp.Code(key) });
        var newCodes = (await fresh.Content.ReadFromJsonAsync<RecoveryCodesDto>())!.RecoveryCodes;
        Assert.DoesNotContain(recovery[1], newCodes);

        // Wyłączenie kodem odzyskiwania (np. po utracie telefonu)
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/account/2fa/disable", new TwoFactorCodeRequest { Code = newCodes[0] })).StatusCode);
        Assert.False((await client.GetFromJsonAsync<TwoFactorStatusDto>("/api/account/2fa"))!.Enabled);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().PostAsJsonAsync("/api/auth/login", Credentials(email))).StatusCode);
    }

    [Fact]
    public async Task BezLogowania_401()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/account/2fa")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/account/2fa/setup", new { })).StatusCode);
    }
}
