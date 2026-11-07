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

    [Fact]
    public async Task Pwa_ManifestIkonyIServiceWorker_SaSerwowane()
    {
        var manifest = await _client.GetAsync("/manifest.webmanifest");
        manifest.EnsureSuccessStatusCode();
        Assert.Equal("application/manifest+json", manifest.Content.Headers.ContentType!.MediaType);

        using var json = System.Text.Json.JsonDocument.Parse(await manifest.Content.ReadAsStringAsync());
        Assert.Equal("standalone", json.RootElement.GetProperty("display").GetString());
        var icons = json.RootElement.GetProperty("icons").EnumerateArray().ToList();
        Assert.Contains(icons, i => i.GetProperty("sizes").GetString() == "512x512" && i.TryGetProperty("purpose", out var p) && p.GetString() == "maskable");
        foreach (var icon in icons)
        {
            var png = await _client.GetByteArrayAsync("/" + icon.GetProperty("src").GetString());
            Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[..4]);
        }

        (await _client.GetAsync("/service-worker.js")).EnsureSuccessStatusCode();
        var index = await _client.GetStringAsync("/");
        Assert.Contains("rel=\"manifest\"", index);
        Assert.Contains("serviceWorker.register", index);
    }

    /// <summary>Produkcyjny service worker nie może przechowywać odpowiedzi API (dane użytkownika) - D45.</summary>
    [Fact]
    public void ProdukcyjnyServiceWorker_NiePrzechowujeOdpowiedziApi()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FakturowanieKSeF.Client", "wwwroot", "service-worker.published.js");
        var script = File.ReadAllText(path);
        Assert.Contains("url.pathname.startsWith('/api/')", script);
        Assert.Contains("if (!isApi)", script);
    }
}

