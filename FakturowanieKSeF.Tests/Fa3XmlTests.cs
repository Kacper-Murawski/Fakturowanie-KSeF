using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Xml.Linq;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Ksef.Fa3;
using FakturowanieKSeF.Models;
using FakturowanieKSeF.Tests.Infrastructure;

namespace FakturowanieKSeF.Tests;

/// <summary>
/// Generowanie XML FA(3): każdy wariant faktury musi przejść walidację względem oficjalnego schematu XSD MF,
/// a wartości w XML muszą odpowiadać danym faktury.
/// </summary>
public class Fa3XmlGeneratorTests
{
    private static readonly XNamespace Ns = Fa3XmlGenerator.Namespace;
    private readonly Fa3XmlGenerator _generator = new(new FixedTimeProvider(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero)));
    private readonly Fa3SchemaValidator _validator = new();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static Address Adres(string street = "ul. Kwiatowa 1/2", string postal = "00-001", string city = "Warszawa") =>
        new() { Street = street, PostalCode = postal, City = city, CountryCode = "PL" };

    private static InvoiceItem Pozycja(int position, string name, decimal qty, decimal price, VatRate rate, string? unit = "szt.")
    {
        var line = FakturowanieKSeF.Contracts.Calculation.InvoiceCalculator.CalculateLine(new(qty, price, rate));
        return new InvoiceItem
        {
            Position = position, Name = name, Unit = unit, Quantity = qty, UnitNetPrice = price, VatRate = rate,
            NetAmount = line.NetAmount, VatAmount = line.VatAmount, GrossAmount = line.GrossAmount
        };
    }

    private static Invoice Faktura(params InvoiceItem[] items) => new()
    {
        Number = "FV/1/11/2026",
        IssueDate = new DateOnly(2026, 11, 1),
        Currency = "PLN",
        PaymentMethod = PaymentMethod.Transfer,
        DueDate = new DateOnly(2026, 11, 15),
        BankAccountNumber = "61109010140000071219812874",
        Seller = new InvoiceParty { Name = "Pracownia Programistyczna Jan Nowak", Nip = "5260250274", Address = Adres() },
        Buyer = new InvoiceParty { Name = "Kontrahent S.A.", Nip = "1234563218", Address = Adres("ul. Polna 5", "30-001", "Kraków") },
        Items = items.ToList()
    };

    private XDocument GenerujIZwaliduj(Invoice invoice)
    {
        var bytes = _generator.GenerateBytes(invoice);
        var result = _validator.Validate(bytes);
        Assert.True(result.IsValid, "XML niezgodny ze schematem FA(3):\n" + string.Join("\n", result.Errors));
        return XDocument.Parse(Encoding.UTF8.GetString(bytes));
    }

    private static string? Pole(XDocument doc, string name) => doc.Descendants(Ns + name).FirstOrDefault()?.Value;

    [Fact]
    public void FakturaB2B_ZDwiemaStawkami_JestZgodnaZeSchematem_IZawieraPoprawneKwoty()
    {
        var doc = GenerujIZwaliduj(Faktura(
            Pozycja(1, "Usługa programistyczna", 10, 150m, VatRate.Rate23, "godz."),
            Pozycja(2, "Książka", 1, 50m, VatRate.Rate5)));

        Assert.Equal("FA (3)", doc.Descendants(Ns + "KodFormularza").Single().Attribute("kodSystemowy")!.Value);
        Assert.Equal("2026-11-01T12:00:00Z", Pole(doc, "DataWytworzeniaFa"));
        Assert.Equal("5260250274", doc.Descendants(Ns + "Podmiot1").Descendants(Ns + "NIP").Single().Value);
        Assert.Equal("1234563218", doc.Descendants(Ns + "Podmiot2").Descendants(Ns + "NIP").Single().Value);
        Assert.Equal("00-001 Warszawa", doc.Descendants(Ns + "Podmiot1").Descendants(Ns + "AdresL2").Single().Value);
        Assert.Equal("FV/1/11/2026", Pole(doc, "P_2"));
        Assert.Equal("1500.00", Pole(doc, "P_13_1"));
        Assert.Equal("345.00", Pole(doc, "P_14_1"));
        Assert.Equal("50.00", Pole(doc, "P_13_3"));
        Assert.Equal("2.50", Pole(doc, "P_14_3"));
        Assert.Null(Pole(doc, "P_13_2"));             // stawki nieużyte - pola nieobecne
        Assert.Equal("1897.50", Pole(doc, "P_15"));
        Assert.Equal("VAT", Pole(doc, "RodzajFaktury"));
        Assert.Equal(2, doc.Descendants(Ns + "FaWiersz").Count());
        Assert.Equal("6", Pole(doc, "FormaPlatnosci"));
        Assert.Equal("2026-11-15", Pole(doc, "Termin"));
        Assert.Equal("1", Pole(doc, "P_19N"));        // brak sprzedaży zwolnionej
    }

    [Fact]
    public void XmlJestWUtf8BezBom_IZaczynaSieOdDeklaracji()
    {
        var bytes = _generator.GenerateBytes(Faktura(Pozycja(1, "Usługa", 1, 100m, VatRate.Rate23)));

        Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..]);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void OsobaPrywatnaBezNip_MaZnacznikBrakID()
    {
        var invoice = Faktura(Pozycja(1, "Konsultacja", 2, 99.99m, VatRate.Rate23));
        invoice.Buyer = new InvoiceParty { Name = "Anna Kowalska", Nip = null, Address = Adres("ul. Leśna 3", "80-001", "Gdańsk") };

        var doc = GenerujIZwaliduj(invoice);

        var buyer = doc.Descendants(Ns + "Podmiot2").Single();
        Assert.Empty(buyer.Descendants(Ns + "NIP"));
        Assert.Equal("1", buyer.Descendants(Ns + "BrakID").Single().Value);
    }

    [Fact]
    public void WszystkieObslugiwaneStawki_SaRaportowaneWOdpowiednichPolach()
    {
        var invoice = Faktura(
            Pozycja(1, "A", 1, 100m, VatRate.Rate23),
            Pozycja(2, "B", 1, 100m, VatRate.Rate22),
            Pozycja(3, "C", 1, 100m, VatRate.Rate8),
            Pozycja(4, "D", 1, 100m, VatRate.Rate7),
            Pozycja(5, "E", 1, 100m, VatRate.Rate5),
            Pozycja(6, "F", 1, 10m, VatRate.Rate0Domestic),
            Pozycja(7, "G", 1, 20m, VatRate.Rate0Wdt),
            Pozycja(8, "H", 1, 30m, VatRate.Rate0Export),
            Pozycja(9, "I", 1, 40m, VatRate.Exempt),
            Pozycja(10, "J", 1, 50m, VatRate.NotSubjectI),
            Pozycja(11, "K", 1, 60m, VatRate.NotSubjectII),
            Pozycja(12, "L", 1, 70m, VatRate.ReverseCharge));
        invoice.ExemptionBasis = "art. 43 ust. 1 pkt 18 ustawy o VAT";

        var doc = GenerujIZwaliduj(invoice);

        Assert.Equal("200.00", Pole(doc, "P_13_1"));   // 23% + 22%
        Assert.Equal("45.00", Pole(doc, "P_14_1"));
        Assert.Equal("200.00", Pole(doc, "P_13_2"));   // 8% + 7%
        Assert.Equal("15.00", Pole(doc, "P_14_2"));
        Assert.Equal("100.00", Pole(doc, "P_13_3"));
        Assert.Equal("10.00", Pole(doc, "P_13_6_1"));
        Assert.Equal("20.00", Pole(doc, "P_13_6_2"));
        Assert.Equal("30.00", Pole(doc, "P_13_6_3"));
        Assert.Equal("40.00", Pole(doc, "P_13_7"));
        Assert.Equal("50.00", Pole(doc, "P_13_8"));
        Assert.Equal("60.00", Pole(doc, "P_13_9"));
        Assert.Equal("70.00", Pole(doc, "P_13_10"));
        Assert.Equal("1", Pole(doc, "P_18"));           // odwrotne obciążenie
        Assert.Equal("1", Pole(doc, "P_19"));           // sprzedaż zwolniona
        Assert.Equal("art. 43 ust. 1 pkt 18 ustawy o VAT", Pole(doc, "P_19A"));
        Assert.Equal("np II", doc.Descendants(Ns + "P_12").ElementAt(10).Value);
    }

    [Fact]
    public void DataSprzedazyInnaNizWystawienia_TrafiaDoP6_ATaSama_JestPomijana()
    {
        var invoice = Faktura(Pozycja(1, "Usługa", 1, 100m, VatRate.Rate23));
        invoice.SaleDate = new DateOnly(2026, 10, 31);
        Assert.Equal("2026-10-31", Pole(GenerujIZwaliduj(invoice), "P_6"));

        invoice.SaleDate = invoice.IssueDate;
        Assert.Null(Pole(GenerujIZwaliduj(invoice), "P_6"));
    }

    [Fact]
    public void PlatnoscGotowka_BezTerminuIRachunku_JestZgodnaZeSchematem()
    {
        var invoice = Faktura(Pozycja(1, "Towar", 3, 9.99m, VatRate.Rate8));
        invoice.PaymentMethod = PaymentMethod.Cash;
        invoice.DueDate = null;
        invoice.BankAccountNumber = null;

        var doc = GenerujIZwaliduj(invoice);

        Assert.Equal("1", Pole(doc, "FormaPlatnosci"));
        Assert.Null(Pole(doc, "RachunekBankowy"));
    }

    [Fact]
    public void FakturaZaplaconaPrzyWystawieniu_MaZaplaconoIDateZaplaty_PrzedFormaPlatnosci()
    {
        var invoice = Faktura(Pozycja(1, "Towar", 1, 100m, VatRate.Rate23));
        invoice.PaymentMethod = PaymentMethod.Card;
        invoice.DueDate = null;
        invoice.BankAccountNumber = null;
        invoice.PaidOn = new DateOnly(2026, 11, 1);

        var doc = GenerujIZwaliduj(invoice);

        var platnosc = doc.Descendants(Ns + "Platnosc").Single().Elements().Select(e => e.Name.LocalName).ToList();
        Assert.Equal(["Zaplacono", "DataZaplaty", "FormaPlatnosci"], platnosc);
        Assert.Equal("1", Pole(doc, "Zaplacono"));
        Assert.Equal("2026-11-01", Pole(doc, "DataZaplaty"));

        // Odczyt (podgląd, PDF) rozpoznaje oznaczenie zapłaty
        Assert.Equal(new DateOnly(2026, 11, 1), Fa3InvoiceReader.Read(doc.ToString()).PaidOn);
    }

    [Fact]
    public void FakturaBezOznaczeniaZaplaty_NieMaElementuZaplacono()
    {
        var doc = GenerujIZwaliduj(Faktura(Pozycja(1, "Towar", 1, 100m, VatRate.Rate23)));
        Assert.Null(Pole(doc, "Zaplacono"));
        Assert.Null(Fa3InvoiceReader.Read(doc.ToString()).PaidOn);
    }

    [Fact]
    public void ZnakiSpecjalneIPolskieLitery_SaPoprawnieKodowane()
    {
        var invoice = Faktura(Pozycja(1, "Zażółć gęślą jaźń <&> \"cytat\"", 1.123456m, 0.12345678m, VatRate.Rate23));
        invoice.Buyer.Name = "Firma \"Kowalski & Syn\" <sp. j.>";

        var doc = GenerujIZwaliduj(invoice);

        Assert.Equal("Zażółć gęślą jaźń <&> \"cytat\"", Pole(doc, "P_7"));
        Assert.Equal("Firma \"Kowalski & Syn\" <sp. j.>", doc.Descendants(Ns + "Podmiot2").Descendants(Ns + "Nazwa").Single().Value);
        Assert.Equal("1.123456", Pole(doc, "P_8B"));    // ilość - do 6 miejsc po przecinku
        Assert.Equal("0.12345678", Pole(doc, "P_9A"));  // cena - do 8 miejsc po przecinku
    }

    [Theory]
    [InlineData(VatRate.Rate4)]
    [InlineData(VatRate.Rate3)]
    public void StawkiRyczaltuDlaTaksowek_SaOdrzucane(VatRate rate)
    {
        var ex = Assert.Throws<Fa3GenerationException>(() => _generator.GenerateBytes(Faktura(Pozycja(1, "Kurs", 1, 50m, rate))));
        Assert.Contains("taksówek", ex.Message);
    }

    [Fact]
    public void WalutaObca_IZwolnienieBezPodstawy_SaOdrzucane()
    {
        var eur = Faktura(Pozycja(1, "Usługa", 1, 100m, VatRate.Rate23));
        eur.Currency = "EUR";
        Assert.Throws<Fa3GenerationException>(() => _generator.GenerateBytes(eur));

        var zw = Faktura(Pozycja(1, "Usługa medyczna", 1, 100m, VatRate.Exempt));
        Assert.Throws<Fa3GenerationException>(() => _generator.GenerateBytes(zw));
    }
}

public class Fa3SchemaValidatorTests
{
    private readonly Fa3SchemaValidator _validator = new();

    /// <summary>Kontrola poprawności samego walidatora: przykładowa faktura z repozytorium MF jest akceptowana.</summary>
    [Fact]
    public void PrzykladowaFakturaMinisterstwa_JestPoprawna()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "external", "ksef-client-csharp", "KSeF.Client.Tests.Core", "Templates", "invoice-template-fa-3.xml");
        var xml = File.ReadAllText(path).Replace("#nip#", "5260250274").Replace("#invoice_number#", "1");

        var result = _validator.Validate(new UTF8Encoding(false).GetBytes(xml));

        Assert.True(result.IsValid, string.Join("\n", result.Errors));
    }

    [Fact]
    public void BrakWymaganegoPola_JestWykrywany()
    {
        var xml = new Fa3XmlGenerator().Generate(new Invoice
        {
            Number = "FV/1", IssueDate = new DateOnly(2026, 11, 1), Currency = "PLN",
            Seller = new InvoiceParty { Name = "S", Nip = "5260250274", Address = new Address { Street = "a", PostalCode = "00-001", City = "W" } },
            Buyer = new InvoiceParty { Name = "B", Address = new Address { Street = "b", PostalCode = "00-001", City = "W" } },
            Items = [new InvoiceItem { Position = 1, Name = "X", Quantity = 1, UnitNetPrice = 1, VatRate = VatRate.Rate23 }]
        });
        var broken = System.Text.RegularExpressions.Regex.Replace(xml, @"\s*<P_2>.*?</P_2>", "");

        var result = _validator.Validate(Encoding.UTF8.GetBytes(broken));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("P_2") || e.Contains("P_6") || e.Contains("P_13_1"));
    }

    [Fact]
    public void ZnacznikBom_JestWykrywany()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("<a/>")).ToArray();

        var result = _validator.Validate(bytes);

        Assert.Contains(result.Errors, e => e.Contains("BOM"));
    }

    [Fact]
    public void DeklaracjaDtd_JestOdrzucana_OchronaPrzedXxe()
    {
        var xml = "<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><x>&e;</x>";

        var result = _validator.Validate(Encoding.UTF8.GetBytes(xml));

        Assert.False(result.IsValid);
    }
}

public class InvoiceXmlApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task XmlFaktury_JestZwracanyJakoPlik_IZgodnyZeSchematem()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        var invoice = await (await client.PostAsJsonAsync("/api/invoices", TestUsers.SampleInvoice()))
            .Content.ReadFromJsonAsync<InvoiceDto>();

        var response = await client.GetAsync($"/api/invoices/{invoice!.Id}/xml");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("FV_1_11_2026.xml", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(new Fa3SchemaValidator().Validate(bytes).IsValid);
    }

    [Fact]
    public async Task FakturaZwolnionaBezPodstawy_IWalutaObca_SaOdrzucanePrzyZapisie()
    {
        var client = await factory.CreateClientWithCompanyAsync();

        var zw = TestUsers.SampleInvoice();
        zw.Items[0].VatRate = VatRate.Exempt;
        var response = await client.PostAsJsonAsync("/api/invoices", zw);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("podstawę prawną zwolnienia", await response.Content.ReadAsStringAsync());

        zw.ExemptionBasis = "art. 113 ust. 1 ustawy o VAT";
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/invoices", zw)).StatusCode);

        var eur = TestUsers.SampleInvoice();
        eur.Currency = "EUR";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/invoices", eur)).StatusCode);

        var taxi = TestUsers.SampleInvoice();
        taxi.Items[0].VatRate = VatRate.Rate4;
        var taxiResponse = await client.PostAsJsonAsync("/api/invoices", taxi);
        Assert.Equal(HttpStatusCode.BadRequest, taxiResponse.StatusCode);
        Assert.Contains("taksówek", await taxiResponse.Content.ReadAsStringAsync());
    }
}
