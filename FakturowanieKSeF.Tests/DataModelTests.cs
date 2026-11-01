using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Models;
using FakturowanieKSeF.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FakturowanieKSeF.Tests;

/// <summary>
/// Testy modelu danych na prawdziwym PostgreSQL: ograniczenia bazy, miękkie usuwanie, znaczniki czasu,
/// niezmienność danych na fakturze i kontrola współbieżności.
/// </summary>
public class DataModelTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public DataModelTests(ApiFactory factory) => _factory = factory;

    private AppDbContext NewContext() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();

    private static Address PrzykladowyAdres() => new()
    {
        Street = "ul. Testowa 1", PostalCode = "00-001", City = "Warszawa", CountryCode = "PL"
    };

    /// <summary>Tworzy użytkownika z firmą i zwraca identyfikator firmy.</summary>
    private async Task<int> UtworzFirmeAsync(string nip = "5260250274")
    {
        await using var db = NewContext();
        var user = new ApplicationUser { UserName = $"u{Guid.NewGuid():N}@example.com", Email = "x@example.com" };
        db.Users.Add(user);
        var company = new Company { Name = "Firma Testowa", Nip = nip, Address = PrzykladowyAdres(), OwnerId = user.Id };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return company.Id;
    }

    private static Invoice NowaFaktura(int companyId, string number, Customer? customer = null) => new()
    {
        CompanyId = companyId,
        Number = number,
        IssueDate = new DateOnly(2026, 11, 1),
        Seller = new InvoiceParty { Name = "Firma Testowa", Nip = "5260250274", Address = PrzykladowyAdres() },
        Buyer = new InvoiceParty
        {
            Name = customer?.Name ?? "Jan Kowalski",
            Nip = customer?.Nip,
            Address = customer?.Address is { } a
                ? new Address { Street = a.Street, PostalCode = a.PostalCode, City = a.City, CountryCode = a.CountryCode }
                : PrzykladowyAdres()
        },
        Customer = customer,
        Items =
        [
            new InvoiceItem
            {
                Position = 1, Name = "Usługa programistyczna", Unit = "godz.",
                Quantity = 1.5m, UnitNetPrice = 100.12345678m, VatRate = VatRate.Rate23,
                NetAmount = 150.19m, VatAmount = 34.54m, GrossAmount = 184.73m
            }
        ]
    };

    [Fact]
    public async Task Zapis_UstawiaCreatedAt_AModyfikacja_UpdatedAt()
    {
        var companyId = await UtworzFirmeAsync();

        await using (var db = NewContext())
        {
            var company = await db.Companies.SingleAsync(c => c.Id == companyId);
            Assert.True(company.CreatedAt > DateTime.UtcNow.AddMinutes(-1));
            Assert.Null(company.UpdatedAt);

            company.Name = "Nowa nazwa";
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var company = await db.Companies.SingleAsync(c => c.Id == companyId);
            Assert.NotNull(company.UpdatedAt);
        }
    }

    [Fact]
    public async Task UsuniecieKontrahenta_JestMiekkie_RekordZostajeWBazie()
    {
        var companyId = await UtworzFirmeAsync();
        int customerId;

        await using (var db = NewContext())
        {
            var customer = new Customer { CompanyId = companyId, Name = "Kontrahent", Nip = "1234563218", Address = PrzykladowyAdres() };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;

            db.Customers.Remove(customer);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            Assert.False(await db.Customers.AnyAsync(c => c.Id == customerId));
            var usuniety = await db.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == customerId);
            Assert.True(usuniety.IsDeleted);
        }
    }

    [Fact]
    public async Task UsuniecieSzkicuFaktury_JestMiekkie_IZachowujePozycje()
    {
        var companyId = await UtworzFirmeAsync();
        int invoiceId;

        await using (var db = NewContext())
        {
            var invoice = NowaFaktura(companyId, "FV/MIEKKIE/1");
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
            invoiceId = invoice.Id;
        }

        await using (var db = NewContext())
        {
            var invoice = await db.Invoices.Include(i => i.Items).SingleAsync(i => i.Id == invoiceId);
            db.Invoices.Remove(invoice);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            Assert.False(await db.Invoices.AnyAsync(i => i.Id == invoiceId));
            Assert.False(await db.InvoiceItems.AnyAsync(i => i.InvoiceId == invoiceId));
            Assert.Equal(1, await db.InvoiceItems.IgnoreQueryFilters().CountAsync(i => i.InvoiceId == invoiceId));
        }
    }

    [Fact]
    public async Task NumerFaktury_UnikalnyWRamachFirmy_AleNieMiedzyFirmami()
    {
        var firmaA = await UtworzFirmeAsync();
        var firmaB = await UtworzFirmeAsync();

        await using (var db = NewContext())
        {
            db.Invoices.Add(NowaFaktura(firmaA, "FV/1/11/2026"));
            db.Invoices.Add(NowaFaktura(firmaB, "FV/1/11/2026")); // inna firma - dozwolone
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            db.Invoices.Add(NowaFaktura(firmaA, "FV/1/11/2026")); // duplikat w tej samej firmie
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("IX_Invoices_CompanyId_Number", ex.InnerException?.Message);
        }
    }

    [Fact]
    public async Task NumerUsunietegoSzkicu_MoznaUzycPonownie()
    {
        var companyId = await UtworzFirmeAsync();

        await using (var db = NewContext())
        {
            var invoice = NowaFaktura(companyId, "FV/2/11/2026");
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
            db.Invoices.Remove(invoice);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            db.Invoices.Add(NowaFaktura(companyId, "FV/2/11/2026"));
            await db.SaveChangesAsync(); // indeks częściowy pomija usunięte
        }
    }

    [Fact]
    public async Task JedenUzytkownik_MozeMiecTylkoJednaFirme()
    {
        var companyId = await UtworzFirmeAsync();

        await using var db = NewContext();
        var ownerId = (await db.Companies.SingleAsync(c => c.Id == companyId)).OwnerId;
        db.Companies.Add(new Company { Name = "Druga firma", Nip = "1234563218", Address = PrzykladowyAdres(), OwnerId = ownerId });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("IX_Companies_OwnerId", ex.InnerException?.Message);
    }

    [Fact]
    public async Task FizyczneUsuniecieKontrahentaZFakturami_JestBlokowanePrzezBaze()
    {
        var companyId = await UtworzFirmeAsync();
        int customerId;

        await using (var db = NewContext())
        {
            var customer = new Customer { CompanyId = companyId, Name = "Kontrahent", Nip = "1111111111", Address = PrzykladowyAdres() };
            db.Invoices.Add(NowaFaktura(companyId, "FV/RESTRICT/1", customer));
            await db.SaveChangesAsync();
            customerId = customer.Id;
        }

        // ExecuteDelete omija miękkie usuwanie - chroni nas ograniczenie klucza obcego (ON DELETE RESTRICT)
        await using (var db = NewContext())
        {
            await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
                db.Customers.IgnoreQueryFilters().Where(c => c.Id == customerId).ExecuteDeleteAsync());
        }
    }

    [Fact]
    public async Task ZmianaDanychKontrahenta_NieZmieniaWystawionejFaktury()
    {
        var companyId = await UtworzFirmeAsync();
        int invoiceId, customerId;

        await using (var db = NewContext())
        {
            var customer = new Customer { CompanyId = companyId, Name = "Stara Nazwa sp. z o.o.", Nip = "2222222222", Address = PrzykladowyAdres() };
            var invoice = NowaFaktura(companyId, "FV/SNAPSHOT/1", customer);
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
            (invoiceId, customerId) = (invoice.Id, customer.Id);
        }

        await using (var db = NewContext())
        {
            var customer = await db.Customers.SingleAsync(c => c.Id == customerId);
            customer.Name = "Nowa Nazwa S.A.";
            customer.Address.City = "Kraków";
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var invoice = await db.Invoices.SingleAsync(i => i.Id == invoiceId);
            Assert.Equal("Stara Nazwa sp. z o.o.", invoice.Buyer.Name);
            Assert.Equal("Warszawa", invoice.Buyer.Address.City);
        }
    }

    [Fact]
    public async Task DatyIKwoty_ZachowujaTypIPrecyzjeZgodnaZFa3()
    {
        var companyId = await UtworzFirmeAsync();
        int invoiceId;

        await using (var db = NewContext())
        {
            var invoice = NowaFaktura(companyId, "FV/PRECYZJA/1");
            invoice.SaleDate = new DateOnly(2026, 10, 31);
            invoice.Items[0].Quantity = 1.123456m;
            invoice.Items[0].VatRate = VatRate.NotSubjectII;
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
            invoiceId = invoice.Id;
        }

        await using (var db = NewContext())
        {
            var invoice = await db.Invoices.Include(i => i.Items).SingleAsync(i => i.Id == invoiceId);
            Assert.Equal(new DateOnly(2026, 11, 1), invoice.IssueDate);
            Assert.Equal(new DateOnly(2026, 10, 31), invoice.SaleDate);
            Assert.Equal(1.123456m, invoice.Items[0].Quantity);
            Assert.Equal(100.12345678m, invoice.Items[0].UnitNetPrice);
            Assert.Equal(VatRate.NotSubjectII, invoice.Items[0].VatRate);
            Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        }
    }

    [Fact]
    public async Task RownoczesnaModyfikacjaFaktury_WykrywaKonflikt()
    {
        var companyId = await UtworzFirmeAsync();
        int invoiceId;

        await using (var db = NewContext())
        {
            var invoice = NowaFaktura(companyId, "FV/WSPOLBIEZNOSC/1");
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
            invoiceId = invoice.Id;
        }

        // Dwa „równoczesne żądania” wczytują tę samą wersję faktury
        await using var db1 = NewContext();
        await using var db2 = NewContext();
        var kopia1 = await db1.Invoices.SingleAsync(i => i.Id == invoiceId);
        var kopia2 = await db2.Invoices.SingleAsync(i => i.Id == invoiceId);

        kopia1.Status = InvoiceStatus.Pending;
        await db1.SaveChangesAsync();

        kopia2.Status = InvoiceStatus.Pending; // np. drugie kliknięcie „wyślij”
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());
    }
}
