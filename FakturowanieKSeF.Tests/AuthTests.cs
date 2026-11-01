using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FakturowanieKSeF.Tests.Infrastructure;

namespace FakturowanieKSeF.Tests;

/// <summary>
/// Testy rejestracji i logowania (ASP.NET Core Identity, endpointy pod /api/auth).
/// </summary>
public class AuthTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public AuthTests(ApiFactory factory) => _client = factory.CreateClient();

    private static string UnikalnyEmail() => $"user_{Guid.NewGuid():N}@example.com";

    private record LoginResponse(string TokenType, string AccessToken, long ExpiresIn, string RefreshToken);

    [Fact]
    public async Task Rejestracja_IZalogowanie_ZwracaTokenDostepu()
    {
        var dane = new { email = UnikalnyEmail(), password = "Haslo123!" };

        var rejestracja = await _client.PostAsJsonAsync("/api/auth/register", dane);
        Assert.Equal(HttpStatusCode.OK, rejestracja.StatusCode);

        var logowanie = await _client.PostAsJsonAsync("/api/auth/login", dane);
        Assert.Equal(HttpStatusCode.OK, logowanie.StatusCode);

        var token = await logowanie.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal("Bearer", token!.TokenType);
        Assert.False(string.IsNullOrEmpty(token.AccessToken));

        // Token pozwala pobrać dane zalogowanego użytkownika
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/manage/info");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var info = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, info.StatusCode);
        Assert.Contains(dane.email, await info.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Logowanie_ZlymHaslem_Zwraca401()
    {
        var email = UnikalnyEmail();
        await _client.PostAsJsonAsync("/api/auth/register", new { email, password = "Haslo123!" });

        var logowanie = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "ZleHaslo1!" });

        Assert.Equal(HttpStatusCode.Unauthorized, logowanie.StatusCode);
    }

    [Fact]
    public async Task Rejestracja_ZbytSlabymHaslem_Zwraca400()
    {
        var rejestracja = await _client.PostAsJsonAsync("/api/auth/register",
            new { email = UnikalnyEmail(), password = "abc" });

        Assert.Equal(HttpStatusCode.BadRequest, rejestracja.StatusCode);
    }

    [Fact]
    public async Task EndpointChroniony_BezTokenu_Zwraca401()
    {
        var response = await _client.GetAsync("/api/auth/manage/info");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

/// <summary>Logowanie przeglądarki ciasteczkiem sesji (aplikacja Blazor) i wylogowanie.</summary>
public class CookieAuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task LogowanieCiasteczkiem_UstawiaBezpieczneCiasteczko_AWylogowanieKonczySesje()
    {
        var client = factory.CreateClient(); // klient testowy przechowuje ciasteczka jak przeglądarka
        var dane = new { email = $"cookie_{Guid.NewGuid():N}@example.com", password = "Haslo123!" };
        await client.PostAsJsonAsync("/api/auth/register", dane);

        var logowanie = await client.PostAsJsonAsync("/api/auth/login?useCookies=true", dane);
        Assert.Equal(HttpStatusCode.OK, logowanie.StatusCode);

        var cookie = Assert.Single(logowanie.Headers.GetValues("Set-Cookie"), c => c.StartsWith("ksef.auth="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);       // niedostępne dla JavaScriptu
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase); // niewysyłane z obcych stron

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/manage/info")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/manage/info")).StatusCode);
    }

    [Fact]
    public async Task Rejestracja_ZwracaPolskieKomunikatyBledowHasla()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { email = $"pl_{Guid.NewGuid():N}@example.com", password = "haslohaslo" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Hasło musi zawierać cyfrę.", body);
        Assert.Contains("Hasło musi zawierać wielką literę.", body);
    }
}
