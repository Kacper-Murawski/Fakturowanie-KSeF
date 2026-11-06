using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Xml.Linq;
using FakturowanieKSeF.Api.Integrations.Nbp;
using FakturowanieKSeF.Contracts.Calculation;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Ksef.Fa3;
using FakturowanieKSeF.Models;
using FakturowanieKSeF.Tests.Infrastructure;
using Microsoft.Extensions.Caching.Memory;

namespace FakturowanieKSeF.Tests;

/// <summary>Kurs NBP: dzień notowania (art. 31a ustawy o VAT), odczyt odpowiedzi API, pamięć podręczna.</summary>
public class ExchangeRateLookupTests
{
    private static (ExchangeRateLookup Lookup, FakeNbpClient Client) Create()
    {
        var client = new FakeNbpClient();
        return (new ExchangeRateLookup(client, new MemoryCache(new MemoryCacheOptions())), client);
    }

    [Theory]
    [InlineData("2026-11-06", "2026-11-05")] // piątek → czwartek
    [InlineData("2026-11-09", "2026-11-06")] // poniedziałek → piątek (weekend pominięty)
    [InlineData("2026-11-12", "2026-11-10")] // dzień po 11 listopada (święto) → 10 listopada
    [InlineData("2026-12-28", "2026-12-23")] // po świętach Bożego Narodzenia
    public async Task KursZOstatniegoDniaRoboczegoPrzedDniemSprzedazy(string taxPoint, string expected)
    {
        var (lookup, _) = Create();
        var rate = await lookup.ForTaxPointAsync("eur", DateOnly.Parse(taxPoint));
        Assert.Equal(DateOnly.Parse(expected), rate.EffectiveDate);
        Assert.Equal("EUR", rate.Currency);
        Assert.Equal(4.3916m, rate.Mid);
    }

    [Fact]
    public async Task PamiecPodreczna_IBladDlaWalutySpozaTabeli()
    {
        var (lookup, client) = Create();
        await lookup.ForTaxPointAsync("EUR", new DateOnly(2026, 11, 6));
        await lookup.ForTaxPointAsync("EUR", new DateOnly(2026, 11, 6));
        Assert.Equal(1, client.Calls);

        await Assert.ThrowsAsync<NbpException>(() => lookup.ForTaxPointAsync("XXX", new DateOnly(2026, 11, 6)));
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? Requested { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requested = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task KlientNbp_OdczytujeOdpowiedzApi_A404ToBrakNotowan()
    {
        // Odpowiedź w formacie API NBP (fragment rzeczywistej odpowiedzi z 11.2026)
        const string json = """{"table":"A","currency":"euro","code":"EUR","rates":[{"no":"215/A/NBP/2026","effectiveDate":"2026-11-04","mid":4.4014},{"no":"216/A/NBP/2026","effectiveDate":"2026-11-05","mid":4.3916}]}""";
        var handler = new StubHandler(HttpStatusCode.OK, json);
        var client = new NbpClient(new HttpClient(handler) { BaseAddress = new Uri(NbpClient.BaseAddress) });

        var rates = await client.GetRatesAsync("EUR", new DateOnly(2026, 10, 23), new DateOnly(2026, 11, 5));

        Assert.Equal("https://api.nbp.pl/api/exchangerates/rates/a/eur/2026-10-23/2026-11-05/?format=json", handler.Requested!.ToString());
        Assert.Equal(2, rates.Count);
        Assert.Equal(new NbpRate("EUR", 4.3916m, new DateOnly(2026, 11, 5), "216/A/NBP/2026"), rates[1]);

        var notFound = new NbpClient(new HttpClient(new StubHandler(HttpStatusCode.NotFound, "404 NotFound")) { BaseAddress = new Uri(NbpClient.BaseAddress) });
        Assert.Empty(await notFound.GetRatesAsync("EUR", new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 8)));
    }
}

/// <summary>Faktura w walucie obcej w strukturze FA(3): kwoty w walucie, podatek w PLN (P_14_xW), kurs w wierszach.</summary>
public class CurrencyXmlTests
{
    private static readonly XNamespace Ns = Fa3XmlGenerator.Namespace;

    private static InvoiceItem Line(int pos, string name, decimal qty, decimal price, VatRate rate)
    {
        var l = InvoiceCalculator.CalculateLine(new LineInput(qty, price, rate));
        return new InvoiceItem { Position = pos, Name = name, Quantity = qty, UnitNetPrice = price, VatRate = rate,
            NetAmount = l.NetAmount, VatAmount = l.VatAmount, GrossAmount = l.GrossAmount };
    }

    [Fact]
    public void VatWPln_PodatekRazyKursZaokraglonyDoGrosza()
    {
        var summary = InvoiceCalculator.Calculate([new LineInput(1, 1000m, VatRate.Rate23), new LineInput(1, 50m, VatRate.Exempt)]).ByVatRate;
        var pln = InvoiceCalculator.VatInPln(summary, 4.3916m);
        var vat = Assert.Single(pln); // tylko stawki z podatkiem
        Assert.Equal(1010.07m, vat.Vat); // 230,00 × 4,3916 = 1010,068
    }

    [Fact]
    public void FakturaWEuro_JestZgodnaZeSchematem_IZawieraPodatekWPln()
    {
        var address = new Address { Street = "ul. Testowa 1", PostalCode = "00-001", City = "Warszawa", CountryCode = "PL" };
        var invoice = new Invoice
        {
            Number = "FV/1/11/2026", IssueDate = new DateOnly(2026, 11, 6), Currency = "EUR", PaymentMethod = PaymentMethod.Transfer,
            DueDate = new DateOnly(2026, 11, 20), BankAccountNumber = "PL61109010140000071219812874",
            ExchangeRate = 4.3916m, ExchangeRateDate = new DateOnly(2026, 11, 5), ExchangeRateTable = "216/A/NBP/2026",
            Seller = new InvoiceParty { Name = "Sprzedawca", Nip = "5260250274", Address = address },
            Buyer = new InvoiceParty { Name = "Nabywca", Nip = "1234563218", Address = address },
            Items = [Line(1, "Usługa", 1, 1000m, VatRate.Rate23), Line(2, "Książka", 2, 25m, VatRate.Rate5)]
        };

        var bytes = new Fa3XmlGenerator().GenerateBytes(invoice);
        var validation = new Fa3SchemaValidator().Validate(bytes);
        Assert.True(validation.IsValid, string.Join("\n", validation.Errors));

        var doc = XDocument.Parse(Encoding.UTF8.GetString(bytes));
        string? F(string name) => doc.Descendants(Ns + name).FirstOrDefault()?.Value;
        Assert.Equal("EUR", F("KodWaluty"));
        Assert.Equal("230.00", F("P_14_1"));
        Assert.Equal("1010.07", F("P_14_1W"));
        Assert.Equal("2.50", F("P_14_3"));
        Assert.Equal("10.98", F("P_14_3W")); // 2,50 × 4,3916 = 10,979
        Assert.All(doc.Descendants(Ns + "FaWiersz"), w => Assert.Equal("4.3916", w.Element(Ns + "KursWaluty")!.Value));

        // Odczyt (podgląd, PDF, eksport)
        var read = Fa3InvoiceReader.Read(Encoding.UTF8.GetString(bytes));
        Assert.Equal(4.3916m, read.ExchangeRate);
        Assert.Equal(1021.05m, read.VatSummary.Sum(v => v.VatPln ?? 0));
    }

    [Fact]
    public void WalutaObcaBezKursu_JestOdrzucana()
    {
        var invoice = new Invoice { Number = "X", Currency = "EUR", Items = [Line(1, "A", 1, 1m, VatRate.Rate23)],
            Seller = new InvoiceParty { Nip = "5260250274" } };
        Assert.Throws<Fa3GenerationException>(() => Fa3XmlGenerator.EnsureSupported(invoice));
    }
}

/// <summary>Faktury w walutach obcych przez API: kurs przy zapisie, błędy NBP, korekta, rozliczenia, eksport.</summary>
public class CurrencyApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static SaveInvoiceRequest Eur(DateOnly? sale = null)
    {
        var r = TestUsers.SampleInvoice(); // wystawiona 01.11.2026 (niedziela)
        r.Currency = "EUR";
        r.SaleDate = sale;
        r.BankAccountNumber = "PL61109010140000071219812874";
        return r;
    }

    [Fact]
    public async Task ZapisFakturyWEuro_UstalaKursNbp_IPodatekWPln_AZmianaDatyPobieraNowyKurs()
    {
        var client = await factory.CreateClientWithCompanyAsync();

        var created = (await (await client.PostAsJsonAsync("/api/invoices", Eur())).Content.ReadFromJsonAsync<InvoiceDto>())!;
        Assert.Equal("EUR", created.Currency);
        Assert.Equal(new DateOnly(2026, 10, 30), created.ExchangeRate!.EffectiveDate); // ostatni dzień roboczy przed 01.11
        Assert.Equal(4.3916m, created.ExchangeRate.Rate);
        Assert.Equal(1526.08m, created.VatSummaryPln!.Sum(s => s.Vat)); // 345,00 × 4,3916 = 1515,10; 2,50 × 4,3916 = 10,98

        // Zmiana daty sprzedaży - kurs z dnia poprzedzającego nową datę (12.11 → 10.11, bo 11.11 to święto)
        var edit = Eur(sale: new DateOnly(2026, 11, 12));
        edit.Version = created.Version;
        var updated = (await (await client.PutAsJsonAsync($"/api/invoices/{created.Id}", edit)).Content.ReadFromJsonAsync<InvoiceDto>())!;
        Assert.Equal(new DateOnly(2026, 11, 10), updated.ExchangeRate!.EffectiveDate);

        // Powrót do PLN - kurs usunięty
        var pln = TestUsers.SampleInvoice();
        pln.Version = updated.Version;
        Assert.Null((await (await client.PutAsJsonAsync($"/api/invoices/{created.Id}", pln)).Content.ReadFromJsonAsync<InvoiceDto>())!.ExchangeRate);
    }

    [Fact]
    public async Task VatWPlnWPozycjiSumy_IWalidacja_NieobslugiwanaWalutaIAwariaNbp()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        var invoice = (await (await client.PostAsJsonAsync("/api/invoices", Eur())).Content.ReadFromJsonAsync<InvoiceDto>())!;
        Assert.Equal([1515.10m, 10.98m], invoice.VatSummaryPln!.Select(s => s.Vat)); // osobno dla każdej stawki

        var unsupported = Eur();
        unsupported.Currency = "XYZ";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/invoices", unsupported)).StatusCode);

        factory.Nbp.Failure = new NbpException("Serwis kursów NBP jest niedostępny.");
        try
        {
            var response = await client.PostAsJsonAsync("/api/invoices", Eur(sale: new DateOnly(2026, 10, 15)));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("kursu NBP", await response.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.BadGateway, (await client.GetAsync("/api/exchange-rates/USD?date=2026-10-14")).StatusCode);
        }
        finally
        {
            factory.Nbp.Failure = null;
        }

        var preview = (await client.GetFromJsonAsync<ExchangeRateDto>("/api/exchange-rates/usd?date=2026-11-09"))!;
        Assert.Equal(("USD", 3.9871m, new DateOnly(2026, 11, 6)), (preview.Currency, preview.Rate, preview.EffectiveDate));
    }

    [Fact]
    public async Task WysylkaDoKsef_XmlWEuro_KorektaZKursemFakturyKorygowanej_IRozliczeniaWPln()
    {
        var client = await factory.CreateClientWithKsefAsync();
        var invoice = await factory.AcceptedInvoiceAsync(client, Eur());

        var xml = await client.GetStringAsync($"/api/invoices/{invoice.Id}/xml");
        Assert.Contains("<KodWaluty>EUR</KodWaluty>", xml);
        Assert.Contains("<P_14_1W>1515.10</P_14_1W>", xml);

        // Korekta (obniżka) - waluta i kurs faktury korygowanej
        var correction = new SaveInvoiceRequest
        {
            CorrectedInvoiceId = invoice.Id, CorrectionReason = "Rabat", IssueDate = new DateOnly(2026, 11, 20),
            Items = [new SaveInvoiceItemRequest { Name = "Usługa programistyczna", Unit = "godz.", Quantity = 10, UnitNetPrice = 100m, VatRate = VatRate.Rate23 },
                     new SaveInvoiceItemRequest { Name = "Książka", Unit = "szt.", Quantity = 1, UnitNetPrice = 50m, VatRate = VatRate.Rate5 }]
        };
        var kor = (await (await client.PostAsJsonAsync("/api/invoices", correction)).Content.ReadFromJsonAsync<InvoiceDto>())!;
        Assert.Equal("EUR", kor.Currency);
        Assert.Equal(invoice.ExchangeRate, kor.ExchangeRate);
        Assert.Equal(-505.03m, kor.VatSummaryPln!.Sum(s => s.Vat)); // -115,00 × 4,3916

        // Należności w PLN nie obejmują faktur w EUR (różne waluty nie są sumowane)
        var summary = (await client.GetFromJsonAsync<PaymentsSummaryDto>("/api/payments/summary"))!;
        Assert.Equal(0, summary.ReceivablesCount);
        Assert.Equal(PaymentStatus.Unpaid, (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{invoice.Id}"))!.Payment!.Status);

        // Eksport: kurs i VAT w PLN w zestawieniu, sumy w PLN bez faktury w EUR
        var csv = Encoding.UTF8.GetString((await client.GetByteArrayAsync("/api/exports/sales.csv?from=2026-11-01&to=2026-11-30"))[3..]);
        Assert.Contains(";Waluta;Kurs;VAT w PLN;", csv);
        Assert.Contains(";EUR;4,3916;1526,08;", csv); // VAT w PLN: 1515,10 + 10,98
        var export = (await client.GetFromJsonAsync<ExportSummaryDto>("/api/exports/summary?from=2026-11-01&to=2026-11-30"))!;
        Assert.Equal(1, export.SalesForeignCurrency);
        Assert.Equal(0m, export.SalesGross);
    }
}
