using System.Net;
using System.Net.Http.Json;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Ksef;
using FakturowanieKSeF.Tests.Infrastructure;

namespace FakturowanieKSeF.Tests;

/// <summary>Zmiana NIP firmy a token KSeF (D51) i rozpoznanie odrzucenia tokenu przez KSeF.</summary>
public class CompanyNipChangeTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public CompanyNipChangeTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ZmianaNipBezFakturWKsef_UsuwaTokenKsef()
    {
        var client = await _factory.CreateClientWithKsefAsync();
        Assert.True((await client.GetFromJsonAsync<CompanyDto>("/api/company"))!.HasKsefToken);

        var request = TestUsers.SampleCompany(TestUsers.RandomNip());
        var response = await client.PutAsJsonAsync("/api/company", request);

        response.EnsureSuccessStatusCode();
        var company = (await response.Content.ReadFromJsonAsync<CompanyDto>())!;
        Assert.Equal(request.Nip, company.Nip);
        Assert.False(company.HasKsefToken); // token był wydany dla poprzedniego NIP
    }

    [Fact]
    public async Task ZmianaNipPoWyslaniuFakturyDoKsef_Zwraca409()
    {
        var client = await _factory.CreateClientWithKsefAsync();
        await _factory.AcceptedInvoiceAsync(client);
        var before = (await client.GetFromJsonAsync<CompanyDto>("/api/company"))!;

        var response = await client.PutAsJsonAsync("/api/company", TestUsers.SampleCompany(TestUsers.RandomNip()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var after = (await client.GetFromJsonAsync<CompanyDto>("/api/company"))!;
        Assert.Equal(before.Nip, after.Nip);
        Assert.True(after.HasKsefToken);
    }

    [Fact]
    public async Task ZapisBezZmianyNip_ZostawiaToken()
    {
        var client = await _factory.CreateClientWithKsefAsync();
        var current = (await client.GetFromJsonAsync<CompanyDto>("/api/company"))!;

        var response = await client.PutAsJsonAsync("/api/company", TestUsers.SampleCompany(current.Nip));

        response.EnsureSuccessStatusCode();
        Assert.True((await response.Content.ReadFromJsonAsync<CompanyDto>())!.HasKsefToken);
    }

    [Fact]
    public void OdrzucenieTokenuPrzezKsef_RozpoznaneZKomunikatuKlientaMf()
    {
        const string message = "Uwierzytelnianie nie zakończyło się kodem 200 w ciągu 60s. Ostatni status: 450, Opis: Uwierzytelnianie " +
                               "zakończone niepowodzeniem z powodu błędnego tokenu, Szczegóły: Token nie może być użyty w kontekście nip-5445607937.";

        var reason = KsefGateway.AuthRejection(message);

        Assert.NotNull(reason);
        Assert.Contains("kod 450", reason);
        Assert.Contains("nip-5445607937", reason);
        Assert.Null(KsefGateway.AuthRejection("Uwierzytelnianie nie zakończyło się kodem 200 w ciągu 60s. Ostatni status: 100, Opis: W toku"));
    }
}
