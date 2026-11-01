using System.Net;
using System.Net.Http.Json;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Services;
using FakturowanieKSeF.Contracts.Calculation;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Ksef;
using FakturowanieKSeF.Ksef.Fa3;
using FakturowanieKSeF.Models;
using FakturowanieKSeF.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FakturowanieKSeF.Tests;

/// <summary>Czytnik XML faktur w strukturze FA (podgląd faktur otrzymanych od innych wystawców).</summary>
public class Fa3InvoiceReaderTests
{
    [Fact]
    public void OdczytujeFaktureWygenerowanaPrzezAplikacje()
    {
        var xml = ReceivedInvoicesTests.ForeignInvoiceXml("1234563218", "5260250274", "FV/77/2026", 3);

        var doc = Fa3InvoiceReader.Read(xml);

        Assert.Equal("FV/77/2026", doc.InvoiceNumber);
        Assert.Equal("VAT", doc.InvoiceType);
        Assert.Equal("Hurtownia Dostawca sp. z o.o.", doc.Seller.Name);
        Assert.Equal("1234563218", doc.Seller.Nip);
        Assert.Equal("5260250274", doc.Buyer.Nip);
        Assert.Equal("00-950 Warszawa", doc.Seller.AddressLine2);
        Assert.Equal(2, doc.Lines.Count);
        Assert.Equal("Papier A4", doc.Lines[0].Name);
        Assert.Equal(3m, doc.Lines[0].Quantity);
        Assert.Equal("23", doc.Lines[0].VatRate);
        Assert.Contains(doc.VatSummary, v => v.Label == "23% / 22%" && v.Net == 75m);
        Assert.Contains(doc.VatSummary, v => v.Label == "5%");
        Assert.Equal("Przelew", doc.PaymentMethod);
        Assert.NotNull(doc.DueDate);
    }

    [Fact]
    public void OdczytujePrzykladowaFaktureMinisterstwa()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "external", "ksef-client-csharp", "KSeF.Client.Tests.Core", "Templates", "invoice-template-fa-3.xml");
        var xml = File.ReadAllText(path).Replace("#nip#", "5260250274").Replace("#invoice_number#", "1");

        var doc = Fa3InvoiceReader.Read(xml);

        Assert.Equal("Elektrownia S.A.", doc.Seller.Name);
        Assert.Equal(2, doc.Lines.Count);
        Assert.Equal(53.63m, doc.TotalGross);
    }

    [Fact]
    public void DokumentZDtd_JestOdrzucany_OchronaPrzedXxe()
    {
        Assert.ThrowsAny<System.Xml.XmlException>(() =>
            Fa3InvoiceReader.Read("<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><Faktura>&e;</Faktura>"));
    }
}

/// <summary>Synchronizacja faktur otrzymanych z KSeF (atrapa KSeF): przyrostowość, HWM, limity, deduplikacja, błędy.</summary>
public class ReceivedInvoicesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);

    /// <summary>XML faktury wystawionej przez innego sprzedawcę na NIP nabywcy (z generatora aplikacji).</summary>
    public static string ForeignInvoiceXml(string sellerNip, string buyerNip, string number, decimal qty)
    {
        var lines = new[] { (n: "Papier A4", q: qty, p: 25m, r: VatRate.Rate23), (n: "Książka", q: 1m, p: 40m, r: VatRate.Rate5) };
        var items = lines.Select((l, i) =>
        {
            var c = InvoiceCalculator.CalculateLine(new LineInput(l.q, l.p, l.r));
            return new InvoiceItem { Position = i + 1, Name = l.n, Unit = "szt.", Quantity = l.q, UnitNetPrice = l.p, VatRate = l.r,
                NetAmount = c.NetAmount, VatAmount = c.VatAmount, GrossAmount = c.GrossAmount };
        }).ToList();
        return new Fa3XmlGenerator().Generate(new Invoice
        {
            Number = number, IssueDate = new DateOnly(2026, 10, 30), Currency = "PLN", PaymentMethod = PaymentMethod.Transfer,
            DueDate = new DateOnly(2026, 11, 13), BankAccountNumber = "61109010140000071219812874",
            Seller = new InvoiceParty { Name = "Hurtownia Dostawca sp. z o.o.", Nip = sellerNip,
                Address = new Address { Street = "ul. Handlowa 10", PostalCode = "00-950", City = "Warszawa", CountryCode = "PL" } },
            Buyer = new InvoiceParty { Name = "Odbiorca", Nip = buyerNip,
                Address = new Address { Street = "ul. Kwiatowa 1/2", PostalCode = "00-001", City = "Warszawa", CountryCode = "PL" } },
            Items = items
        });
    }

    /// <summary>Dodaje do atrapy KSeF fakturę wystawioną na podany NIP nabywcy.</summary>
    private void Publish(string buyerNip, int n, DateTimeOffset storedAt)
    {
        var ksefNumber = $"1234563218-20261030-{n:D6}{buyerNip[..4]}-{n % 100:D2}";
        var meta = new KsefInvoiceMetadata(ksefNumber, $"FV/{n}/10/2026", new DateOnly(2026, 10, 30), storedAt, storedAt,
            "1234563218", "Hurtownia Dostawca sp. z o.o.", 115m, 19.25m, 134.25m, "PLN");
        lock (factory.Ksef.Received)
        {
            if (!factory.Ksef.Received.TryGetValue(buyerNip, out var list)) factory.Ksef.Received[buyerNip] = list = [];
            list.Add((meta, ForeignInvoiceXml("1234563218", buyerNip, meta.InvoiceNumber, 3)));
        }
    }

    private async Task<(HttpClient client, string nip)> CompanyWithTokenAsync()
    {
        var nip = TestUsers.RandomNip();
        var client = await factory.CreateClientWithCompanyAsync(nip);
        (await client.PostAsync("/api/company/ksef/test-token", null)).EnsureSuccessStatusCode();
        return (client, nip);
    }

    /// <summary>Synchronizacja tak, jak wykonałaby ją usługa w tle (bez ograniczenia częstotliwości ręcznego wywołania).</summary>
    private async Task<(SyncResultDto Result, Company Company)> SyncAsync(string nip)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var company = await db.Companies.SingleAsync(c => c.Nip == nip);
        var result = await scope.ServiceProvider.GetRequiredService<ReceivedInvoiceSync>().SyncAsync(company);
        return (result, company);
    }

    [Fact]
    public async Task Synchronizacja_PobieraNoweFaktury_BezDuplikatow_IPrzesuwaPunktKontrolnyDoHwm()
    {
        var (client, nip) = await CompanyWithTokenAsync();
        Publish(nip, 1, T0);
        Publish(nip, 2, T0.AddMinutes(5));
        factory.Ksef.Hwm = DateTimeOffset.UtcNow;

        var (first, company) = await SyncAsync(nip);
        Assert.Equal(2, first.Downloaded);
        Assert.True(first.Complete);
        Assert.Equal(factory.Ksef.Hwm.Value.UtcDateTime, company.ReceivedInvoicesSyncedUntil!.Value, TimeSpan.FromMilliseconds(1));

        var list = await client.GetFromJsonAsync<PagedResult<ReceivedInvoiceListItemDto>>("/api/received-invoices");
        Assert.Equal(2, list!.TotalCount);
        Assert.Equal("Hurtownia Dostawca sp. z o.o.", list.Items[0].SellerName);

        // Druga synchronizacja: zapytanie od punktu kontrolnego, nic do pobrania
        var (second, _) = await SyncAsync(nip);
        Assert.Equal(0, second.Downloaded);
        Assert.Equal(factory.Ksef.Hwm.Value, factory.Ksef.Queries.Last(q => q.Nip == nip).From, TimeSpan.FromMilliseconds(1));
        Assert.Equal(2, (await client.GetFromJsonAsync<PagedResult<ReceivedInvoiceListItemDto>>("/api/received-invoices"))!.TotalCount);
    }

    [Fact]
    public async Task SzczegolyIXml_SaZLokalnejBazy_ZTrescOdczytanaZXml()
    {
        var (client, nip) = await CompanyWithTokenAsync();
        Publish(nip, 7, T0);
        factory.Ksef.Hwm = DateTimeOffset.UtcNow;
        await SyncAsync(nip);
        var item = (await client.GetFromJsonAsync<PagedResult<ReceivedInvoiceListItemDto>>("/api/received-invoices"))!.Items.Single();

        var details = await client.GetFromJsonAsync<ReceivedInvoiceDto>($"/api/received-invoices/{item.Id}");
        Assert.Null(details!.DocumentError);
        Assert.Equal("FV/7/10/2026", details.Document!.InvoiceNumber);
        Assert.Equal(nip, details.Document.Buyer.Nip);
        Assert.Equal(2, details.Document.Lines.Count);

        var xml = await client.GetStringAsync($"/api/received-invoices/{item.Id}/xml");
        Assert.Contains("<P_2>FV/7/10/2026</P_2>", xml);
    }

    [Fact]
    public async Task FakturyPozaHwm_SaPobieranePoPrzesunieciuHwm()
    {
        var (_, nip) = await CompanyWithTokenAsync();
        var now = DateTimeOffset.UtcNow;
        Publish(nip, 1, now.AddMinutes(-10));
        Publish(nip, 2, now.AddMinutes(-1)); // jeszcze powyżej HWM - KSeF nie gwarantuje kompletności tego okresu
        factory.Ksef.Hwm = now.AddMinutes(-5);

        Assert.Equal(1, (await SyncAsync(nip)).Result.Downloaded);

        factory.Ksef.Hwm = now;
        Assert.Equal(1, (await SyncAsync(nip)).Result.Downloaded);
    }

    [Fact]
    public async Task LimitPobranNaSynchronizacje_ResztaWKolejnymPrzebiegu()
    {
        var (_, nip) = await CompanyWithTokenAsync();
        for (var i = 0; i < ReceivedInvoiceSync.MaxDownloadsPerSync + 5; i++) Publish(nip, 100 + i, T0.AddSeconds(i));
        factory.Ksef.Hwm = DateTimeOffset.UtcNow;

        var (first, company) = await SyncAsync(nip);
        Assert.Equal(ReceivedInvoiceSync.MaxDownloadsPerSync, first.Downloaded);
        Assert.False(first.Complete);
        // punkt kontrolny na ostatniej pobranej fakturze, nie dalej
        Assert.Equal(T0.AddSeconds(ReceivedInvoiceSync.MaxDownloadsPerSync - 1).UtcDateTime, company.ReceivedInvoicesSyncedUntil!.Value);

        var (second, _) = await SyncAsync(nip);
        Assert.Equal(5, second.Downloaded);
        Assert.True(second.Complete);
    }

    [Fact]
    public async Task BladPobierania_JestZapisany_IFakturaPobranaPrzyKolejnejSynchronizacji()
    {
        var (client, nip) = await CompanyWithTokenAsync();
        Publish(nip, 1, T0);
        factory.Ksef.Hwm = DateTimeOffset.UtcNow;
        factory.Ksef.DownloadFailure = new HttpRequestException("KSeF niedostępny");
        try
        {
            var (result, company) = await SyncAsync(nip);
            Assert.Equal(0, result.Downloaded);
            Assert.Contains("niedostępny", company.ReceivedInvoicesLastSyncError);
            Assert.Null(company.ReceivedInvoicesSyncedUntil); // punkt kontrolny nie przeskoczył niepobranej faktury
            Assert.Contains("niedostępny", (await client.GetFromJsonAsync<ReceivedSyncStatusDto>("/api/received-invoices/sync-status"))!.LastError);
        }
        finally
        {
            factory.Ksef.DownloadFailure = null;
        }

        var (retry, after) = await SyncAsync(nip);
        Assert.Equal(1, retry.Downloaded);
        Assert.Null(after.ReceivedInvoicesLastSyncError);
    }

    [Fact]
    public async Task RecznaSynchronizacja_WymagaTokenu_IJestOgraniczonaCzestotliwoscia()
    {
        var withoutToken = await factory.CreateClientWithCompanyAsync(TestUsers.RandomNip());
        Assert.Equal(HttpStatusCode.Conflict, (await withoutToken.PostAsync("/api/received-invoices/sync", null)).StatusCode);

        var (client, nip) = await CompanyWithTokenAsync();
        Publish(nip, 1, T0);
        factory.Ksef.Hwm = DateTimeOffset.UtcNow;

        var first = await client.PostAsync("/api/received-invoices/sync", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(1, (await first.Content.ReadFromJsonAsync<SyncResultDto>())!.Downloaded);

        var tooSoon = await client.PostAsync("/api/received-invoices/sync", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, tooSoon.StatusCode);
    }

    [Fact]
    public async Task UzytkownikB_NieWidziFakturOtrzymanychUzytkownikaA()
    {
        var (clientA, nipA) = await CompanyWithTokenAsync();
        Publish(nipA, 1, T0);
        factory.Ksef.Hwm = DateTimeOffset.UtcNow;
        await SyncAsync(nipA);
        var id = (await clientA.GetFromJsonAsync<PagedResult<ReceivedInvoiceListItemDto>>("/api/received-invoices"))!.Items.Single().Id;

        var clientB = await factory.CreateClientWithCompanyAsync(TestUsers.RandomNip());
        Assert.Equal(0, (await clientB.GetFromJsonAsync<PagedResult<ReceivedInvoiceListItemDto>>("/api/received-invoices"))!.TotalCount);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/received-invoices/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/received-invoices/{id}/xml")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.PostAsync($"/api/received-invoices/{id}/bank-account-check", null)).StatusCode);
    }

    // ------------------------------------------------------------------ weryfikacja rachunku (Biała lista)

    /// <summary>Faktura otrzymana zapisana wprost w bazie, z podanym rachunkiem sprzedawcy 1234563218.</summary>
    private async Task<int> InsertReceivedAsync(string buyerNip, string account)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var company = await db.Companies.SingleAsync(c => c.Nip == buyerNip);
        var invoice = new ReceivedInvoice
        {
            CompanyId = company.Id,
            KsefNumber = $"1234563218-20261030-{Guid.NewGuid():N}"[..37] + "-00",
            InvoiceNumber = "FV/99/10/2026", SellerNip = "1234563218", SellerName = "Hurtownia Dostawca sp. z o.o.",
            IssueDate = new DateOnly(2026, 10, 30), TotalNet = 115m, TotalVat = 19.25m, TotalGross = 134.25m,
            KsefAcceptedAt = DateTime.UtcNow, DownloadedAt = DateTime.UtcNow,
            Xml = ForeignInvoiceXml("1234563218", buyerNip, "FV/99/10/2026", 3).Replace("61109010140000071219812874", account)
        };
        db.ReceivedInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice.Id;
    }

    [Fact]
    public async Task RachunekNaBialejLiscie_WynikZapisanyPrzyFakturze_IPonowneSprawdzenieZPamieciPodrecznej()
    {
        var nip = TestUsers.RandomNip();
        var client = await factory.CreateClientWithCompanyAsync(nip);
        var id = await InsertReceivedAsync(nip, "61109010140000071219812874");

        // Przed sprawdzeniem - brak wyniku
        Assert.Null((await client.GetFromJsonAsync<ReceivedInvoiceDto>($"/api/received-invoices/{id}"))!.BankAccountCheck);

        var before = factory.VatWhiteList.AccountChecks;
        var response = await client.PostAsync($"/api/received-invoices/{id}/bank-account-check", null);
        response.EnsureSuccessStatusCode();
        var check = (await response.Content.ReadFromJsonAsync<BankAccountCheckDto>())!;
        Assert.True(check.OnWhiteList);
        Assert.Equal("61109010140000071219812874", check.Account);
        Assert.NotNull(check.RequestId);

        // Wynik zapisany: szczegóły i lista
        var details = await client.GetFromJsonAsync<ReceivedInvoiceDto>($"/api/received-invoices/{id}");
        Assert.Equal(check.RequestId, details!.BankAccountCheck!.RequestId);
        Assert.True(details.BankAccountCheck.OnWhiteList);
        var list = await client.GetFromJsonAsync<PagedResult<ReceivedInvoiceListItemDto>>("/api/received-invoices");
        Assert.True(list!.Items.Single(i => i.Id == id).BankAccountOnWhiteList);

        // Ponowne sprawdzenie tego samego dnia - bez zapytania do MF
        (await client.PostAsync($"/api/received-invoices/{id}/bank-account-check", null)).EnsureSuccessStatusCode();
        Assert.Equal(1, factory.VatWhiteList.AccountChecks - before);
    }

    [Fact]
    public async Task RachunekSpozaWykazu_OznaczonyNaLiscie()
    {
        var nip = TestUsers.RandomNip();
        var client = await factory.CreateClientWithCompanyAsync(nip);
        var id = await InsertReceivedAsync(nip, "10105000997603123456789123");

        var check = await (await client.PostAsync($"/api/received-invoices/{id}/bank-account-check", null))
            .Content.ReadFromJsonAsync<BankAccountCheckDto>();
        Assert.False(check!.OnWhiteList);

        var list = await client.GetFromJsonAsync<PagedResult<ReceivedInvoiceListItemDto>>("/api/received-invoices");
        Assert.False(list!.Items.Single(i => i.Id == id).BankAccountOnWhiteList);
    }

    [Fact]
    public async Task RachunekZagraniczny_409_BezZapytaniaDoMF()
    {
        var nip = TestUsers.RandomNip();
        var client = await factory.CreateClientWithCompanyAsync(nip);
        var id = await InsertReceivedAsync(nip, "DE89370400440532013000");

        var before = factory.VatWhiteList.AccountChecks;
        var response = await client.PostAsync($"/api/received-invoices/{id}/bank-account-check", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("wyłącznie rachunki prowadzone w Polsce", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, factory.VatWhiteList.AccountChecks);
    }

    [Theory]
    [InlineData("61 1090 1014 0000 0712 1981 2874", "61109010140000071219812874")]
    [InlineData("PL61109010140000071219812874", "61109010140000071219812874")]
    [InlineData("DE89370400440532013000", null)]
    [InlineData("6110901014000007121981287", null)]
    public void NumerRachunku_NormalizowanyDoNrb(string account, string? expected) =>
        Assert.Equal(expected, FakturowanieKSeF.Api.Integrations.VatWhiteList.VatRegistryLookup.ToNrb(account));
}
