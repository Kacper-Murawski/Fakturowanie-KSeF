using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Services;
using FakturowanieKSeF.Contracts.Calculation;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Ksef.Fa3;
using FakturowanieKSeF.Models;
using FakturowanieKSeF.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FakturowanieKSeF.Tests;

/// <summary>Format CSV: zgodność z polskim Excelem i ochrona przed wstrzyknięciem formuł.</summary>
public class CsvFormatTests
{
    [Theory]
    [InlineData("Kontrahent S.A.", "Kontrahent S.A.")]
    [InlineData("=HYPERLINK(\"http://zly.example\")", "\"'=HYPERLINK(\"\"http://zly.example\"\")\"")]
    [InlineData("+48 600", "'+48 600")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("-cmd", "'-cmd")]
    [InlineData("-246,00", "-246,00")] // kwota ujemna (korekta) nie jest formułą
    [InlineData("Firma; oddział", "\"Firma; oddział\"")]
    [InlineData(null, "")]
    public void Escape_ZabezpieczaFormulyISeparator(string? value, string expected) =>
        Assert.Equal(expected, AccountingExport.CsvBuilder.Escape(value));

    [Fact]
    public void Csv_MaZnacznikBomIKonceLiniiWindows()
    {
        var csv = new AccountingExport.CsvBuilder();
        csv.Row(["a", "b"]);
        var bytes = csv.ToBytes();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal("a;b\r\n", Encoding.UTF8.GetString(bytes[3..]));
    }

    [Theory]
    [InlineData("2026-11-01", "2026-11-30", null)]
    [InlineData("2026-11-30", "2026-11-01", "Data końcowa")]
    [InlineData("2026-01-01", "2027-01-02", "366")]
    public void Okres_JestSprawdzany(string from, string to, string? error)
    {
        var result = AccountingExport.Validate(DateOnly.Parse(from), DateOnly.Parse(to));
        if (error is null) Assert.Null(result); else Assert.Contains(error, result);
    }

    [Fact]
    public void NazwaPliku_MiesiacAlboZakres()
    {
        Assert.Equal("2026-11", AccountingExport.FileSuffix(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)));
        Assert.Equal("2026-11-01_2026-11-15", AccountingExport.FileSuffix(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 15)));
    }
}

/// <summary>Eksport dla księgowej: zakres danych, treść zestawień i paczki ZIP, izolacja danych.</summary>
public class ExportApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Period = "from=2026-11-01&to=2026-11-30";

    /// <summary>Firma z fakturą przyjętą w KSeF (1 897,50 zł), szkicem i fakturą otrzymaną.</summary>
    private async Task<(HttpClient Client, InvoiceDto Accepted, InvoiceDto Draft, ReceivedInvoice Received)> SeedAsync()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        (await client.PostAsync("/api/company/ksef/test-token", null)).EnsureSuccessStatusCode();

        var draft = (await (await client.PostAsJsonAsync("/api/invoices", TestUsers.SampleInvoice())).Content.ReadFromJsonAsync<InvoiceDto>())!;
        (await client.PostAsJsonAsync($"/api/invoices/{draft.Id}/send", new SendInvoiceRequest { Version = draft.Version })).EnsureSuccessStatusCode();
        for (var i = 0; i < 5 && await factory.ProcessKsefAsync() > 0; i++) { }
        var accepted = (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{draft.Id}"))!;
        Assert.Equal(InvoiceStatus.Accepted, accepted.Status);

        var stillDraft = (await (await client.PostAsJsonAsync("/api/invoices", TestUsers.SampleInvoice())).Content.ReadFromJsonAsync<InvoiceDto>())!;

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var companyId = (await db.Invoices.SingleAsync(i => i.Id == accepted.Id)).CompanyId;
        var received = await AddReceivedAsync(db, companyId, "=Dostawca złośliwy sp. z o.o.");
        return (client, accepted, stillDraft, received);
    }

    private static async Task<ReceivedInvoice> AddReceivedAsync(AppDbContext db, int companyId, string sellerName)
    {
        var line = InvoiceCalculator.CalculateLine(new LineInput(2, 100m, VatRate.Rate8));
        var address = new Address { Street = "ul. Testowa 1", PostalCode = "00-001", City = "Warszawa", CountryCode = "PL" };
        var xml = new Fa3XmlGenerator().Generate(new Invoice
        {
            Number = "ZAK/7/2026", IssueDate = new DateOnly(2026, 11, 3), Currency = "PLN", PaymentMethod = PaymentMethod.Transfer,
            DueDate = new DateOnly(2026, 11, 20), BankAccountNumber = "61109010140000071219812874",
            Seller = new InvoiceParty { Name = sellerName, Nip = "1234563218", Address = address },
            Buyer = new InvoiceParty { Name = "Firma użytkownika", Nip = "5260250274", Address = address },
            Items = [new InvoiceItem { Position = 1, Name = "Materiały", Quantity = 2, UnitNetPrice = 100m, VatRate = VatRate.Rate8,
                NetAmount = line.NetAmount, VatAmount = line.VatAmount, GrossAmount = line.GrossAmount }]
        });
        var received = new ReceivedInvoice
        {
            CompanyId = companyId, KsefNumber = $"1234563218-20261103-{Random.Shared.NextInt64(0, 0xFFFFFFFFFFFF):X12}-00",
            InvoiceNumber = "ZAK/7/2026", SellerNip = "1234563218", SellerName = sellerName, IssueDate = new DateOnly(2026, 11, 3),
            Currency = "PLN", TotalNet = 200m, TotalVat = 16m, TotalGross = 216m,
            KsefAcceptedAt = new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc), Xml = xml, DownloadedAt = DateTime.UtcNow
        };
        db.ReceivedInvoices.Add(received);
        await db.SaveChangesAsync();
        return received;
    }

    [Fact]
    public async Task Podsumowanie_ObejmujeTylkoFakturyPrzyjeteWKsef_IOtrzymane()
    {
        var (client, _, _, _) = await SeedAsync();

        var summary = (await client.GetFromJsonAsync<ExportSummaryDto>($"/api/exports/summary?{Period}"))!;

        Assert.Equal(1, summary.SalesCount); // szkic pominięty
        Assert.Equal(1550m, summary.SalesNet);
        Assert.Equal(347.50m, summary.SalesVat);
        Assert.Equal(1897.50m, summary.SalesGross);
        Assert.Equal(1, summary.PurchasesCount);
        Assert.Equal(216m, summary.PurchasesGross);

        // Inny miesiąc - pusto
        var october = (await client.GetFromJsonAsync<ExportSummaryDto>("/api/exports/summary?from=2026-10-01&to=2026-10-31"))!;
        Assert.Equal(0, october.SalesCount + october.PurchasesCount);
    }

    [Fact]
    public async Task ZestawieniaCsv_ZawierajaKwotyPerStawka_IChroniaPrzedFormulami()
    {
        var (client, accepted, draft, _) = await SeedAsync();

        var response = await client.GetAsync($"/api/exports/sales.csv?{Period}");
        Assert.Equal("sprzedaz_2026-11.csv", response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName);
        var sales = Encoding.UTF8.GetString((await response.Content.ReadAsByteArrayAsync())[3..]).Split("\r\n");
        Assert.StartsWith("Rodzaj;Numer faktury;Data wystawienia", sales[0]);
        Assert.Contains("Netto 23%;VAT 23%;Netto 8%;VAT 8%;Netto 5%;VAT 5%", sales[0]);
        var row = Assert.Single(sales.Skip(1), l => l.Length > 0);
        Assert.StartsWith($"VAT;{accepted.Number};2026-11-01;", row);
        Assert.Contains(accepted.KsefNumber!, row);
        Assert.Contains("1500,00;345,00;;;50,00;2,50", row);
        Assert.Contains(";1550,00;347,50;1897,50;PLN;Przelew;2026-11-15;", row);
        Assert.DoesNotContain(sales, l => l.Contains(draft.Number));

        var purchases = Encoding.UTF8.GetString((await client.GetByteArrayAsync($"/api/exports/purchases.csv?{Period}"))[3..]).Split("\r\n");
        Assert.Contains("'=Dostawca złośliwy sp. z o.o.;1234563218", purchases[1]); // formuła zneutralizowana
        Assert.Contains(";;200,00;16,00;", purchases[1]); // kolumny Netto 8% i VAT 8%
        Assert.Contains("nie sprawdzano", purchases[1]);
    }

    [Fact]
    public async Task PaczkaZip_ZawieraZestawieniaXmlIUpo()
    {
        var (client, accepted, _, received) = await SeedAsync();

        var bytes = await client.GetByteArrayAsync($"/api/exports/package.zip?{Period}");
        using var zip = new ZipArchive(new MemoryStream(bytes));
        var names = zip.Entries.Select(e => e.FullName).ToList();

        var safe = accepted.Number.Replace('/', '_');
        Assert.Contains("sprzedaz.csv", names);
        Assert.Contains("zakupy.csv", names);
        Assert.Contains("README.txt", names);
        Assert.Contains($"sprzedaz/{safe}.xml", names);
        Assert.Contains($"sprzedaz/{safe}_UPO.xml", names);
        Assert.Contains($"zakupy/{received.KsefNumber}.xml", names);
        Assert.Equal(6, names.Count);

        // XML w paczce to dokładnie dokument wysłany do KSeF
        using var reader = new StreamReader(zip.GetEntry($"sprzedaz/{safe}.xml")!.Open());
        Assert.Equal(await client.GetStringAsync($"/api/invoices/{accepted.Id}/xml"), await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Walidacja_ZakresDatIBrakParametrow()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/exports/sales.csv?from=2026-11-30&to=2026-11-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/exports/package.zip?from=2025-01-01&to=2026-11-30")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/exports/summary")).StatusCode);

        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/exports/package.zip?{Period}")).StatusCode);
    }

    [Fact]
    public async Task UzytkownikB_NieEksportujeDanychUzytkownikaA()
    {
        var (_, accepted, _, received) = await SeedAsync();
        var clientB = await factory.CreateClientWithCompanyAsync();

        var summary = (await clientB.GetFromJsonAsync<ExportSummaryDto>($"/api/exports/summary?{Period}"))!;
        Assert.Equal(0, summary.SalesCount + summary.PurchasesCount);
        Assert.DoesNotContain(accepted.Number, await clientB.GetStringAsync($"/api/exports/sales.csv?{Period}"));
        Assert.DoesNotContain(received.KsefNumber, await clientB.GetStringAsync($"/api/exports/purchases.csv?{Period}"));

        using var zip = new ZipArchive(new MemoryStream(await clientB.GetByteArrayAsync($"/api/exports/package.zip?{Period}")));
        Assert.Equal(3, zip.Entries.Count); // tylko puste zestawienia i README
    }
}
