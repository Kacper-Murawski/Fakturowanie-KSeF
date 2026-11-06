using System.Net;
using System.Net.Http.Json;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Tests.Infrastructure;

namespace FakturowanieKSeF.Tests;

/// <summary>Katalog towarów i usług: dodawanie, edycja, unikalność nazwy, usuwanie, walidacja.</summary>
public class ProductApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static SaveProductRequest Sample(string name = "Usługa programistyczna", decimal price = 150m) =>
        new() { Name = name, Unit = "godz.", UnitNetPrice = price, VatRate = VatRate.Rate23 };

    [Fact]
    public async Task Dodanie_Edycja_Wyszukiwanie_IUsuniecie()
    {
        var client = await factory.CreateClientWithCompanyAsync();

        var created = await client.PostAsJsonAsync("/api/products", Sample());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var product = (await created.Content.ReadFromJsonAsync<ProductDto>())!;
        Assert.Equal(150m, product.UnitNetPrice);
        (await client.PostAsJsonAsync("/api/products", new SaveProductRequest { Name = "Książka", UnitNetPrice = 49.99m, VatRate = VatRate.Rate5 }))
            .EnsureSuccessStatusCode();

        // Lista posortowana po nazwie, wyszukiwanie bez rozróżniania wielkości liter
        var all = (await client.GetFromJsonAsync<List<ProductDto>>("/api/products"))!;
        Assert.Equal(["Książka", "Usługa programistyczna"], all.Select(p => p.Name));
        Assert.Single((await client.GetFromJsonAsync<List<ProductDto>>("/api/products?q=PROGRAM"))!);

        var edited = await client.PutAsJsonAsync($"/api/products/{product.Id}", Sample(" Usługa programistyczna ", 180m));
        Assert.Equal(180m, (await edited.Content.ReadFromJsonAsync<ProductDto>())!.UnitNetPrice);
        Assert.Equal("Usługa programistyczna", (await client.GetFromJsonAsync<ProductDto>($"/api/products/{product.Id}"))!.Name);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/products/{product.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/products/{product.Id}")).StatusCode);

        // Po usunięciu (miękkim) można ponownie dodać pozycję o tej nazwie
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/products", Sample())).StatusCode);
    }

    [Fact]
    public async Task TaSamaNazwa_WKataloguFirmy_ToKonflikt()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        (await client.PostAsJsonAsync("/api/products", Sample("Abonament"))).EnsureSuccessStatusCode();

        var duplicate = await client.PostAsJsonAsync("/api/products", Sample("Abonament"));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("już jest w katalogu", await duplicate.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Walidacja_NazwaCenaIStawka()
    {
        var client = await factory.CreateClientWithCompanyAsync();

        var response = await client.PostAsJsonAsync("/api/products",
            new SaveProductRequest { Name = "", UnitNetPrice = -1m, VatRate = VatRate.Rate4 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Name", body);
        Assert.Contains("UnitNetPrice", body);
        Assert.Contains("VatRate", body);
    }

    [Fact]
    public async Task ZmianaKatalogu_NieZmieniaFaktury()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        var product = (await (await client.PostAsJsonAsync("/api/products", Sample("Usługa programistyczna", 150m)))
            .Content.ReadFromJsonAsync<ProductDto>())!;
        var invoice = (await (await client.PostAsJsonAsync("/api/invoices", TestUsers.SampleInvoice()))
            .Content.ReadFromJsonAsync<InvoiceDto>())!;

        await client.PutAsJsonAsync($"/api/products/{product.Id}", Sample("Usługa programistyczna", 999m));
        await client.DeleteAsync($"/api/products/{product.Id}");

        var after = (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{invoice.Id}"))!;
        Assert.Equal(150m, after.Items[0].UnitNetPrice);
    }
}
