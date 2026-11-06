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

    [LiveKsefFact]
    public async Task FakturaWystawionaPrzezFirmeA_JestOdbieranaPrzezFirmeB()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKsefTestClient();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var gateway = scope.ServiceProvider.GetRequiredService<IKsefGateway>();

        var nipA = RandomNip();
        var nipB = RandomNip();
        var tokenA = await gateway.GenerateTestTokenAsync(nipA);
        var tokenB = await gateway.GenerateTestTokenAsync(nipB);
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        // Firma A wystawia fakturę na NIP firmy B
        var items = new List<InvoiceItem> { Line(1, "Materiały biurowe", 4, 37.5m, VatRate.Rate23) };
        var totals = InvoiceCalculator.Calculate(items.Select(i => new LineInput(i.Quantity, i.UnitNetPrice, i.VatRate)));
        var number = $"FV/B2B/{Random.Shared.Next(100000, 999999)}";
        var invoice = new Invoice
        {
            Number = number, IssueDate = DateOnly.FromDateTime(DateTime.UtcNow), Currency = "PLN", PaymentMethod = PaymentMethod.Cash,
            Seller = new InvoiceParty { Name = "Firma A (test)", Nip = nipA, Address = Addr() },
            Buyer = new InvoiceParty { Name = "Firma B (test)", Nip = nipB, Address = Addr() },
            Items = items, TotalNet = totals.TotalNet, TotalVat = totals.TotalVat, TotalGross = totals.TotalGross
        };
        var sent = await gateway.SendInvoiceAsync(nipA, tokenA, new Fa3XmlGenerator().GenerateBytes(invoice));
        KsefInvoiceStatus status;
        var attempts = 0;
        do
        {
            await Task.Delay(1000);
            status = await gateway.GetInvoiceStatusAsync(nipA, tokenA, sent.SessionReferenceNumber, sent.InvoiceReferenceNumber);
        } while (status.IsProcessing && ++attempts < 60);
        Assert.True(status.IsAccepted, $"{status.Code} {status.Description}");
        await gateway.CloseSessionAsync(nipA, tokenA, sent.SessionReferenceNumber);

        // Firma B wyszukuje faktury otrzymane (do HWM) - czekamy, aż HWM obejmie nową fakturę
        KsefInvoiceMetadata? found = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 10 && found is null; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(15));
            var page = await gateway.QueryReceivedInvoicesAsync(nipB, tokenB, startedAt, 0, 100);
            found = page.Invoices.FirstOrDefault(x => x.KsefNumber == status.KsefNumber);
            Console.WriteLine($"[{sw.Elapsed:mm\\:ss}] HWM={page.PermanentStorageHwmDate:O}, faktur={page.Invoices.Count}, znaleziono={found is not null}");
        }

        Assert.NotNull(found);
        Assert.Equal(number, found.InvoiceNumber);
        Assert.Equal(nipA, found.SellerNip);
        Assert.Equal(totals.TotalGross, found.GrossAmount);

        var xml = await gateway.DownloadInvoiceAsync(nipB, tokenB, found.KsefNumber);
        var document = Fa3InvoiceReader.Read(xml);
        Assert.Equal(number, document.InvoiceNumber);
        Assert.Equal(nipB, document.Buyer.Nip);
    }

    [LiveKsefFact]
    public async Task FakturaKorygujaca_JestPrzyjmowanaPrzezTestowyKsef()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKsefTestClient();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var gateway = scope.ServiceProvider.GetRequiredService<IKsefGateway>();

        var nip = RandomNip();
        var token = await gateway.GenerateTestTokenAsync(nip);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        async Task<string> SendAsync(Invoice invoice)
        {
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
            return status.KsefNumber!;
        }

        Invoice New(string number, List<InvoiceItem> items) => new()
        {
            Number = number, IssueDate = today, Currency = "PLN", PaymentMethod = PaymentMethod.Transfer, DueDate = today.AddDays(7),
            BankAccountNumber = "61109010140000071219812874",
            Seller = new InvoiceParty { Name = "Firma Testowa (test automatyczny)", Nip = nip, Address = Addr() },
            Buyer = new InvoiceParty { Name = "Odbiorca Testowy sp. z o.o.", Nip = "1234563218", Address = Addr() },
            Items = items
        };

        // Faktura pierwotna
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var original = New($"FV/LIVE/{suffix}", [Line(1, "Usługa programistyczna", 10, 150m, VatRate.Rate23), Line(2, "Książka", 1, 50m, VatRate.Rate5)]);
        var originalKsef = await SendAsync(original);

        // Korekta: obniżka ceny usługi (stan przed i po) - kwoty ujemne, wiersze StanPrzed
        var before1 = Line(1, "Usługa programistyczna", 10, 150m, VatRate.Rate23);
        var before2 = Line(2, "Książka", 1, 50m, VatRate.Rate5);
        before1.BeforeCorrection = before2.BeforeCorrection = true;
        var correction = New($"KOR/LIVE/{suffix}",
            [before1, before2, Line(3, "Usługa programistyczna", 10, 120m, VatRate.Rate23), Line(4, "Książka", 1, 50m, VatRate.Rate5)]);
        correction.Kind = InvoiceKind.Correction;
        correction.CorrectedInvoiceNumber = original.Number;
        correction.CorrectedInvoiceIssueDate = original.IssueDate;
        correction.CorrectedInvoiceKsefNumber = originalKsef;
        correction.CorrectionReason = "Rabat (test automatyczny)";
        correction.CorrectionType = CorrectionType.CorrectionDate;

        var correctionKsef = await SendAsync(correction);
        Assert.StartsWith(nip + "-", correctionKsef);
    }

    [LiveKsefFact]
    public async Task FakturaOznaczonaJakoZaplacona_JestPrzyjmowanaPrzezTestowyKsef()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKsefTestClient();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var gateway = scope.ServiceProvider.GetRequiredService<IKsefGateway>();

        var nip = RandomNip();
        var token = await gateway.GenerateTestTokenAsync(nip);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Sprzedaż za gotówkę zapłacona w dniu wystawienia: Platnosc/Zaplacono + DataZaplaty, bez terminu
        var invoice = new Invoice
        {
            Number = $"FV/LIVE/{Guid.NewGuid().ToString("N")[..8]}", IssueDate = today, Currency = "PLN",
            PaymentMethod = PaymentMethod.Cash, PaidOn = today,
            Seller = new InvoiceParty { Name = "Firma Testowa (test automatyczny)", Nip = nip, Address = Addr() },
            Buyer = new InvoiceParty { Name = "Odbiorca Testowy sp. z o.o.", Nip = "1234563218", Address = Addr() },
            Items = [Line(1, "Naprawa sprzętu", 1, 200m, VatRate.Rate23)]
        };
        var xml = new Fa3XmlGenerator().GenerateBytes(invoice);
        var validation = new Fa3SchemaValidator().Validate(xml);
        Assert.True(validation.IsValid, string.Join("\n", validation.Errors));
        Assert.Contains("<Zaplacono>1</Zaplacono>", System.Text.Encoding.UTF8.GetString(xml));

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
