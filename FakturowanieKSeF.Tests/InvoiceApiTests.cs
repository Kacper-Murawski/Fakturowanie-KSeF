using System.Net;
using System.Net.Http.Json;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FakturowanieKSeF.Tests;

public class InvoiceApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<(HttpClient client, CustomerDto customer)> ClientWithCustomerAsync()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        var response = await client.PostAsJsonAsync("/api/customers", TestUsers.SampleCustomer());
        return (client, (await response.Content.ReadFromJsonAsync<CustomerDto>())!);
    }

    private static async Task<InvoiceDto> CreateAsync(HttpClient client, SaveInvoiceRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/invoices", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;
    }

    [Fact]
    public async Task UtworzenieFaktury_NadajeNumer_KopiujeStrony_IPrzeliczaKwoty()
    {
        var (client, customer) = await ClientWithCustomerAsync();

        var invoice = await CreateAsync(client, TestUsers.SampleInvoice(customer.Id));

        Assert.Equal("FV/1/11/2026", invoice.Number);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);

        // Sprzedawca z profilu firmy, nabywca z książki adresowej, rachunek domyślny firmy
        Assert.Equal("5260250274", invoice.Seller.Nip);
        Assert.Equal("Kontrahent S.A.", invoice.Buyer.Name);
        Assert.Equal("1234563218", invoice.Buyer.Nip);
        Assert.Equal("61109010140000071219812874", invoice.BankAccountNumber);

        // 10 × 150 = 1500 netto (23%) + 50 netto (5%)
        Assert.Equal(2, invoice.Items.Count);
        Assert.Equal(1500m, invoice.Items[0].NetAmount);
        Assert.Equal(1550m, invoice.TotalNet);
        Assert.Equal(345m + 2.5m, invoice.TotalVat);
        Assert.Equal(1897.5m, invoice.TotalGross);
        Assert.Equal(2, invoice.VatSummary.Count);

        var second = await CreateAsync(client, TestUsers.SampleInvoice(customer.Id));
        Assert.Equal("FV/2/11/2026", second.Number);
    }

    [Fact]
    public async Task Faktura_ZNabywcaPodanymRecznie_IWlasnymNumerem()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        var request = TestUsers.SampleInvoice();
        request.Number = "WLASNY/2026/001";

        var invoice = await CreateAsync(client, request);

        Assert.Equal("WLASNY/2026/001", invoice.Number);
        Assert.Equal("Jan Kowalski", invoice.Buyer.Name);
        Assert.Null(invoice.Buyer.Nip);
        Assert.Null(invoice.CustomerId);

        var duplicate = await client.PostAsJsonAsync("/api/invoices", request);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Faktura_BezPozycji_Zwraca400()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        var request = TestUsers.SampleInvoice();
        request.Items.Clear();

        var response = await client.PostAsJsonAsync("/api/invoices", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("co najmniej jedną pozycję", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Faktura_PrzelewBezTerminuPlatnosci_Zwraca400()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        var request = TestUsers.SampleInvoice();
        request.DueDate = null;

        var response = await client.PostAsJsonAsync("/api/invoices", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("podaj termin płatności", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Faktura_ZBlednaPozycja_Zwraca400_ZWalidacjaElementowListy()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        var request = TestUsers.SampleInvoice();
        request.Items[1].Quantity = 0;

        var response = await client.PostAsJsonAsync("/api/invoices", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Ilość musi być większa od zera", body);
        Assert.Contains("Items[1]", body); // wskazanie konkretnej pozycji
    }

    [Fact]
    public async Task Edycja_ZAktualnaWersja_Przechodzi_ZNieaktualna_Zwraca409()
    {
        var (client, customer) = await ClientWithCustomerAsync();
        var invoice = await CreateAsync(client, TestUsers.SampleInvoice(customer.Id));

        var edit = TestUsers.SampleInvoice(customer.Id);
        edit.Version = invoice.Version;
        edit.Items.RemoveAt(1);
        var response = await client.PutAsJsonAsync($"/api/invoices/{invoice.Id}", edit);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;
        Assert.Single(updated.Items);
        Assert.Equal(1845m, updated.TotalGross);
        Assert.Equal(invoice.Number, updated.Number);

        // Ponowny zapis ze starą wersją (np. druga karta przeglądarki) - konflikt
        var stale = await client.PutAsJsonAsync($"/api/invoices/{invoice.Id}", edit);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task FakturaPrzyjetaPrzezKsef_NieMozeBycEdytowanaAniUsunieta()
    {
        var (client, customer) = await ClientWithCustomerAsync();
        var invoice = await CreateAsync(client, TestUsers.SampleInvoice(customer.Id));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Invoices.Where(i => i.Id == invoice.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InvoiceStatus.Accepted));
        }

        var current = await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{invoice.Id}");
        var edit = TestUsers.SampleInvoice(customer.Id);
        edit.Version = current!.Version;

        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/invoices/{invoice.Id}", edit)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/invoices/{invoice.Id}")).StatusCode);
    }

    [Fact]
    public async Task UsuniecieSzkicu_UkrywaFaktureZListy()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        var invoice = await CreateAsync(client, TestUsers.SampleInvoice());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/invoices/{invoice.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/invoices/{invoice.Id}")).StatusCode);
        var list = await client.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices");
        Assert.Equal(0, list!.TotalCount);
    }

    [Fact]
    public async Task Lista_FiltrujeWyszukujeIStronicuje()
    {
        var (client, customer) = await ClientWithCustomerAsync();
        for (var i = 0; i < 3; i++) await CreateAsync(client, TestUsers.SampleInvoice(customer.Id));
        await CreateAsync(client, TestUsers.SampleInvoice()); // nabywca: Jan Kowalski

        var page = await client.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices?page=1&pageSize=2");
        Assert.Equal(4, page!.TotalCount);
        Assert.Equal(2, page.Items.Count);

        var search = await client.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices?q=kowalski");
        Assert.Equal(1, search!.TotalCount);

        var drafts = await client.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices?status=Draft");
        Assert.Equal(4, drafts!.TotalCount);
        var accepted = await client.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices?status=Accepted");
        Assert.Equal(0, accepted!.TotalCount);
    }
}

/// <summary>
/// Izolacja danych między użytkownikami (multi-tenancy): użytkownik B nie może odczytać, zmienić ani usunąć
/// danych użytkownika A, ani użyć ich przy tworzeniu własnych dokumentów - nawet znając identyfikatory.
/// </summary>
public class DataIsolationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task UzytkownikB_NieMaDostepuDoDanychUzytkownikaA()
    {
        // Użytkownik A: firma, kontrahent, faktura
        var userA = await factory.CreateClientWithCompanyAsync();
        var customerA = (await (await userA.PostAsJsonAsync("/api/customers", TestUsers.SampleCustomer()))
            .Content.ReadFromJsonAsync<CustomerDto>())!;
        var invoiceA = (await (await userA.PostAsJsonAsync("/api/invoices", TestUsers.SampleInvoice(customerA.Id)))
            .Content.ReadFromJsonAsync<InvoiceDto>())!;

        // Użytkownik B: własna firma (ten sam NIP firmy jest dopuszczalny - to inne konto)
        var userB = await factory.CreateClientWithCompanyAsync();

        // Odczyt cudzych danych - 404 (nie ujawniamy nawet istnienia rekordu)
        Assert.Equal(HttpStatusCode.NotFound, (await userB.GetAsync($"/api/customers/{customerA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await userB.GetAsync($"/api/invoices/{invoiceA.Id}")).StatusCode);

        // Listy B nie zawierają danych A
        Assert.Empty((await userB.GetFromJsonAsync<List<CustomerDto>>("/api/customers"))!);
        Assert.Equal(0, (await userB.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices"))!.TotalCount);

        // Modyfikacja i usunięcie cudzych danych - 404
        Assert.Equal(HttpStatusCode.NotFound,
            (await userB.PutAsJsonAsync($"/api/customers/{customerA.Id}", TestUsers.SampleCustomer(name: "Przejęty"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await userB.DeleteAsync($"/api/customers/{customerA.Id}")).StatusCode);

        var edit = TestUsers.SampleInvoice();
        edit.Version = invoiceA.Version;
        Assert.Equal(HttpStatusCode.NotFound, (await userB.PutAsJsonAsync($"/api/invoices/{invoiceA.Id}", edit)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await userB.DeleteAsync($"/api/invoices/{invoiceA.Id}")).StatusCode);

        // XML FA(3) cudzej faktury - 404
        Assert.Equal(HttpStatusCode.NotFound, (await userB.GetAsync($"/api/invoices/{invoiceA.Id}/xml")).StatusCode);

        // Użycie cudzego kontrahenta na własnej fakturze - odrzucone
        var stolen = await userB.PostAsJsonAsync("/api/invoices", TestUsers.SampleInvoice(customerA.Id));
        Assert.Equal(HttpStatusCode.BadRequest, stolen.StatusCode);

        // Dane A pozostały nienaruszone
        var stillThere = await userA.GetFromJsonAsync<CustomerDto>($"/api/customers/{customerA.Id}");
        Assert.Equal("Kontrahent S.A.", stillThere!.Name);
        Assert.Equal(HttpStatusCode.OK, (await userA.GetAsync($"/api/invoices/{invoiceA.Id}")).StatusCode);
    }

    [Fact]
    public async Task UzytkownikB_NieMaDostepuDoKataloguUzytkownikaA()
    {
        var userA = await factory.CreateClientWithCompanyAsync();
        var productA = (await (await userA.PostAsJsonAsync("/api/products",
                new SaveProductRequest { Name = "Produkt A", UnitNetPrice = 10m, VatRate = VatRate.Rate23 }))
            .Content.ReadFromJsonAsync<ProductDto>())!;
        var userB = await factory.CreateClientWithCompanyAsync();

        Assert.Empty((await userB.GetFromJsonAsync<List<ProductDto>>("/api/products"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await userB.GetAsync($"/api/products/{productA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await userB.PutAsJsonAsync($"/api/products/{productA.Id}",
            new SaveProductRequest { Name = "Przejęty", UnitNetPrice = 1m })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await userB.DeleteAsync($"/api/products/{productA.Id}")).StatusCode);

        // Ta sama nazwa w katalogu innej firmy jest dozwolona
        Assert.Equal(HttpStatusCode.Created, (await userB.PostAsJsonAsync("/api/products",
            new SaveProductRequest { Name = "Produkt A", UnitNetPrice = 20m })).StatusCode);
        Assert.Equal(10m, (await userA.GetFromJsonAsync<ProductDto>($"/api/products/{productA.Id}"))!.UnitNetPrice);
    }
}
