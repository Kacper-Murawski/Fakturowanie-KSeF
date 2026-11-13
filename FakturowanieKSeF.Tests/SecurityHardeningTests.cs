using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Tests.Infrastructure;

namespace FakturowanieKSeF.Tests;

/// <summary>Nagłówki bezpieczeństwa każdej odpowiedzi (D47).</summary>
public class SecurityHeadersTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/faktury/5")]
    [InlineData("/api/health")]
    [InlineData("/manifest.webmanifest")]
    public async Task OdpowiedzMaNaglowkiBezpieczenstwa(string url)
    {
        var response = await factory.CreateClient().GetAsync(url);

        Assert.Equal(SecurityHeaders.ContentSecurityPolicy, response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("camera=()", response.Headers.GetValues("Permissions-Policy").Single());
    }

    [Fact]
    public void Csp_TylkoSkryptyZSerwera_BezRamekIObcychZrodel()
    {
        Assert.Contains("script-src 'self' 'wasm-unsafe-eval';", SecurityHeaders.ContentSecurityPolicy);
        Assert.DoesNotContain("'unsafe-eval'", SecurityHeaders.ContentSecurityPolicy.Replace("'wasm-unsafe-eval'", ""));
        Assert.Contains("frame-ancestors 'none'", SecurityHeaders.ContentSecurityPolicy);
        Assert.Contains("object-src 'none'", SecurityHeaders.ContentSecurityPolicy);
    }

    [Fact]
    public void Hsts_RokTylkoDlaDomenyAplikacji()
    {
        var options = factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.HttpsPolicy.HstsOptions>>().Value;
        Assert.Equal(TimeSpan.FromDays(365), options.MaxAge);
        Assert.False(options.IncludeSubDomains);
        Assert.False(options.Preload);
    }

    [Fact]
    public async Task StronaStartowa_BezSkryptowWstawionychWStrone()
    {
        var html = await factory.CreateClient().GetStringAsync("/");
        Assert.DoesNotMatch(@"<script>(?!</script>)", html); // każdy <script> ma atrybut src
    }
}

/// <summary>Aplikacja z niskimi limitami żądań - sprawdzenie ochrony przed zgadywaniem haseł i nadużyciem API (D47).</summary>
public class RateLimitFactory : ApiFactory
{
    protected override IEnumerable<KeyValuePair<string, string?>> AdditionalSettings() =>
    [
        new("RateLimiting:Enabled", "true"),
        new("RateLimiting:AuthPerMinute", "3"),
        new("RateLimiting:ApiPerMinute", "15")
    ];
}

/// <summary>Osobna instancja aplikacji (osobne liczniki limitów) dla testu limitu API.</summary>
public class ApiRateLimitFactory : RateLimitFactory;

/// <summary>Osobna instancja (osobne liczniki): sprawdzanie sesji nie może zużywać limitu logowania (SEC-3).</summary>
public class SessionRateLimitFactory : RateLimitFactory;

/// <summary>Limit logowań na parę „adres IP + e-mail” niższy niż limit na sam adres (D52).</summary>
public class LoginPairRateLimitFactory : ApiFactory
{
    protected override IEnumerable<KeyValuePair<string, string?>> AdditionalSettings() =>
    [
        new("RateLimiting:Enabled", "true"),
        new("RateLimiting:AuthPerMinute", "100"),
        new("RateLimiting:LoginPerMinute", "2")
    ];
}

/// <summary>Limit żądań z kodem 2FA na użytkownika (SEC-1).</summary>
public class TwoFactorRateLimitFactory : ApiFactory
{
    protected override IEnumerable<KeyValuePair<string, string?>> AdditionalSettings() =>
    [
        new("RateLimiting:Enabled", "true"),
        new("RateLimiting:TwoFactorPerMinute", "2")
    ];
}

public class SessionRateLimitTests(SessionRateLimitFactory factory) : IClassFixture<SessionRateLimitFactory>
{
    [Fact]
    public async Task SprawdzenieSesjiIWylogowanie_NieZuzywajaLimituLogowania()
    {
        var client = factory.CreateClient();

        // Odświeżanie strony przez kilka osób z jednego adresu (biuro za NAT) - wcześniej blokowało logowanie.
        // 12 żądań: ponad limit logowania (3), poniżej limitu całego API (15)
        for (var i = 0; i < 6; i++)
        {
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/auth/manage/info")).StatusCode);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        }

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "ktos@example.com", password = "Zle123!!" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }
}

public class LoginPairRateLimitTests(LoginPairRateLimitFactory factory) : IClassFixture<LoginPairRateLimitFactory>
{
    [Fact]
    public async Task Logowanie_LimitNaAdresIEmail_InneKontoZTegoAdresuMozeSieLogowac()
    {
        var client = factory.CreateClient();
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.PostAsJsonAsync("/api/auth/login", new { email = "ofiara@example.com", password = "Zle123!!" })).StatusCode);

        var blocked = await client.PostAsJsonAsync("/api/auth/login", new { email = "OFIARA@example.com ", password = "Zle123!!" });
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.Contains("Retry-After"));

        // Inne konto z tego samego adresu - osobna pula
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login", new { email = "ktos-inny@example.com", password = "Zle123!!" })).StatusCode);
    }
}

public class TwoFactorRateLimitTests(TwoFactorRateLimitFactory factory) : IClassFixture<TwoFactorRateLimitFactory>
{
    private sealed record LoginResponse(string AccessToken);

    [Fact]
    public async Task OperacjeZKodem2FA_LimitNaUzytkownika()
    {
        var client = factory.CreateClient();
        var email = $"limit2fa_{Guid.NewGuid():N}@example.com";
        await factory.RegisterConfirmedAsync(client, email);
        var token = (await (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Haslo123!" }))
            .Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        await client.PostAsJsonAsync("/api/account/2fa/setup", new { });

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
            statuses.Add((await client.PostAsJsonAsync("/api/account/2fa/enable", new { code = "000000" })).StatusCode);

        Assert.Equal([HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.TooManyRequests], statuses);
    }
}

public class RateLimitTests(RateLimitFactory factory) : IClassFixture<RateLimitFactory>
{
    [Fact]
    public async Task Logowanie_PoPrzekroczeniuLimituZAdresu_429ZRetryAfter()
    {
        var client = factory.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            var attempt = await client.PostAsJsonAsync("/api/auth/login", new { email = $"x{i}@example.com", password = "Zle123!!" });
            Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
        }

        var blocked = await client.PostAsJsonAsync("/api/auth/login", new { email = "inne@example.com", password = "Zle123!!" });

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.Contains("Retry-After"));
        Assert.Contains("Zbyt wiele żądań", await blocked.Content.ReadAsStringAsync());
    }
}

public class ApiRateLimitTests(ApiRateLimitFactory factory) : IClassFixture<ApiRateLimitFactory>
{
    private sealed record LoginResponse(string AccessToken);

    [Fact]
    public async Task Api_LimitNaUzytkownika()
    {
        var client = factory.CreateClient();
        var email = $"limit_{Guid.NewGuid():N}@example.com";
        await factory.RegisterConfirmedAsync(client, email);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Haslo123!" });
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 16; i++) statuses.Add((await client.GetAsync("/api/customers")).StatusCode);

        // Limit 15 żądań na minutę na użytkownika - 16. żądanie odrzucone
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
        Assert.All(statuses.Take(15), s => Assert.NotEqual(HttpStatusCode.TooManyRequests, s));
    }
}
