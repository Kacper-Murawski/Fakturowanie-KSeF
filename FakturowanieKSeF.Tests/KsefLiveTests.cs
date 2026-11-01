using FakturowanieKSeF.Contracts.Calculation;
using FakturowanieKSeF.Ksef;
using FakturowanieKSeF.Ksef.Fa3;
using FakturowanieKSeF.Models;
using Microsoft.Extensions.DependencyInjection;

namespace FakturowanieKSeF.Tests;

/// <summary>Test uruchamiany tylko na żądanie (KSEF_LIVE_TESTS=1) - wymaga dostępu do testowego KSeF.</summary>
public sealed class LiveKsefFactAttribute : FactAttribute
{
    public LiveKsefFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("KSEF_LIVE_TESTS") != "1")
            Skip = "Test na żywo z testowym KSeF - uruchom z KSEF_LIVE_TESTS=1.";
    }
}

/// <summary>
/// Prawdziwa bramka <see cref="KsefGateway"/> na ŚRODOWISKU TESTOWYM KSeF: token testowy, wysyłka faktury
/// z generatora aplikacji, status, numer KSeF, zamknięcie sesji i UPO.
/// </summary>
public class KsefLiveTests
{
    [LiveKsefFact]
    public async Task PrawdziwaBramka_WysylaFaktureDoTestowegoKsef_IOdbieraNumerIUpo()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKsefTestClient();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var gateway = scope.ServiceProvider.GetRequiredService<IKsefGateway>();

        var nip = RandomNip();
        var token = await gateway.GenerateTestTokenAsync(nip);
        await gateway.VerifyTokenAsync(nip, token);

        var items = new List<InvoiceItem>
        {
            Line(1, "Usługa programistyczna", 8, 125m, VatRate.Rate23),
            Line(2, "Licencja", 1, 99.99m, VatRate.Rate8)
        };
        var totals = InvoiceCalculator.Calculate(items.Select(i => new LineInput(i.Quantity, i.UnitNetPrice, i.VatRate)));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var invoice = new Invoice
        {
            Number = $"FV/LIVE/{Guid.NewGuid():N}"[..20], IssueDate = today, Currency = "PLN",
            PaymentMethod = PaymentMethod.Transfer, DueDate = today.AddDays(7), BankAccountNumber = "61109010140000071219812874",
            Seller = new InvoiceParty { Name = "Firma Testowa (test automatyczny)", Nip = nip, Address = Addr() },
            Buyer = new InvoiceParty { Name = "Anna Kowalska", Address = Addr() },
            Items = items, TotalNet = totals.TotalNet, TotalVat = totals.TotalVat, TotalGross = totals.TotalGross
        };
        var xml = new Fa3XmlGenerator().GenerateBytes(invoice);
        Assert.True(new Fa3SchemaValidator().Validate(xml).IsValid);

        var sent = await gateway.SendInvoiceAsync(nip, token, xml);

        KsefInvoiceStatus status;
        var attempts = 0;
        do
        {
            await Task.Delay(1000);
            status = await gateway.GetInvoiceStatusAsync(nip, token, sent.SessionReferenceNumber, sent.InvoiceReferenceNumber);
        } while (status.IsProcessing && ++attempts < 60);

        Assert.True(status.IsAccepted, $"KSeF nie przyjął faktury: {status.Code} {status.Description} {string.Join("; ", status.Details)}");
        Assert.StartsWith(nip + "-", status.KsefNumber);

        await gateway.CloseSessionAsync(nip, token, sent.SessionReferenceNumber);

        string? upo = null;
        for (var i = 0; i < 30 && upo is null; i++)
        {
            await Task.Delay(2000);
            upo = await gateway.GetUpoAsync(nip, token, sent.SessionReferenceNumber, status.KsefNumber!);
        }

        Assert.NotNull(upo);
        Assert.Contains(status.KsefNumber!, upo);
        Assert.Contains("środowisko testowe", upo);
    }

    private static Address Addr() => new() { Street = "ul. Testowa 1", PostalCode = "00-001", City = "Warszawa", CountryCode = "PL" };

    private static InvoiceItem Line(int pos, string name, decimal qty, decimal price, VatRate rate)
    {
        var l = InvoiceCalculator.CalculateLine(new LineInput(qty, price, rate));
        return new InvoiceItem { Position = pos, Name = name, Unit = "szt.", Quantity = qty, UnitNetPrice = price, VatRate = rate,
            NetAmount = l.NetAmount, VatAmount = l.VatAmount, GrossAmount = l.GrossAmount };
    }

    private static string RandomNip()
    {
        int[] w = [6, 5, 7, 2, 3, 4, 5, 6, 7];
        while (true)
        {
            var d = Enumerable.Range(0, 9).Select(i => i < 2 ? Random.Shared.Next(1, 10) : Random.Shared.Next(0, 10)).ToArray();
            var c = d.Select((x, i) => x * w[i]).Sum() % 11;
            if (c < 10) return string.Concat(d) + c;
        }
    }
}
