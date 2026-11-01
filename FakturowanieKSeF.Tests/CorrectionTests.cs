using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FakturowanieKSeF.Contracts.Calculation;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Ksef.Fa3;
using FakturowanieKSeF.Models;
using FakturowanieKSeF.Tests.Infrastructure;
using UglyToad.PdfPig;

namespace FakturowanieKSeF.Tests;

/// <summary>Kwoty faktury korygującej metodą „przed i po”.</summary>
public class CorrectionCalculatorTests
{
    [Fact]
    public void Roznica_PerStawka_OrazStawkiZJednegoStanu()
    {
        var before = new[] { new LineInput(10, 150m, VatRate.Rate23), new LineInput(1, 50m, VatRate.Rate5) };
        var after = new[] { new LineInput(10, 120m, VatRate.Rate23), new LineInput(2, 100m, VatRate.Rate8) };

        var c = InvoiceCalculator.CalculateCorrection(before, after);

        Assert.Equal(1897.50m, c.Before.TotalGross);
        Assert.Equal(1692.00m, c.After.TotalGross); // 1200 + 276 + 200 + 16
        Assert.Equal(-205.50m, c.TotalGross);
        Assert.Collection(c.Difference,
            s => { Assert.Equal(VatRate.Rate23, s.VatRate); Assert.Equal(-300m, s.Net); Assert.Equal(-69m, s.Vat); },
            s => { Assert.Equal(VatRate.Rate8, s.VatRate); Assert.Equal(200m, s.Net); Assert.Equal(16m, s.Vat); },
            s => { Assert.Equal(VatRate.Rate5, s.VatRate); Assert.Equal(-50m, s.Net); Assert.Equal(-2.50m, s.Vat); });
    }

    [Fact]
    public void KorektaDoZera_RoznicaRownaMinusStanPrzed()
    {
        var c = InvoiceCalculator.CalculateCorrection([new LineInput(3, 33.33m, VatRate.Rate23)], []);
        Assert.Equal(-c.Before.TotalGross, c.TotalGross);
        Assert.Equal(-99.99m, c.TotalNet);
    }
}

/// <summary>XML faktury korygującej (RodzajFaktury = KOR) - zgodność z oficjalnym schematem FA(3).</summary>
public class CorrectionXmlTests
{
    private static readonly XNamespace Ns = Fa3XmlGenerator.Namespace;
    private readonly Fa3XmlGenerator _generator = new();
    private readonly Fa3SchemaValidator _validator = new();

    private static InvoiceItem Item(int position, string name, decimal qty, decimal price, VatRate rate, bool before)
    {
        var line = InvoiceCalculator.CalculateLine(new(qty, price, rate));
        return new InvoiceItem
        {
            Position = position, Name = name, Unit = "szt.", Quantity = qty, UnitNetPrice = price, VatRate = rate, BeforeCorrection = before,
            NetAmount = line.NetAmount, VatAmount = line.VatAmount, GrossAmount = line.GrossAmount
        };
    }

    private static Invoice Correction(params InvoiceItem[] items) => new()
    {
        Kind = InvoiceKind.Correction,
        Number = "KOR/1/11/2026",
        IssueDate = new DateOnly(2026, 11, 5),
        Currency = "PLN",
        PaymentMethod = PaymentMethod.Transfer,
        DueDate = new DateOnly(2026, 11, 19),
        BankAccountNumber = "61109010140000071219812874",
        CorrectedInvoiceNumber = "FV/1/11/2026",
        CorrectedInvoiceIssueDate = new DateOnly(2026, 11, 1),
        CorrectedInvoiceKsefNumber = "5260250274-20261101-0123456789AB-CD",
        CorrectionReason = "Rabat udzielony po wystawieniu faktury",
        CorrectionType = CorrectionType.CorrectionDate,
        Seller = new InvoiceParty { Name = "Pracownia", Nip = "5260250274", Address = new Address { Street = "ul. Kwiatowa 1", PostalCode = "00-001", City = "Warszawa", CountryCode = "PL" } },
        Buyer = new InvoiceParty { Name = "Kontrahent S.A.", Nip = "1234563218", Address = new Address { Street = "ul. Polna 5", PostalCode = "30-001", City = "Kraków", CountryCode = "PL" } },
        Items = items.ToList()
    };

    private XDocument Valid(Invoice invoice)
    {
        var bytes = _generator.GenerateBytes(invoice);
        var result = _validator.Validate(bytes);
        Assert.True(result.IsValid, "XML korekty niezgodny ze schematem FA(3):\n" + string.Join("\n", result.Errors));
        return XDocument.Parse(Encoding.UTF8.GetString(bytes));
    }

    private static string? Field(XContainer doc, string name) => doc.Descendants(Ns + name).FirstOrDefault()?.Value;

    [Fact]
    public void ObnizkaCeny_KwotyRoznicy_WierszeStanPrzed_IDaneFakturyKorygowanej()
    {
        var doc = Valid(Correction(
            Item(1, "Usługa", 10, 150m, VatRate.Rate23, before: true),
            Item(2, "Książka", 1, 50m, VatRate.Rate5, before: true),
            Item(3, "Usługa", 10, 120m, VatRate.Rate23, before: false),
            Item(4, "Książka", 1, 50m, VatRate.Rate5, before: false)));

        Assert.Equal("KOR", Field(doc, "RodzajFaktury"));
        Assert.Equal("-300.00", Field(doc, "P_13_1"));
        Assert.Equal("-69.00", Field(doc, "P_14_1"));
        Assert.Equal("0.00", Field(doc, "P_13_3"));  // stawka bez zmian - różnica zerowa
        Assert.Equal("-369.00", Field(doc, "P_15"));
        Assert.Equal("Rabat udzielony po wystawieniu faktury", Field(doc, "PrzyczynaKorekty"));
        Assert.Equal("2", Field(doc, "TypKorekty"));
        Assert.Equal("FV/1/11/2026", Field(doc, "NrFaKorygowanej"));
        Assert.Equal("2026-11-01", Field(doc, "DataWystFaKorygowanej"));
        Assert.Equal("5260250274-20261101-0123456789AB-CD", Field(doc, "NrKSeFFaKorygowanej"));

        var rows = doc.Descendants(Ns + "FaWiersz").ToList();
        Assert.Equal(["1", "2", "3", "4"], rows.Select(r => r.Element(Ns + "NrWierszaFa")!.Value));
        Assert.Equal(2, rows.Count(r => r.Element(Ns + "StanPrzed")?.Value == "1"));
        // Zwrot dla nabywcy - korekta bez terminu płatności
        Assert.Null(doc.Descendants(Ns + "Platnosc").FirstOrDefault());
    }

    [Fact]
    public void KorektaDoZera_TylkoWierszePrzed()
    {
        var doc = Valid(Correction(Item(1, "Usługa", 1, 1000m, VatRate.Rate23, before: true)));
        Assert.Equal("-1230.00", Field(doc, "P_15"));
        Assert.Single(doc.Descendants(Ns + "FaWiersz"));
    }

    [Fact]
    public void Doplata_ZPlatnoscia_IReadRozpoznajeKorekte()
    {
        var invoice = Correction(
            Item(1, "Usługa", 1, 100m, VatRate.Rate23, before: true),
            Item(2, "Usługa", 2, 100m, VatRate.Rate23, before: false));
        invoice.CorrectionType = null;
        var doc = Valid(invoice);
        Assert.Equal("123.00", Field(doc, "P_15"));
        Assert.Null(Field(doc, "TypKorekty"));
        Assert.Equal("2026-11-19", Field(doc, "Termin"));

        var read = Fa3InvoiceReader.Read(_generator.Generate(invoice));
        Assert.True(read.IsCorrection);
        Assert.Equal("KOR", read.InvoiceType);
        Assert.Equal("Rabat udzielony po wystawieniu faktury", read.CorrectionReason);
        Assert.Equal("FV/1/11/2026", read.CorrectedInvoices!.Single().Number);
        Assert.Equal([true, false], read.Lines.Select(l => l.BeforeCorrection));
        Assert.Equal(123m, read.TotalGross);
    }
}

/// <summary>Faktury korygujące w API: tworzenie, reguły, wysyłka, rozliczenie, PDF, izolacja.</summary>
public class CorrectionApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task ProcessAsync()
    {
        for (var i = 0; i < 5 && await factory.ProcessKsefAsync() > 0; i++) { }
    }

    /// <summary>Faktura VAT przyjęta w KSeF: 10 × 150 zł (23%) + 1 × 50 zł (5%) = 1 897,50 zł brutto.</summary>
    private async Task<(HttpClient Client, InvoiceDto Invoice)> AcceptedInvoiceAsync()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        (await client.PostAsync("/api/company/ksef/test-token", null)).EnsureSuccessStatusCode();
        var draft = (await (await client.PostAsJsonAsync("/api/invoices", TestUsers.SampleInvoice())).Content.ReadFromJsonAsync<InvoiceDto>())!;
        (await client.PostAsJsonAsync($"/api/invoices/{draft.Id}/send", new SendInvoiceRequest { Version = draft.Version })).EnsureSuccessStatusCode();
        await ProcessAsync();
        return (client, (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{draft.Id}"))!);
    }

    /// <summary>Korekta: obniżka ceny usługi ze 150 do 120 zł (różnica -369 zł brutto).</summary>
    private static SaveInvoiceRequest PriceDecrease(InvoiceDto original, decimal newPrice = 120m) => new()
    {
        CorrectedInvoiceId = original.Id,
        CorrectionReason = "Rabat",
        CorrectionType = CorrectionType.CorrectionDate,
        IssueDate = original.IssueDate,
        PaymentMethod = PaymentMethod.Transfer,
        Items = original.Items.Select(i => new SaveInvoiceItemRequest
        {
            Name = i.Name, Unit = i.Unit, Quantity = i.Quantity, VatRate = i.VatRate,
            UnitNetPrice = i.Name == "Usługa programistyczna" ? newPrice : i.UnitNetPrice
        }).ToList()
    };

    private static async Task<string> Error(HttpResponseMessage response) => Regex.Unescape(await response.Content.ReadAsStringAsync());

    [Fact]
    public async Task Korekta_Utworzenie_Wysylka_IPowiazanieZFakturaKorygowana()
    {
        var (client, original) = await AcceptedInvoiceAsync();

        var response = await client.PostAsJsonAsync("/api/invoices", PriceDecrease(original));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var correction = (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;

        Assert.Equal(InvoiceKind.Correction, correction.Kind);
        Assert.StartsWith("KOR/1/", correction.Number);
        Assert.Equal(-369m, correction.TotalGross);
        Assert.Equal(original.Buyer.Name, correction.Buyer.Name);
        Assert.Equal(original.Number, correction.Correction!.CorrectedInvoiceNumber);
        Assert.Equal(original.KsefNumber, correction.Correction.CorrectedInvoiceKsefNumber);
        Assert.Equal(2, correction.Correction.ItemsBefore.Count);
        Assert.Equal(120m, correction.Items[0].UnitNetPrice);
        Assert.Null(correction.Payment); // korekta nie przyjmuje wpłat - zmienia należność faktury korygowanej

        // XML korekty zgodny z XSD (endpoint waliduje przed wydaniem)
        var xmlResponse = await client.GetAsync($"/api/invoices/{correction.Id}/xml");
        Assert.True(xmlResponse.IsSuccessStatusCode, await Error(xmlResponse));
        var xml = await xmlResponse.Content.ReadAsStringAsync();
        Assert.Contains("<RodzajFaktury>KOR</RodzajFaktury>", xml);
        Assert.Contains($"<NrKSeFFaKorygowanej>{original.KsefNumber}</NrKSeFFaKorygowanej>", xml);

        // Wysyłka do KSeF jak zwykłej faktury
        (await client.PostAsJsonAsync($"/api/invoices/{correction.Id}/send", new SendInvoiceRequest { Version = correction.Version })).EnsureSuccessStatusCode();
        await ProcessAsync();
        var accepted = (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{correction.Id}"))!;
        Assert.Equal(InvoiceStatus.Accepted, accepted.Status);

        // Faktura korygowana: lista korekt i należność pomniejszona o korektę
        var updated = (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{original.Id}"))!;
        Assert.Equal(correction.Id, updated.Corrections!.Single().Id);
        Assert.Equal(1897.50m - 369m, updated.Payment!.TotalDue);
        Assert.Equal(1528.50m, updated.Payment.Remaining);

        // Lista faktur oznacza korektę
        var list = (await client.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices"))!;
        Assert.Equal(InvoiceKind.Correction, list.Items.Single(i => i.Id == correction.Id).Kind);
        Assert.Null(list.Items.Single(i => i.Id == correction.Id).PaymentStatus);
    }

    [Fact]
    public async Task KolejnaKorekta_StanPrzedToStanPoPoprzedniej_INadplataPoKorekcie()
    {
        var (client, original) = await AcceptedInvoiceAsync();
        // Faktura opłacona w całości przed korektą
        (await client.PostAsJsonAsync($"/api/invoices/{original.Id}/payments",
            new AddPaymentRequest { PaidOn = original.IssueDate, Amount = 1897.50m })).EnsureSuccessStatusCode();

        var first = (await (await client.PostAsJsonAsync("/api/invoices", PriceDecrease(original, 120m))).Content.ReadFromJsonAsync<InvoiceDto>())!;
        (await client.PostAsJsonAsync($"/api/invoices/{first.Id}/send", new SendInvoiceRequest { Version = first.Version })).EnsureSuccessStatusCode();

        // Druga korekta, gdy pierwsza czeka w KSeF - odmowa (stan przed korektą nie jest jeszcze ustalony)
        var blocked = await client.PostAsJsonAsync("/api/invoices", PriceDecrease(original, 100m));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        await ProcessAsync();

        var second = (await (await client.PostAsJsonAsync("/api/invoices", PriceDecrease(original, 100m))).Content.ReadFromJsonAsync<InvoiceDto>())!;
        Assert.StartsWith("KOR/2/", second.Number);
        Assert.Equal(120m, second.Correction!.ItemsBefore[0].UnitNetPrice); // stan po pierwszej korekcie
        Assert.Equal(-246m, second.TotalGross);                            // 10 × 20 zł netto + 23%

        // Należność po pierwszej korekcie: nadpłata 369 zł do zwrotu
        var payment = (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{original.Id}"))!.Payment!;
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal(369m, payment.Overpaid);
    }

    [Fact]
    public async Task Reguly_Przyczyna_BezZmian_SzkicKorekta_KorektaKorekty_IWplataNaKorekte()
    {
        var (client, original) = await AcceptedInvoiceAsync();

        var noReason = PriceDecrease(original);
        noReason.CorrectionReason = " ";
        Assert.Contains("Podaj przyczynę korekty.", await Error(await client.PostAsJsonAsync("/api/invoices", noReason)));

        var unchanged = PriceDecrease(original, 150m);
        var same = await client.PostAsJsonAsync("/api/invoices", unchanged);
        Assert.Equal(HttpStatusCode.BadRequest, same.StatusCode);
        Assert.Contains("takie same jak przed korektą", await Error(same));

        var draft = (await (await client.PostAsJsonAsync("/api/invoices", TestUsers.SampleInvoice())).Content.ReadFromJsonAsync<InvoiceDto>())!;
        var ofDraft = await client.PostAsJsonAsync("/api/invoices", PriceDecrease(draft));
        Assert.Contains("wyłącznie fakturę przyjętą w KSeF", await Error(ofDraft));

        var correction = (await (await client.PostAsJsonAsync("/api/invoices", PriceDecrease(original))).Content.ReadFromJsonAsync<InvoiceDto>())!;
        var request = PriceDecrease(original);
        request.CorrectedInvoiceId = correction.Id;
        Assert.Contains("do faktury pierwotnej", await Error(await client.PostAsJsonAsync("/api/invoices", request)));

        var payment = await client.PostAsJsonAsync($"/api/invoices/{correction.Id}/payments",
            new AddPaymentRequest { PaidOn = original.IssueDate, Amount = 1m });
        Assert.Equal(HttpStatusCode.Conflict, payment.StatusCode);

        // Korekta do zera (wszystkie pozycje usunięte)
        var zero = PriceDecrease(original);
        zero.Items = [];
        var zeroCorrection = (await (await client.PostAsJsonAsync("/api/invoices", zero)).Content.ReadFromJsonAsync<InvoiceDto>())!;
        Assert.Equal(-1897.50m, zeroCorrection.TotalGross);
        Assert.Empty(zeroCorrection.Items);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/invoices/{zeroCorrection.Id}/xml")).StatusCode);

        // Zwykła faktura nadal wymaga pozycji
        var empty = TestUsers.SampleInvoice();
        empty.Items = [];
        Assert.Contains("co najmniej jedną pozycję", await Error(await client.PostAsJsonAsync("/api/invoices", empty)));
    }

    [Fact]
    public async Task PdfKorekty_StanPrzedIPo_KwotaDoZwrotu()
    {
        var (client, original) = await AcceptedInvoiceAsync();
        var correction = (await (await client.PostAsJsonAsync("/api/invoices", PriceDecrease(original))).Content.ReadFromJsonAsync<InvoiceDto>())!;

        var pdf = await client.GetByteArrayAsync($"/api/invoices/{correction.Id}/pdf");
        using var doc = PdfDocument.Open(pdf);
        var text = string.Join("\n", doc.GetPages().Select(p => string.Join(" ", p.GetWords().Select(w => w.Text))));

        Assert.Contains("Faktura korygująca", text);
        Assert.Contains($"Korekta faktury nr {original.Number}", text);
        Assert.Contains("Przyczyna korekty: Rabat", text);
        Assert.Contains("Stan przed korektą", text);
        Assert.Contains("Stan po korekcie", text);
        Assert.Contains("Do zwrotu:", text);
        Assert.Contains("369,00", text);
    }

    [Fact]
    public async Task UzytkownikB_NieKorygujeFakturyUzytkownikaA()
    {
        var (_, originalA) = await AcceptedInvoiceAsync();
        var clientB = await factory.CreateClientWithCompanyAsync();

        var response = await clientB.PostAsJsonAsync("/api/invoices", PriceDecrease(originalA));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Nie znaleziono faktury korygowanej.", await Error(response));
    }
}
