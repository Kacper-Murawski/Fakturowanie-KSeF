using FakturowanieKSeF.Api.Integrations.Nbp;
using FakturowanieKSeF.Api.Integrations.VatWhiteList;
using FakturowanieKSeF.Contracts.Calculation;
using FakturowanieKSeF.Ksef;
using FakturowanieKSeF.Ksef.Fa3;
using FakturowanieKSeF.Models;
using FakturowanieKSeF.Tests.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace FakturowanieKSeF.Tests;

/// <summary>
/// Testy na żywo z publicznymi API (uruchamiane z KSEF_LIVE_TESTS=1): kursy NBP, wykaz podatników VAT MF
/// i faktura w walucie obcej wysłana do TESTOWEGO KSeF.
/// </summary>
public class ExternalApiLiveTests
{
    private static NbpClient Nbp() => new(new HttpClient { BaseAddress = new Uri(NbpClient.BaseAddress), Timeout = TimeSpan.FromSeconds(20) });

    [LiveKsefFact]
    public async Task Nbp_KursEuroZOstatniegoDniaRoboczego()
    {
        var lookup = new ExchangeRateLookup(Nbp(), new MemoryCache(new MemoryCacheOptions()));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var rate = await lookup.ForTaxPointAsync("EUR", today);

        Assert.Equal("EUR", rate.Currency);
        Assert.True(rate.EffectiveDate < today && rate.EffectiveDate >= today.AddDays(-7));
        Assert.InRange(rate.Mid, 3m, 6m);
        Assert.Matches(@"^\d+/A/NBP/\d{4}$", rate.TableNo);
    }

    [LiveKsefFact]
    public async Task BialaLista_DaneMinisterstwaFinansowPoNip()
    {
        var client = new VatWhiteListClient(new HttpClient { BaseAddress = new Uri(VatWhiteListClient.BaseAddress), Timeout = TimeSpan.FromSeconds(20) });

        // NIP Ministerstwa Finansów - podmiot publiczny, stale obecny w wykazie
        var subject = await client.FindByNipAsync("5260250274", DateOnly.FromDateTime(DateTime.UtcNow));

        Assert.NotNull(subject);
        Assert.Contains("MINISTERSTWO FINANSÓW", subject.Name.ToUpperInvariant());
        Assert.NotNull(subject.RequestId);
        var address = WhiteListAddress.Parse(subject.WorkingAddress ?? subject.ResidenceAddress);
        Assert.Equal("Warszawa", address!.City);
    }

    [LiveKsefFact]
    public async Task FakturaWEuro_ZKursemNbp_JestPrzyjmowanaPrzezTestowyKsef()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKsefTestClient();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var gateway = scope.ServiceProvider.GetRequiredService<IKsefGateway>();

        var nip = TestUsers.RandomNip();
        var token = await gateway.GenerateTestTokenAsync(nip);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rate = await new ExchangeRateLookup(Nbp(), new MemoryCache(new MemoryCacheOptions())).ForTaxPointAsync("EUR", today);

        var line = InvoiceCalculator.CalculateLine(new LineInput(10, 120m, VatRate.Rate23));
        var address = new Address { Street = "ul. Testowa 1", PostalCode = "00-001", City = "Warszawa", CountryCode = "PL" };
        var invoice = new Invoice
        {
            Number = $"FV/EUR/{Guid.NewGuid().ToString("N")[..8]}", IssueDate = today, Currency = "EUR",
            PaymentMethod = PaymentMethod.Transfer, DueDate = today.AddDays(14), BankAccountNumber = "PL61109010140000071219812874",
            ExchangeRate = rate.Mid, ExchangeRateDate = rate.EffectiveDate, ExchangeRateTable = rate.TableNo,
            Seller = new InvoiceParty { Name = "Firma Testowa (test automatyczny)", Nip = nip, Address = address },
            Buyer = new InvoiceParty { Name = "Odbiorca Testowy sp. z o.o.", Nip = "1234563218", Address = address },
            Items = [new InvoiceItem { Position = 1, Name = "Usługa programistyczna", Unit = "godz.", Quantity = 10, UnitNetPrice = 120m,
                VatRate = VatRate.Rate23, NetAmount = line.NetAmount, VatAmount = line.VatAmount, GrossAmount = line.GrossAmount }]
        };
        var xml = new Fa3XmlGenerator().GenerateBytes(invoice);
        var validation = new Fa3SchemaValidator().Validate(xml);
        Assert.True(validation.IsValid, string.Join("\n", validation.Errors));

        var sent = await gateway.SendInvoiceAsync(nip, token, xml);
        KsefInvoiceStatus status;
        var attempts = 0;
        do
        {
            await Task.Delay(1000);
            status = await gateway.GetInvoiceStatusAsync(nip, token, sent.SessionReferenceNumber, sent.InvoiceReferenceNumber);
        } while (status.IsProcessing && ++attempts < 60);
        await gateway.CloseSessionAsync(nip, token, sent.SessionReferenceNumber);

        Assert.True(status.IsAccepted, $"KSeF nie przyjął dokumentu: {status.Code} {status.Description} {string.Join("; ", status.Details)}");
    }
}
