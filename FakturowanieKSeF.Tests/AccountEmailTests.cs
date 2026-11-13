using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FakturowanieKSeF.Api.Services.Email;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FakturowanieKSeF.Tests;

/// <summary>
/// Wiadomości systemowe konta (D53): potwierdzenie adresu przy rejestracji, reset hasła, zmiana adresu e-mail.
/// Przepływy sprawdzane z prawdziwymi linkami z wiadomości (atrapa serwera poczty).
/// </summary>
public class AccountEmailTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record LoginResponse(string AccessToken);

    private static string NewEmail(string prefix = "konto") => $"{prefix}_{Guid.NewGuid():N}@example.com";

    private static object Credentials(string email, string password = "Haslo123!") => new { email, password };

    /// <summary>Parametry z fragmentu linku (po #) z treści wiadomości.</summary>
    private static Dictionary<string, string> LinkFragment(string body, string page)
    {
        var start = body.IndexOf($"/{page}#", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Brak linku /{page}# w treści:\n{body}");
        var link = body[start..].Split('\n')[0].Trim();
        return link[(link.IndexOf('#') + 1)..].Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
    }

    private async Task<OutgoingEmail> SingleMailAsync(string to, string subjectPart)
    {
        await factory.ProcessEmailsAsync();
        return Assert.Single(factory.Email.To(to), m => m.Subject.Contains(subjectPart));
    }

    private async Task<HttpResponseMessage> ConfirmAsync(Dictionary<string, string> f)
    {
        var url = $"/api/auth/confirmEmail?userId={Uri.EscapeDataString(f["userId"])}&code={Uri.EscapeDataString(f["code"])}";
        if (f.TryGetValue("changedEmail", out var changed)) url += $"&changedEmail={Uri.EscapeDataString(changed)}";
        return await factory.CreateClient().GetAsync(url);
    }

    private async Task<HttpClient> LoggedInAsync(string email, string password = "Haslo123!")
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", Credentials(email, password));
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        return client;
    }

    [Fact]
    public async Task Rejestracja_BezPotwierdzeniaNieMoznaSieZalogowac_LinkZWiadomosciAktywujeKonto()
    {
        var email = NewEmail();
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/register", Credentials(email))).StatusCode);

        var before = await client.PostAsJsonAsync("/api/auth/login", Credentials(email));
        Assert.Equal(HttpStatusCode.Unauthorized, before.StatusCode);
        Assert.Contains("NotAllowed", await before.Content.ReadAsStringAsync());

        var mail = await SingleMailAsync(email, "Potwierdź adres e-mail");
        Assert.Contains("ważny 24 godziny", mail.Body);
        var fragment = LinkFragment(mail.Body, "potwierdz-email");
        Assert.DoesNotContain("?code=", mail.Body); // kod we fragmencie, nie w zapytaniu (logi serwera)

        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(fragment)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", Credentials(email))).StatusCode);
    }

    [Fact]
    public async Task PotwierdzenieZFalszywymKodem_Odrzucone()
    {
        var email = NewEmail();
        (await factory.CreateClient().PostAsJsonAsync("/api/auth/register", Credentials(email))).EnsureSuccessStatusCode();
        var fragment = LinkFragment((await SingleMailAsync(email, "Potwierdź adres e-mail")).Body, "potwierdz-email");
        fragment["code"] = "Zm9yZ2Vk";

        Assert.Equal(HttpStatusCode.Unauthorized, (await ConfirmAsync(fragment)).StatusCode);
    }

    [Fact]
    public async Task ResetHasla_LinkZWiadomosci_UstawiaNoweHaslo_JestJednorazowy_I2FAZostaje()
    {
        var email = NewEmail();
        var client = factory.CreateClient();
        await factory.RegisterConfirmedAsync(client, email);
        var session = await LoggedInAsync(email);
        var setup = (await (await session.PostAsJsonAsync("/api/account/2fa/setup", new { })).Content.ReadFromJsonAsync<TwoFactorSetupDto>())!;
        (await session.PostAsJsonAsync("/api/account/2fa/enable", new TwoFactorCodeRequest { Code = Totp.Code(setup.SharedKey) })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/forgotPassword", new { email })).StatusCode);
        var mail = await SingleMailAsync(email, "Ustaw nowe hasło");
        Assert.Contains("ważny 1 godzinę", mail.Body);
        var fragment = LinkFragment(mail.Body, "reset-hasla");
        Assert.Equal(email, fragment["email"]);

        var reset = new { email, resetCode = fragment["code"], newPassword = "NoweHaslo1!" };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/resetPassword", reset)).StatusCode);
        // Jednorazowy - zmiana hasła zmienia znacznik bezpieczeństwa konta
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/resetPassword", new { email, resetCode = fragment["code"], newPassword = "Inne123!!" })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", Credentials(email))).StatusCode);
        // Nowe hasło działa, ale logowanie nadal wymaga kodu 2FA
        var withNew = await client.PostAsJsonAsync("/api/auth/login", Credentials(email, "NoweHaslo1!"));
        Assert.Equal(HttpStatusCode.Unauthorized, withNew.StatusCode);
        Assert.Contains("RequiresTwoFactor", await withNew.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ResetHasla_NieistniejaceKonto_TaSamaOdpowiedz_BezWiadomosci()
    {
        var email = NewEmail("nikt");
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().PostAsJsonAsync("/api/auth/forgotPassword", new { email })).StatusCode);
        await factory.ProcessEmailsAsync();
        Assert.Empty(factory.Email.To(email));
    }

    [Fact]
    public void WaznoscLinkow_Potwierdzenie24h_Reset1h()
    {
        Assert.Equal(TimeSpan.FromHours(24), factory.Services.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan);
        Assert.Equal(TimeSpan.FromHours(1), factory.Services.GetRequiredService<IOptions<PasswordResetTokenProviderOptions>>().Value.TokenLifespan);
        Assert.Equal(PasswordResetTokenProvider.Name,
            factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value.Tokens.PasswordResetTokenProvider);
    }

    [Fact]
    public async Task WbudowanaZmianaEmaila_BezHasla_JestZablokowana()
    {
        var email = NewEmail();
        await factory.RegisterConfirmedAsync(factory.CreateClient(), email);
        var session = await LoggedInAsync(email);

        var response = await session.PostAsJsonAsync("/api/auth/manage/info", new { newEmail = NewEmail("atakujacy") });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await factory.ProcessEmailsAsync();
        Assert.Empty(factory.Email.Sent.Where(m => m.Subject.Contains("nowy adres")).Where(m => m.To.StartsWith("atakujacy")));
    }

    [Fact]
    public async Task ZmianaEmaila_WymagaHasla_LinkNaNowyAdres_PowiadomienieNaStary()
    {
        var oldEmail = NewEmail("stary");
        var newEmail = NewEmail("nowy");
        await factory.RegisterConfirmedAsync(factory.CreateClient(), oldEmail);
        var session = await LoggedInAsync(oldEmail);

        var wrong = await session.PostAsJsonAsync("/api/account/email", new ChangeEmailRequest { NewEmail = newEmail, Password = "ZleHaslo1!" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("Nieprawidłowe hasło", await wrong.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Accepted,
            (await session.PostAsJsonAsync("/api/account/email", new ChangeEmailRequest { NewEmail = newEmail, Password = "Haslo123!" })).StatusCode);

        var notice = await SingleMailAsync(oldEmail, "Prośba o zmianę adresu");
        Assert.Contains(newEmail, notice.Body);
        var fragment = LinkFragment((await SingleMailAsync(newEmail, "Potwierdź nowy adres")).Body, "potwierdz-email");
        Assert.Equal(newEmail, fragment["changedEmail"]);

        // Przed kliknięciem linku obowiązuje stary adres
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsJsonAsync("/api/auth/login", Credentials(newEmail))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(fragment)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().PostAsJsonAsync("/api/auth/login", Credentials(newEmail))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsJsonAsync("/api/auth/login", Credentials(oldEmail))).StatusCode);
    }

    [Fact]
    public async Task ZmianaEmaila_NaZajetyAdres_TaSamaOdpowiedz_BezWiadomosci()
    {
        var email = NewEmail();
        var taken = NewEmail("zajety");
        await factory.RegisterConfirmedAsync(factory.CreateClient(), email);
        await factory.RegisterConfirmedAsync(factory.CreateClient(), taken);
        var session = await LoggedInAsync(email);

        Assert.Equal(HttpStatusCode.Accepted,
            (await session.PostAsJsonAsync("/api/account/email", new ChangeEmailRequest { NewEmail = taken, Password = "Haslo123!" })).StatusCode);
        await factory.ProcessEmailsAsync();
        Assert.Empty(factory.Email.To(taken).Where(m => m.Subject.Contains("Potwierdź nowy adres")));
        Assert.Empty(factory.Email.To(email).Where(m => m.Subject.Contains("Prośba o zmianę adresu")));
    }

    [Fact]
    public async Task ZmianaEmaila_PrzyWlaczonym2FA_WymagaKodu()
    {
        var email = NewEmail();
        await factory.RegisterConfirmedAsync(factory.CreateClient(), email);
        var session = await LoggedInAsync(email);
        var setup = (await (await session.PostAsJsonAsync("/api/account/2fa/setup", new { })).Content.ReadFromJsonAsync<TwoFactorSetupDto>())!;
        (await session.PostAsJsonAsync("/api/account/2fa/enable", new TwoFactorCodeRequest { Code = Totp.Code(setup.SharedKey) })).EnsureSuccessStatusCode();

        var noCode = await session.PostAsJsonAsync("/api/account/email", new ChangeEmailRequest { NewEmail = NewEmail("nowy"), Password = "Haslo123!" });
        Assert.Equal(HttpStatusCode.BadRequest, noCode.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await session.PostAsJsonAsync("/api/account/email",
            new ChangeEmailRequest { NewEmail = NewEmail("nowy"), Password = "Haslo123!", Code = Totp.Code(setup.SharedKey) })).StatusCode);
    }

    [Fact]
    public async Task WiadomosciSystemowe_LimitNaAdresNaGodzine()
    {
        var email = NewEmail();
        await factory.RegisterConfirmedAsync(factory.CreateClient(), email);
        var client = factory.CreateClient();
        for (var i = 0; i < 7; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/forgotPassword", new { email })).StatusCode);

        await factory.ProcessEmailsAsync();
        // Rejestracja (1) + resety do limitu 5 na godzinę; kolejne pomijane bez zmiany odpowiedzi
        Assert.Equal(5, factory.Email.To(email).Count);
    }
}

/// <summary>Niskie limity poczty - atomowy licznik firmy, limit całej aplikacji, osobna pula wiadomości systemowych (SEC-4).</summary>
public class EmailQuotaFactory : ApiFactory
{
    protected override IEnumerable<KeyValuePair<string, string?>> AdditionalSettings() =>
    [
        new("Email:DailyLimitPerCompany", "3"),
        new("Email:GlobalDailyLimit", "5")
    ];
}

public class EmailQuotaTests(EmailQuotaFactory factory) : IClassFixture<EmailQuotaFactory>
{
    private static SendInvoiceEmailRequest To() => new() { To = $"klient_{Guid.NewGuid():N}@example.com" };

    [Fact]
    public async Task Limity_RownolegleZadaniaNiePrzekraczajaLimituFirmy_LimitAplikacji_ISystemoweOsobno()
    {
        var clientA = await factory.CreateClientWithKsefAsync();
        var invoiceA = await factory.AcceptedInvoiceAsync(clientA);

        // 10 równoczesnych żądań przy limicie firmy 3 - dokładnie 3 przyjęte (wcześniej „sprawdź, potem zapisz”)
        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => clientA.PostAsJsonAsync($"/api/invoices/{invoiceA.Id}/email", To())));
        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Accepted));
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));

        // Limit całej aplikacji (5): druga firma wyśle już tylko 2 wiadomości
        var clientB = await factory.CreateClientWithKsefAsync();
        var invoiceB = await factory.AcceptedInvoiceAsync(clientB);
        var statusesB = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
            statusesB.Add((await clientB.PostAsJsonAsync($"/api/invoices/{invoiceB.Id}/email", To())).StatusCode);
        Assert.Equal([HttpStatusCode.Accepted, HttpStatusCode.Accepted, HttpStatusCode.TooManyRequests], statusesB);

        // Wiadomości systemowe mają własną pulę - reset hasła działa mimo wyczerpanych limitów firm i aplikacji
        var email = $"reset_{Guid.NewGuid():N}@example.com";
        await factory.RegisterConfirmedAsync(factory.CreateClient(), email);
        (await factory.CreateClient().PostAsJsonAsync("/api/auth/forgotPassword", new { email })).EnsureSuccessStatusCode();
        await factory.ProcessEmailsAsync();
        Assert.Contains(factory.Email.To(email), m => m.Subject.Contains("Ustaw nowe hasło"));
    }
}
