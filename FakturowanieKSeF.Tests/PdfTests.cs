using System.Net;
using System.Net.Http.Json;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Ksef;
using FakturowanieKSeF.Tests.Infrastructure;
using KSeF.Client.Core.Interfaces.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig;

namespace FakturowanieKSeF.Tests;

public class KsefQrTests
{
    /// <summary>Link KOD I jest identyczny z linkiem budowanym przez oficjalny klient MF dla środowiska testowego.</summary>
    [Fact]
    public void LinkWeryfikacyjny_JestZgodnyZImplementacjaMinisterstwa()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKsefTestClient();
        using var provider = services.BuildServiceProvider();
        var mf = provider.GetRequiredService<IVerificationLinkService>();
        var hash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData("faktura"u8.ToArray()));

        var ours = KsefQr.InvoiceVerificationUrl("5260250274", new DateOnly(2026, 2, 1), hash);

        Assert.Equal(mf.BuildInvoiceVerificationUrl("5260250274", new DateTime(2026, 2, 1), hash), ours);
        Assert.StartsWith("https://qr-test.ksef.mf.gov.pl/invoice/5260250274/01-02-2026/", ours);
        Assert.DoesNotContain("+", ours);
        Assert.DoesNotContain("=", ours);
    }

    [Fact]
    public void KodQr_JestObrazemPng() =>
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, KsefQr.GeneratePng("https://qr-test.ksef.mf.gov.pl/invoice/x")[..4]);
}

public class InvoicePdfTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string Text(byte[] pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        return string.Join("\n", doc.GetPages().Select(p => string.Join(" ", p.GetWords().Select(w => w.Text))));
    }

    [Fact]
    public async Task PdfSzkicu_ZawieraTresc_PolskieZnaki_IOznaczenieSzkicu_BezKoduQr()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        var request = TestUsers.SampleInvoice();
        request.Items[0].Name = "Usługa wdrożeniowa - zażółć gęślą jaźń";
        var invoice = await (await client.PostAsJsonAsync("/api/invoices", request)).Content.ReadFromJsonAsync<InvoiceDto>();

        var response = await client.GetAsync($"/api/invoices/{invoice!.Id}/pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var text = Text(await response.Content.ReadAsByteArrayAsync());
        Assert.Contains("FV/1/11/2026", text);
        Assert.Contains("zażółć gęślą jaźń", text);
        Assert.Contains("SZKIC", text);
        Assert.Contains("1 897,50", text.Replace(' ', ' ')); // kwota brutto (polski format)
        Assert.Contains("526-025-02-74", text);
        Assert.Contains("brak kodu weryfikacji", text);
    }

    [Fact]
    public async Task PdfFakturyPrzyjetejWKsef_MaKodQrZNumeremKsef()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        (await client.PostAsync("/api/company/ksef/test-token", null)).EnsureSuccessStatusCode();
        var invoice = await (await client.PostAsJsonAsync("/api/invoices", TestUsers.SampleInvoice())).Content.ReadFromJsonAsync<InvoiceDto>();
        (await client.PostAsJsonAsync($"/api/invoices/{invoice!.Id}/send", new SendInvoiceRequest { Version = invoice.Version })).EnsureSuccessStatusCode();

        // Przed nadaniem numeru KSeF - pod kodem QR „OFFLINE”
        var pending = Text(await client.GetByteArrayAsync($"/api/invoices/{invoice.Id}/pdf"));
        Assert.Contains("OFFLINE", pending);

        for (var i = 0; i < 5 && await factory.ProcessKsefAsync() > 0; i++) { }
        var accepted = (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{invoice.Id}"))!;

        var text = Text(await client.GetByteArrayAsync($"/api/invoices/{invoice.Id}/pdf"));
        Assert.Contains(accepted.KsefNumber!, text);
        Assert.Contains("https://qr-test.ksef.mf.gov.pl/invoice/5260250274/01-11-2026/", text);
        Assert.DoesNotContain("SZKIC", text);

        // Link w PDF zawiera skrót dokładnie wysłanego dokumentu
        using var scope = factory.Services.CreateScope();
        var hash = await scope.ServiceProvider.GetRequiredService<AppDbContext>().KsefSubmissions
            .Where(s => s.InvoiceId == invoice.Id).Select(s => s.InvoiceHash).SingleAsync();
        Assert.Contains(KsefQr.InvoiceVerificationUrl("5260250274", new DateOnly(2026, 11, 1), hash), text.Replace(" ", ""));
    }

    [Fact]
    public async Task PdfFakturyOtrzymanej_ZawieraSprzedawceIKodQr()
    {
        var nip = TestUsers.RandomNip();
        var client = await factory.CreateClientWithCompanyAsync(nip);
        (await client.PostAsync("/api/company/ksef/test-token", null)).EnsureSuccessStatusCode();
        var meta = new KsefInvoiceMetadata($"1234563218-20261030-ABCDEF{nip[..4]}-12", "FV/99/10/2026", new DateOnly(2026, 10, 30),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(-1), "1234563218", "Hurtownia Dostawca sp. z o.o.", 115m, 19.25m, 134.25m, "PLN",
            Convert.ToBase64String(new byte[32]));
        lock (factory.Ksef.Received) factory.Ksef.Received[nip] = [(meta, ReceivedInvoicesTests.ForeignInvoiceXml("1234563218", nip, "FV/99/10/2026", 2))];
        factory.Ksef.Hwm = DateTimeOffset.UtcNow;
        (await client.PostAsync("/api/received-invoices/sync", null)).EnsureSuccessStatusCode();
        var id = (await client.GetFromJsonAsync<PagedResult<ReceivedInvoiceListItemDto>>("/api/received-invoices"))!.Items.Single().Id;

        var text = Text(await client.GetByteArrayAsync($"/api/received-invoices/{id}/pdf"));

        Assert.Contains("Hurtownia Dostawca sp. z o.o.", text);
        Assert.Contains(meta.KsefNumber, text);
        Assert.Contains("https://qr-test.ksef.mf.gov.pl/invoice/1234563218/30-10-2026/", text);
    }

    [Fact]
    public async Task PdfCudzejFaktury_Zwraca404()
    {
        var owner = await factory.CreateClientWithCompanyAsync();
        var invoice = await (await owner.PostAsJsonAsync("/api/invoices", TestUsers.SampleInvoice())).Content.ReadFromJsonAsync<InvoiceDto>();
        var other = await factory.CreateClientWithCompanyAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/invoices/{invoice!.Id}/pdf")).StatusCode);
    }
}
