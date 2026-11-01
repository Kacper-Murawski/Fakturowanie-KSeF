using System.Net;
using FakturowanieKSeF.Tests.Infrastructure;

namespace FakturowanieKSeF.Tests;

/// <summary>
/// Sprawdza, że jeden serwer obsługuje jednocześnie API (pod /api) i aplikację kliencką Blazor.
/// </summary>
public class HostingTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public HostingTests(ApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task StronaGlowna_ZwracaAplikacjeKliencka()
    {
        var response = await _client.GetAsync("/");

        response.EnsureSuccessStatusCode();
        Assert.Contains("_framework/blazor.webassembly.js", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/login")]
    [InlineData("/faktury/123")]
    public async Task AdresStronyKlienta_ZwracaIndexHtml(string url)
    {
        var response = await _client.GetAsync(url);

        response.EnsureSuccessStatusCode();
        Assert.Contains("_framework/blazor.webassembly.js", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NieistniejacyEndpointApi_Zwraca404()
    {
        var response = await _client.GetAsync("/api/nie-istnieje");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Health_ZwracaHealthyGdyBazaDostepna()
    {
        var response = await _client.GetAsync("/api/health");

        response.EnsureSuccessStatusCode();
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
