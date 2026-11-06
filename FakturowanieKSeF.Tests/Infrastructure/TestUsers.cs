using System.Net.Http.Headers;
using System.Net.Http.Json;
using FakturowanieKSeF.Contracts.Dtos;

namespace FakturowanieKSeF.Tests.Infrastructure;

/// <summary>Pomocnicze tworzenie zalogowanych użytkowników i ich firm w testach integracyjnych.</summary>
public static class TestUsers
{
    private record LoginResponse(string AccessToken);

    /// <summary>Rejestruje nowego użytkownika i zwraca klienta HTTP z jego tokenem.</summary>
    public static async Task<HttpClient> CreateLoggedInClientAsync(this ApiFactory factory)
    {
        var client = factory.CreateClient();
        var credentials = new { email = $"user_{Guid.NewGuid():N}@example.com", password = "Haslo123!" };

        (await client.PostAsJsonAsync("/api/auth/register", credentials)).EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", credentials);
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static SaveCompanyRequest SampleCompany(string nip = "5260250274") => new()
    {
        Name = "Moja Firma sp. z o.o.",
        Nip = nip,
        Address = new AddressDto { Street = "ul. Kwiatowa 1/2", PostalCode = "00-001", City = "Warszawa", CountryCode = "PL" },
        Email = "biuro@mojafirma.pl",
        BankAccountNumber = "61 1090 1014 0000 0712 1981 2874"
    };

    /// <summary>Zalogowany użytkownik z utworzoną firmą.</summary>
    public static async Task<HttpClient> CreateClientWithCompanyAsync(this ApiFactory factory, string nip = "5260250274")
    {
        var client = await factory.CreateLoggedInClientAsync();
        (await client.PutAsJsonAsync("/api/company", SampleCompany(nip))).EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>Faktura przyjęta w KSeF (atrapa); klient musi mieć firmę z tokenem KSeF.</summary>
    public static async Task<InvoiceDto> AcceptedInvoiceAsync(this ApiFactory factory, HttpClient client, SaveInvoiceRequest? request = null)
    {
        var draft = (await (await client.PostAsJsonAsync("/api/invoices", request ?? SampleInvoice())).Content.ReadFromJsonAsync<InvoiceDto>())!;
        (await client.PostAsJsonAsync($"/api/invoices/{draft.Id}/send", new SendInvoiceRequest { Version = draft.Version })).EnsureSuccessStatusCode();
        for (var i = 0; i < 5 && await factory.ProcessKsefAsync() > 0; i++) { }
        var invoice = (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{draft.Id}"))!;
        if (invoice.Status != InvoiceStatus.Accepted) throw new InvalidOperationException($"Faktura nie została przyjęta: {invoice.Status}");
        return invoice;
    }

    /// <summary>Zalogowany użytkownik z firmą i tokenem KSeF (atrapa).</summary>
    public static async Task<HttpClient> CreateClientWithKsefAsync(this ApiFactory factory)
    {
        var client = await factory.CreateClientWithCompanyAsync();
        (await client.PostAsync("/api/company/ksef/test-token", null)).EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>Losowy, poprawny (z sumą kontrolną) NIP - do testów wymagających unikalnej firmy.</summary>
    public static string RandomNip()
    {
        int[] w = [6, 5, 7, 2, 3, 4, 5, 6, 7];
        while (true)
        {
            var d = Enumerable.Range(0, 9).Select(i => i < 2 ? Random.Shared.Next(1, 10) : Random.Shared.Next(0, 10)).ToArray();
            var c = d.Select((x, i) => x * w[i]).Sum() % 11;
            if (c < 10) return string.Concat(d) + c;
        }
    }

    public static SaveCustomerRequest SampleCustomer(string nip = "1234563218", string name = "Kontrahent S.A.") => new()
    {
        Name = name,
        IsCompany = true,
        Nip = nip,
        Address = new AddressDto { Street = "ul. Polna 5", PostalCode = "30-001", City = "Kraków", CountryCode = "PL" },
        Email = "faktury@kontrahent.pl"
    };

    public static SaveInvoiceRequest SampleInvoice(int? customerId = null) => new()
    {
        IssueDate = new DateOnly(2026, 11, 1),
        DueDate = new DateOnly(2026, 11, 15),
        PaymentMethod = PaymentMethod.Transfer,
        CustomerId = customerId,
        Buyer = customerId is null
            ? new SaveInvoiceBuyerRequest
            {
                Name = "Jan Kowalski",
                Address = new AddressDto { Street = "ul. Leśna 3", PostalCode = "80-001", City = "Gdańsk", CountryCode = "PL" }
            }
            : null,
        Items =
        [
            new SaveInvoiceItemRequest { Name = "Usługa programistyczna", Unit = "godz.", Quantity = 10, UnitNetPrice = 150m, VatRate = VatRate.Rate23 },
            new SaveInvoiceItemRequest { Name = "Książka", Unit = "szt.", Quantity = 1, UnitNetPrice = 50m, VatRate = VatRate.Rate5 }
        ]
    };
}
