using System.Net;
using FakturowanieKSeF.Api.Ksef;

namespace FakturowanieKSeF.Tests;

/// <summary>
/// Testy blokady produkcyjnego KSeF. Te testy NIE łączą się z żadnym serwerem:
/// handler docelowy jest atrapą, która tylko rejestruje, czy żądanie do niej dotarło.
/// </summary>
public class KsefEnvironmentGuardTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public void JedynymSrodowiskiemJestTestowe()
    {
        Assert.Equal([KsefEnvironment.Test], Enum.GetValues<KsefEnvironment>());
        Assert.Equal("https://api-test.ksef.mf.gov.pl/", KsefEnvironments.GetBaseUri(KsefEnvironment.Test).ToString());
    }

    [Theory]
    [InlineData("https://api.ksef.mf.gov.pl/v2/auth/challenge")]        // produkcja
    [InlineData("https://API.KSEF.MF.GOV.PL/v2/auth/challenge")]        // produkcja, inna wielkość liter
    [InlineData("https://api-demo.ksef.mf.gov.pl/v2/auth/challenge")]   // przedprodukcja (DEMO)
    [InlineData("https://ksef.mf.gov.pl/api/online/Session")]           // stare API 1.0 (produkcja)
    [InlineData("http://api-test.ksef.mf.gov.pl/v2/auth/challenge")]    // test, ale bez HTTPS
    [InlineData("https://api-test.ksef.mf.gov.pl.evil.example/v2")]     // podszywanie się pod host testowy
    [InlineData("https://example.com/")]
    public async Task ZadanieSpozaSrodowiskaTestowego_JestBlokowaneZanimWyjdzie(string url)
    {
        var inner = new RecordingHandler();
        using var client = new HttpClient(new KsefTestOnlyHandler(inner));

        await Assert.ThrowsAsync<KsefProductionBlockedException>(() => client.GetAsync(url));
        Assert.Equal(0, inner.Calls);
    }

    [Fact]
    public async Task ZadanieDoSrodowiskaTestowego_JestPrzepuszczane()
    {
        var inner = new RecordingHandler();
        using var client = new HttpClient(new KsefTestOnlyHandler(inner));

        var response = await client.GetAsync("https://api-test.ksef.mf.gov.pl/v2/auth/challenge");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }
}
