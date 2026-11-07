using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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

/// <summary>Import faktur sprzedażowych wystawionych poza aplikacją (D44): dane z XML, deduplikacja, korekty, izolacja.</summary>
public class IssuedImportTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Address BuyerAddress = new() { Street = "ul. Handlowa 10", PostalCode = "30-001", City = "Kraków", CountryCode = "PL" };

    private async Task<(HttpClient Client, string Nip)> CompanyAsync()
    {
        var nip = TestUsers.RandomNip();
        var client = await factory.CreateClientWithCompanyAsync(nip);
        (await client.PostAsync("/api/company/ksef/test-token", null)).EnsureSuccessStatusCode();
        return (client, nip);
    }

    /// <summary>Faktura wystawiona „w innym programie” przez firmę (sprzedawca = NIP firmy) i trwale zapisana w KSeF.</summary>
    private string PublishExternal(string sellerNip, string number, decimal price, string? correctedKsef = null, string kind = "VAT")
    {
        var line = InvoiceCalculator.CalculateLine(new LineInput(2, price, VatRate.Rate23));
        var invoice = new Invoice
        {
            Number = number, IssueDate = new DateOnly(2026, 11, 2), Currency = "PLN", PaymentMethod = PaymentMethod.Transfer,
            DueDate = new DateOnly(2026, 11, 16), BankAccountNumber = "61109010140000071219812874",
            Seller = new InvoiceParty { Name = "Moja Firma sp. z o.o.", Nip = sellerNip, Address = BuyerAddress },
            Buyer = new InvoiceParty { Name = "Klient z innego programu S.A.", Nip = "1234563218", Address = BuyerAddress },
            Items = [new InvoiceItem { Position = 1, Name = "Serwis", Unit = "szt.", Quantity = 2, UnitNetPrice = price, VatRate = VatRate.Rate23,
                NetAmount = line.NetAmount, VatAmount = line.VatAmount, GrossAmount = line.GrossAmount }]
        };
        if (correctedKsef is not null)
        {
            var before = InvoiceCalculator.CalculateLine(new LineInput(2, 100m, VatRate.Rate23));
            invoice.Kind = InvoiceKind.Correction;
            invoice.CorrectionReason = "Rabat";
            invoice.CorrectedInvoiceNumber = "INNY/1/2026";
            invoice.CorrectedInvoiceIssueDate = new DateOnly(2026, 11, 2);
            invoice.CorrectedInvoiceKsefNumber = correctedKsef;
            invoice.Items.Insert(0, new InvoiceItem { Position = 0, BeforeCorrection = true, Name = "Serwis", Unit = "szt.", Quantity = 2,
                UnitNetPrice = 100m, VatRate = VatRate.Rate23, NetAmount = before.NetAmount, VatAmount = before.VatAmount, GrossAmount = before.GrossAmount });
        }
        var xml = new Fa3XmlGenerator().Generate(invoice);
        if (kind != "VAT") xml = xml.Replace("<RodzajFaktury>VAT</RodzajFaktury>", $"<RodzajFaktury>{kind}</RodzajFaktury>");

        var stored = DateTimeOffset.UtcNow.AddMinutes(-30);
        var ksefNumber = $"{sellerNip}-20261102-{Random.Shared.NextInt64(0, 0xFFFFFFFFFFFF):X12}-00";
        var meta = new KsefInvoiceMetadata(ksefNumber, number, new DateOnly(2026, 11, 2), stored, stored, sellerNip, "Moja Firma",
            0, 0, 0, "PLN", Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(xml))));
        lock (factory.Ksef.Received)
        {
            if (!factory.Ksef.Issued.TryGetValue(sellerNip, out var list)) factory.Ksef.Issued[sellerNip] = list = [];
            list.Add((meta, xml));
        }
        return ksefNumber;
    }

    private async Task<SyncResultDto> ImportAsync(string nip)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var company = await db.Companies.SingleAsync(c => c.Nip == nip);
        return await scope.ServiceProvider.GetRequiredService<IssuedInvoiceImport>().SyncAsync(company);
    }

    [Fact]
    public async Task FakturaZInnegoProgramu_JestImportowana_ZDanymiZXml_IBezDuplikatow()
    {
        var (client, nip) = await CompanyAsync();
        var ksef = PublishExternal(nip, "INNY/1/2026", 100m);

        var result = await ImportAsync(nip);
        Assert.Equal(1, result.Downloaded);

        var list = (await client.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices"))!;
        var item = Assert.Single(list.Items);
        Assert.Equal(InvoiceOrigin.KsefImport, item.Origin);
        Assert.Equal(InvoiceStatus.Accepted, item.Status);
        Assert.Equal(PaymentStatus.Unpaid, item.PaymentStatus);

        var invoice = (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{item.Id}"))!;
        Assert.Equal("INNY/1/2026", invoice.Number);
        Assert.Equal(ksef, invoice.KsefNumber);
        Assert.Equal(246m, invoice.TotalGross);
        Assert.Equal("Klient z innego programu S.A.", invoice.Buyer.Name);
        Assert.Equal(("30-001", "Kraków"), (invoice.Buyer.Address.PostalCode, invoice.Buyer.Address.City));
        Assert.Equal(new DateOnly(2026, 11, 16), invoice.DueDate);
        Assert.Equal("Serwis", Assert.Single(invoice.Items).Name);

        // XML i PDF to dokument z KSeF; faktura tylko do odczytu, ale z rozliczeniem
        Assert.Contains("INNY/1/2026", await client.GetStringAsync($"/api/invoices/{invoice.Id}/xml"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/invoices/{invoice.Id}/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/invoices/{invoice.Id}")).StatusCode);
        (await client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/payments",
            new AddPaymentRequest { PaidOn = new DateOnly(2026, 11, 3), Amount = 246m })).EnsureSuccessStatusCode();

        // Ponowny import (np. od początku okresu) - bez duplikatu; usługa wysyłki nie pyta o UPO
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Companies.Where(c => c.Nip == nip).ExecuteUpdateAsync(u => u.SetProperty(c => c.IssuedInvoicesSyncedUntil, (DateTime?)null));
        }
        Assert.Equal(0, (await ImportAsync(nip)).Downloaded);
        Assert.Equal(1, (await client.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices"))!.TotalCount);
        Assert.Equal(0, await factory.ProcessKsefAsync());
    }

    [Fact]
    public async Task FakturaWyslanaZAplikacji_NieJestImportowanaPonownie_ATenSamNumerZInnegoProgramuJestDozwolony()
    {
        var (client, nip) = await CompanyAsync();
        var own = await factory.AcceptedInvoiceAsync(client);

        // KSeF zwraca też fakturę wysłaną przez aplikację (ten sam XML)
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var submission = await db.KsefSubmissions.SingleAsync(s => s.InvoiceId == own.Id);
            var stored = DateTimeOffset.UtcNow.AddMinutes(-20);
            factory.Ksef.Issued[nip] =
            [
                (new KsefInvoiceMetadata(own.KsefNumber!, own.Number, own.IssueDate, stored, stored, nip, "Moja Firma", 0, 0, 0, "PLN",
                    submission.InvoiceHash), submission.SentXml)
            ];
        }
        PublishExternal(nip, own.Number, 50m); // inny program użył tego samego numeru

        var result = await ImportAsync(nip);

        Assert.Equal(1, result.Downloaded);
        var all = (await client.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices"))!.Items;
        Assert.Equal(2, all.Count);
        Assert.Equal(2, all.Count(i => i.Number == own.Number));
    }

    [Fact]
    public async Task KorektaZInnegoProgramu_ZmieniaNaleznoscZaimportowanejFaktury_INieobslugiwanyRodzajJestPomijany()
    {
        var (client, nip) = await CompanyAsync();
        var original = PublishExternal(nip, "INNY/1/2026", 100m);
        PublishExternal(nip, "INNY/KOR/1/2026", 80m, correctedKsef: original);
        PublishExternal(nip, "INNY/ZAL/1/2026", 10m, kind: "ZAL");

        var result = await ImportAsync(nip);

        Assert.Equal(2, result.Downloaded);
        Assert.Equal(1, result.Skipped);
        var vat = (await client.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices?q=INNY/1/2026"))!.Items.Single(i => i.Kind == InvoiceKind.Vat);
        var details = (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{vat.Id}"))!;
        Assert.Single(details.Corrections!);
        Assert.Equal(196.80m, details.Payment!.TotalDue); // 246,00 - 49,20
    }

    [Fact]
    public async Task RecznyImport_WymagaTokenu_JestOgraniczonyCzasowo_IDotyczyTylkoWlasnejFirmy()
    {
        var noToken = await factory.CreateClientWithCompanyAsync(TestUsers.RandomNip());
        Assert.Equal(HttpStatusCode.Conflict, (await noToken.PostAsync("/api/invoices/import-from-ksef", null)).StatusCode);

        var (clientA, nipA) = await CompanyAsync();
        PublishExternal(nipA, "A/1/2026", 100m);
        var (clientB, _) = await CompanyAsync();

        var first = await clientA.PostAsync("/api/invoices/import-from-ksef", null);
        Assert.Equal(1, (await first.Content.ReadFromJsonAsync<SyncResultDto>())!.Downloaded);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await clientA.PostAsync("/api/invoices/import-from-ksef", null)).StatusCode);

        (await clientB.PostAsync("/api/invoices/import-from-ksef", null)).EnsureSuccessStatusCode();
        Assert.Equal(0, (await clientB.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices"))!.TotalCount);
    }
}
