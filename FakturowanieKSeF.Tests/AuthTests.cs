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
