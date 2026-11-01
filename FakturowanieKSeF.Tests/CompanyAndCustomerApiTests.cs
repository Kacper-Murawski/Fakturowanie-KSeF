using System.Net;
using System.Net.Http.Json;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Tests.Infrastructure;

namespace FakturowanieKSeF.Tests;

public class CompanyApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Firma_PrzedUtworzeniem404_PoUtworzeniuDostepna_ZNormalizacjaDanych()
    {
        var client = await factory.CreateLoggedInClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/company")).StatusCode);

        var request = TestUsers.SampleCompany(nip: "526-025-02-74");
        var created = await client.PutAsJsonAsync("/api/company", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var company = await client.GetFromJsonAsync<CompanyDto>("/api/company");
        Assert.Equal("5260250274", company!.Nip);                         // NIP bez myślników
        Assert.Equal("61109010140000071219812874", company.BankAccountNumber); // rachunek bez spacji
        Assert.False(company.HasKsefToken);

        request.Name = "Nowa Nazwa";
        var updated = await client.PutAsJsonAsync("/api/company", request);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Nowa Nazwa", (await updated.Content.ReadFromJsonAsync<CompanyDto>())!.Name);
    }

    [Fact]
    public async Task Firma_ZBlednymNipIKodemPocztowym_Zwraca400ZListaBledow()
    {
        var client = await factory.CreateLoggedInClientAsync();
        var request = TestUsers.SampleCompany(nip: "1234567890");
        request.Address.PostalCode = "00001";

        var response = await client.PutAsJsonAsync("/api/company", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Nieprawidłowy numer NIP", body);
        Assert.Contains("00-000", body); // walidacja zagnieżdżonego adresu
    }

    [Fact]
    public async Task EndpointyBiznesowe_BezZalogowania_Zwracaja401()
    {
        var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/company")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/invoices")).StatusCode);
    }
}

public class CustomerApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Kontrahenci_BezFirmy_Zwraca409ZInstrukcja()
    {
        var client = await factory.CreateLoggedInClientAsync();

        var response = await client.GetAsync("/api/customers");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("uzupełnij dane swojej firmy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Kontrahent_PelnyCykl_DodanieEdycjaWyszukiwanieUsuniecie()
    {
        var client = await factory.CreateClientWithCompanyAsync();

        var created = await client.PostAsJsonAsync("/api/customers", TestUsers.SampleCustomer());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var customer = (await created.Content.ReadFromJsonAsync<CustomerDto>())!;

        var update = TestUsers.SampleCustomer(name: "Zmieniona Nazwa S.A.");
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/customers/{customer.Id}", update)).StatusCode);

        var found = await client.GetFromJsonAsync<List<CustomerDto>>("/api/customers?q=zmieniona");
        Assert.Single(found!);
        Assert.Equal("Zmieniona Nazwa S.A.", found![0].Name);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/customers/{customer.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/customers/{customer.Id}")).StatusCode);
    }

    [Fact]
    public async Task KontrahentFirma_BezNip_Zwraca400_AOsobaPrywatnaBezNip_Przechodzi()
    {
        var client = await factory.CreateClientWithCompanyAsync();

        var firma = TestUsers.SampleCustomer();
        firma.Nip = null;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/customers", firma)).StatusCode);

        var osoba = TestUsers.SampleCustomer(name: "Jan Kowalski");
        osoba.IsCompany = false;
        osoba.Nip = null;
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/customers", osoba)).StatusCode);
    }

    [Fact]
    public async Task Kontrahent_ZDuplikatemNip_Zwraca409()
    {
        var client = await factory.CreateClientWithCompanyAsync();

        await client.PostAsJsonAsync("/api/customers", TestUsers.SampleCustomer());
        var duplicate = await client.PostAsJsonAsync("/api/customers", TestUsers.SampleCustomer(name: "Inna nazwa"));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("już istnieje", await duplicate.Content.ReadAsStringAsync());
    }
}
