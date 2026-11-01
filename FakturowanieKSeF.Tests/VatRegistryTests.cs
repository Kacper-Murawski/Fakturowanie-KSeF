using System.Net;
using System.Net.Http.Json;
using System.Text;
using FakturowanieKSeF.Api.Integrations.VatWhiteList;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Tests.Infrastructure;

namespace FakturowanieKSeF.Tests;

/// <summary>Rozbiór adresu z wykazu podatników VAT na pola formularza.</summary>
public class WhiteListAddressTests
{
    [Theory]
    [InlineData("UL. KWIATOWA 1/2, 00-001 WARSZAWA", "ul. Kwiatowa 1/2", "00-001", "Warszawa")]
    [InlineData("ŚWIĘTOKRZYSKA 12, 00-916 WARSZAWA", "Świętokrzyska 12", "00-916", "Warszawa")]
    [InlineData("AL. JANA PAWŁA II 15/3, 61-139 POZNAŃ", "al. Jana Pawła II 15/3", "61-139", "Poznań")]
    [InlineData("ZAWADA 23, 39-200 DĘBICA", "Zawada 23", "39-200", "Dębica")]
    [InlineData("STRZELCÓW BYTOMSKICH 4, 43-300 BIELSKO-BIAŁA", "Strzelców Bytomskich 4", "43-300", "Bielsko-Biała")]
    [InlineData("UL. ŻÓŁKIEWSKIEGO, BUDYNEK B 7, 31-539 KRAKÓW", "ul. Żółkiewskiego, Budynek B 7", "31-539", "Kraków")]
    public void Adres_RozbityNaPola_ZPolskimiWielkosciamiLiter(string text, string street, string postal, string city)
    {
        var address = WhiteListAddress.Parse(text);

        Assert.NotNull(address);
        Assert.Equal(street, address.Street);
        Assert.Equal(postal, address.PostalCode);
        Assert.Equal(city, address.City);
        Assert.Equal("PL", address.CountryCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("WARSZAWA")]
    [InlineData("UL. KWIATOWA 1, WARSZAWA")]
    [InlineData(", 00-001 WARSZAWA")]
    public void AdresNierozpoznany_ZwracaNull(string? text) => Assert.Null(WhiteListAddress.Parse(text));
}

/// <summary>Klient HTTP API wykazu - odczyt odpowiedzi w formacie MF (bez połączenia z siecią).</summary>
public class VatWhiteListClientTests
{
    private sealed class StubHandler(HttpStatusCode status, string json) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private static (VatWhiteListClient Client, StubHandler Handler) Create(HttpStatusCode status, string json)
    {
        var handler = new StubHandler(status, json);
        return (new VatWhiteListClient(new HttpClient(handler) { BaseAddress = new Uri(VatWhiteListClient.BaseAddress) }), handler);
    }

    [Fact]
    public async Task OdpowiedzMF_ZPodmiotem_JestOdczytana()
    {
        // Struktura odpowiedzi zgodna ze specyfikacją API wykazu (EntityResponse)
        const string json = """
            {"result":{"subject":{"name":"MINISTERSTWO FINANSÓW","nip":"5260250274","statusVat":"Czynny","regon":"000002217",
             "pesel":null,"krs":null,"residenceAddress":null,"workingAddress":"ŚWIĘTOKRZYSKA 12, 00-916 WARSZAWA",
             "representatives":[],"authorizedClerks":[],"partners":[],"registrationLegalDate":"2004-01-01",
             "accountNumbers":["37101010100165482231000000"],"hasVirtualAccounts":false},
             "requestId":"d2n10-84df1a1","requestDateTime":"01-11-2026 10:00:00"}}
            """;
        var (client, handler) = Create(HttpStatusCode.OK, json);

        var subject = await client.FindByNipAsync("5260250274", new DateOnly(2026, 11, 1));

        Assert.Equal("https://wl-api.mf.gov.pl/api/search/nip/5260250274?date=2026-11-01", handler.RequestUri!.ToString());
        Assert.NotNull(subject);
        Assert.Equal("MINISTERSTWO FINANSÓW", subject.Name);
        Assert.Equal("Czynny", subject.StatusVat);
        Assert.Equal("ŚWIĘTOKRZYSKA 12, 00-916 WARSZAWA", subject.WorkingAddress);
        Assert.Equal(["37101010100165482231000000"], subject.AccountNumbers);
        Assert.Equal("d2n10-84df1a1", subject.RequestId);
    }

    [Fact]
    public async Task OdpowiedzMF_BezPodmiotu_ZwracaNull()
    {
        var (client, _) = Create(HttpStatusCode.OK, """{"result":{"subject":null,"requestId":"x1","requestDateTime":"01-11-2026 10:00:00"}}""");
        Assert.Null(await client.FindByNipAsync("1234563218", new DateOnly(2026, 11, 1)));
    }

    [Fact]
    public async Task BladMF_ZamienianyNaCzytelnyWyjatek()
    {
        var (client, _) = Create(HttpStatusCode.BadRequest, """{"code":"WL-113","message":"Pole 'NIP' ma nieprawidłową długość."}""");

        var e = await Assert.ThrowsAsync<VatWhiteListException>(() => client.FindByNipAsync("123", new DateOnly(2026, 11, 1)));
        Assert.Contains("Pole 'NIP' ma nieprawidłową długość.", e.Message);
    }

    [Theory]
    [InlineData("TAK", true)]
    [InlineData("NIE", false)]
    public async Task SprawdzenieRachunku_OdpowiedzMF(string answer, bool assigned)
    {
        var (client, handler) = Create(HttpStatusCode.OK,
            $$$"""{"result":{"accountAssigned":"{{{answer}}}","requestDateTime":"01-11-2026 10:00:00","requestId":"chk-1"}}""");

        var check = await client.CheckBankAccountAsync("1234563218", "61109010140000071219812874", new DateOnly(2026, 11, 1));

        Assert.Equal("https://wl-api.mf.gov.pl/api/check/nip/1234563218/bank-account/61109010140000071219812874?date=2026-11-01",
            handler.RequestUri!.ToString());
        Assert.Equal(assigned, check.Assigned);
        Assert.Equal("chk-1", check.RequestId);
    }

    [Fact]
    public async Task NieoczekiwanaOdpowiedz_ZamienianaNaWyjatek()
    {
        var (client, _) = Create(HttpStatusCode.InternalServerError, "<html>Service Unavailable</html>");
        await Assert.ThrowsAsync<VatWhiteListException>(() => client.FindByNipAsync("1234563218", new DateOnly(2026, 11, 1)));
    }
}

/// <summary>Endpoint wyszukiwania w wykazie: wynik, błędy, pamięć podręczna, uwierzytelnianie.</summary>
public class VatRegistryApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task PodmiotZWykazu_ZwroconyZAdresemRozbitymNaPola()
    {
        var client = await factory.CreateLoggedInClientAsync();

        var entry = await client.GetFromJsonAsync<VatRegistryEntryDto>("/api/vat-registry/123-456-32-18");

        Assert.NotNull(entry);
        Assert.Equal("PRZYKŁADOWA SPÓŁKA Z OGRANICZONĄ ODPOWIEDZIALNOŚCIĄ", entry.Name);
        Assert.Equal("1234563218", entry.Nip);
        Assert.True(entry.IsActiveVatPayer);
        Assert.Equal("ul. Jana Pawła II 15/3", entry.Address!.Street);
        Assert.Equal("61-139", entry.Address.PostalCode);
        Assert.Equal("Poznań", entry.Address.City);
        Assert.Equal(2, entry.BankAccounts.Count);
        Assert.Equal("Ab12c-3de4f5g", entry.RequestId);
    }

    [Fact]
    public async Task PonowneZapytanie_ZPamieciPodrecznej_BezZapytaniaDoMF()
    {
        var nip = TestUsers.RandomNip();
        factory.VatWhiteList.Add(new WhiteListSubject("FIRMA Z PAMIĘCI", nip, "Zwolniony", null, null, null,
            "KWIATOWA 1, 00-001 WARSZAWA", [], "r1"));
        var client = await factory.CreateLoggedInClientAsync();

        var before = factory.VatWhiteList.Searches;
        var first = await client.GetFromJsonAsync<VatRegistryEntryDto>($"/api/vat-registry/{nip}");
        var second = await client.GetFromJsonAsync<VatRegistryEntryDto>($"/api/vat-registry/{nip}");

        Assert.Equal(1, factory.VatWhiteList.Searches - before);
        Assert.Equal(first!.RequestId, second!.RequestId);
        // Osoba fizyczna bez miejsca prowadzenia działalności - adres zamieszkania; status inny niż czynny
        Assert.Equal("Kwiatowa 1", first!.Address!.Street);
        Assert.False(first.IsActiveVatPayer);
        Assert.Equal("Zwolniony", first.VatStatus);
    }

    [Fact]
    public async Task BrakWWykazie_404_ZPodpowiedzia()
    {
        var client = await factory.CreateLoggedInClientAsync();

        var response = await client.GetAsync($"/api/vat-registry/{TestUsers.RandomNip()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("nie ma w wykazie podatników VAT", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NieprawidlowyNip_400_BezZapytaniaDoMF()
    {
        var client = await factory.CreateLoggedInClientAsync();
        var before = factory.VatWhiteList.Searches;

        var response = await client.GetAsync("/api/vat-registry/1234567890");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, factory.VatWhiteList.Searches);
    }

    [Fact]
    public async Task BezLogowania_401()
    {
        var response = await factory.CreateClient().GetAsync("/api/vat-registry/1234563218");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

/// <summary>Awaria API MF i dzienne limity zapytań (osobna aplikacja z niskimi limitami).</summary>
public class VatRegistryLimitTests(VatRegistryLimitTests.LowLimitFactory factory) : IClassFixture<VatRegistryLimitTests.LowLimitFactory>
{
    public sealed class LowLimitFactory : ApiFactory
    {
        protected override IEnumerable<KeyValuePair<string, string?>> AdditionalSettings() =>
        [
            new("VatWhiteList:DailyLimit", "5"),
            new("VatWhiteList:DailyLimitPerUser", "3")
        ];
    }

    [Fact]
    public async Task Limity_NaUzytkownika_IDlaCalejAplikacji_OrazAwariaMF()
    {
        // Awaria MF: 502 z komunikatem (zapytanie wlicza się do limitu - dotarło do MF)
        var userA = await factory.CreateLoggedInClientAsync();
        factory.VatWhiteList.Fail = true;
        var failed = await userA.GetAsync($"/api/vat-registry/{TestUsers.RandomNip()}");
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Contains("nie odpowiada", await failed.Content.ReadAsStringAsync());
        factory.VatWhiteList.Fail = false;

        // Użytkownik A: limit 3 zapytań dziennie (awaria była pierwszym)
        Assert.Equal(HttpStatusCode.OK, (await userA.GetAsync("/api/vat-registry/1234563218")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await userA.GetAsync($"/api/vat-registry/{TestUsers.RandomNip()}")).StatusCode);
        var limited = await userA.GetAsync($"/api/vat-registry/{TestUsers.RandomNip()}");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Contains("dzienny limit", await limited.Content.ReadAsStringAsync());

        // Wynik z pamięci podręcznej nie zużywa limitu - nadal dostępny
        Assert.Equal(HttpStatusCode.OK, (await userA.GetAsync("/api/vat-registry/1234563218")).StatusCode);

        // Użytkownik B: własny limit, ale wspólny limit aplikacji (5) wyczerpuje się po 2 zapytaniach
        var userB = await factory.CreateLoggedInClientAsync();
        for (var i = 0; i < 2; i++)
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await userB.GetAsync($"/api/vat-registry/{TestUsers.RandomNip()}")).StatusCode);
        var appLimited = await userB.GetAsync($"/api/vat-registry/{TestUsers.RandomNip()}");
        Assert.Equal(HttpStatusCode.TooManyRequests, appLimited.StatusCode);
        Assert.Contains("limit zapytań aplikacji", await appLimited.Content.ReadAsStringAsync());
    }
}
