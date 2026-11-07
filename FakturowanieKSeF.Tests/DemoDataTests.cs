using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Api.Infrastructure.DemoData;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Ksef.Fa3;
using FakturowanieKSeF.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FakturowanieKSeF.Tests;

/// <summary>Dane demonstracyjne dla środowiska deweloperskiego (D49).</summary>
public class DemoDataTests : IClassFixture<DemoDataTests.DemoFactory>
{
    public sealed class DemoFactory : ApiFactory
    {
        public const string OwnerEmail = "wlasciciel@example.com";
        public const string OwnerPassword = "Wlasciciel1@";

        protected override IEnumerable<KeyValuePair<string, string?>> AdditionalSettings() =>
        [
            new("DemoData:Owner:Email", OwnerEmail),
            new("DemoData:Owner:Password", OwnerPassword)
        ];

        private readonly SemaphoreSlim _lock = new(1, 1);
        private bool _seeded;

        public async Task EnsureSeededAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (_seeded) return;
                using var scope = Services.CreateScope();
                Assert.True(await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync());
                _seeded = true;
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    private record LoginResponse(string AccessToken);

    private readonly DemoFactory _factory;

    public DemoDataTests(DemoFactory factory) => _factory = factory;

    private async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task KontaTestoweIKontoWlasciciela_LogujaSieIMajaKompletneFirmy()
    {
        await _factory.EnsureSeededAsync();

        var accounts = DemoDataSeeder.TestAccounts.Select(a => (a.Email, a.Password))
            .Append((DemoFactory.OwnerEmail, DemoFactory.OwnerPassword)).ToList();
        foreach (var (email, password) in accounts)
        {
            var client = await LoginAsync(email, password);
            var company = await client.GetFromJsonAsync<CompanyDto>("/api/company");
            Assert.NotNull(company);
            Assert.False(string.IsNullOrEmpty(company!.Name));
            Assert.Equal(10, company.Nip.Length);
            Assert.NotNull(company.BankAccountNumber);
            Assert.True(company.HasKsefToken); // atrapa KSeF wygenerowała token testowy
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var owner = await db.Companies.SingleAsync(c => c.Email == DemoFactory.OwnerEmail);
        Assert.True(owner.NotifyReceivedInvoices);
        Assert.Equal(DemoFactory.OwnerEmail, owner.NotificationEmail);
    }

    [Fact]
    public async Task PonowneUruchomienie_NiczegoNieDubluje()
    {
        await _factory.EnsureSeededAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoices = await db.Invoices.CountAsync();

        Assert.False(await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync());

        Assert.Equal(invoices, await db.Invoices.CountAsync());
        Assert.Equal(5, await db.Companies.CountAsync());
    }

    [Fact]
    public async Task Faktury_ZgodneZFa3_ZRoznymiStanamiIFakturamiOtrzymanymiUNabywcow()
    {
        await _factory.EnsureSeededAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var validator = scope.ServiceProvider.GetRequiredService<Fa3SchemaValidator>();

        var invoices = await db.Invoices.Include(i => i.KsefSubmissions).ToListAsync();
        foreach (var s in invoices.SelectMany(i => i.KsefSubmissions))
            Assert.True(validator.Validate(System.Text.Encoding.UTF8.GetBytes(s.SentXml)).IsValid);

        Assert.Contains(invoices, i => i.Status == InvoiceStatus.Draft && i.Kind == InvoiceKind.Vat);
        Assert.Contains(invoices, i => i.Status == InvoiceStatus.Rejected);
        Assert.Contains(invoices, i => i.Kind == InvoiceKind.Correction && i.Status == InvoiceStatus.Accepted);
        Assert.Contains(invoices, i => i.Kind == InvoiceKind.Proforma);
        Assert.Contains(invoices, i => i.ProformaId != null);
        Assert.Contains(invoices, i => i.Currency == "EUR" && i.ExchangeRate != null);
        Assert.True(await db.RecurringInvoices.AnyAsync());
        Assert.True(await db.Payments.AnyAsync(p => p.FromInvoice));

        // Każda faktura „przyjęta” dla firmy z danych demonstracyjnych jest u nabywcy fakturą otrzymaną (ten sam XML)
        var nips = await db.Companies.Select(c => c.Nip).ToListAsync();
        var accepted = invoices.Where(i => i.Status == InvoiceStatus.Accepted && nips.Contains(i.Buyer.Nip!)).ToList();
        Assert.NotEmpty(accepted);
        foreach (var invoice in accepted)
        {
            var received = await db.ReceivedInvoices.SingleAsync(r => r.KsefNumber == invoice.KsefNumber);
            Assert.Equal(invoice.KsefSubmissions[0].SentXml, received.Xml);
            Assert.Equal(invoice.TotalGross, received.TotalGross);
        }

        // Faktury „przyjęte” na niby nie są podejmowane przez usługę wysyłki do KSeF
        Assert.Equal(0, await _factory.ProcessKsefAsync());
    }

    [Fact]
    public async Task Izolacja_KazdeKontoWidziTylkoSwojeFaktury()
    {
        await _factory.EnsureSeededAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var account in DemoDataSeeder.TestAccounts)
        {
            var client = await LoginAsync(account.Email, account.Password);
            var companyId = await db.Companies.Where(c => c.Email == account.Email).Select(c => c.Id).SingleAsync();

            var issued = await client.GetFromJsonAsync<PagedResult<System.Text.Json.JsonElement>>("/api/invoices?pageSize=100");
            Assert.Equal(await db.Invoices.CountAsync(i => i.CompanyId == companyId), issued!.TotalCount);

            var received = await client.GetFromJsonAsync<PagedResult<System.Text.Json.JsonElement>>("/api/received-invoices?pageSize=100");
            Assert.Equal(await db.ReceivedInvoices.CountAsync(r => r.CompanyId == companyId), received!.TotalCount);
        }
    }

    [Fact]
    public async Task BiuroRachunkowe_JestKsiegowaWFirmieTestUser1_BezPrawaWystawiania()
    {
        await _factory.EnsureSeededAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var studio = await db.Companies.SingleAsync(c => c.Email == DemoDataSeeder.TestAccounts[0].Email);

        var accountant = await LoginAsync(DemoDataSeeder.TestAccounts[3].Email, DemoDataSeeder.TestAccounts[3].Password);
        accountant.DefaultRequestHeaders.Add(CurrentCompany.HeaderName, studio.Id.ToString());

        var company = await accountant.GetFromJsonAsync<CompanyDto>("/api/company");
        Assert.Equal(studio.Nip, company!.Nip);
        var create = await accountant.PostAsJsonAsync("/api/invoices", TestUsers.SampleInvoice());
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task SzkicDoKontaWlasciciela_DajeSieWyslacDoKsef()
    {
        await _factory.EnsureSeededAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = DemoDataSeeder.TestAccounts[2];
        var owner = await db.Companies.SingleAsync(c => c.Email == DemoFactory.OwnerEmail);
        var draft = await db.Invoices.FirstAsync(i => i.Company.Email == account.Email && i.Status == InvoiceStatus.Draft
                                                      && i.Kind == InvoiceKind.Vat && i.Buyer.Nip == owner.Nip);

        var client = await LoginAsync(account.Email, account.Password);
        var send = await client.PostAsJsonAsync($"/api/invoices/{draft.Id}/send", new SendInvoiceRequest { Version = draft.Version });
        Assert.Equal(HttpStatusCode.Accepted, send.StatusCode);
        for (var i = 0; i < 5 && await _factory.ProcessKsefAsync() > 0; i++) { }

        var sent = await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{draft.Id}");
        Assert.Equal(InvoiceStatus.Accepted, sent!.Status);
    }
}
