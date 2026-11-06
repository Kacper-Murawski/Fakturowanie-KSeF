using System.Net;
using System.Net.Http.Json;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Tests.Infrastructure;
using UglyToad.PdfPig;

namespace FakturowanieKSeF.Tests;

/// <summary>Faktura proforma: numeracja, brak KSeF i rozliczeń, PDF, e-mail, zamiana na fakturę VAT, izolacja.</summary>
public class ProformaTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static SaveInvoiceRequest Proforma()
    {
        var r = TestUsers.SampleInvoice();
        r.IsProforma = true;
        return r;
    }

    private static async Task<InvoiceDto> CreateAsync(HttpClient client, SaveInvoiceRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/invoices", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;
    }

    [Fact]
    public async Task Proforma_WlasnaSeria_NieTrafiaDoKsef_INiePodlegaRozliczeniu()
    {
        var client = await factory.CreateClientWithKsefAsync();
        var proforma = await CreateAsync(client, Proforma());

        Assert.Equal(InvoiceKind.Proforma, proforma.Kind);
        Assert.StartsWith("PRO/1/11/2026", proforma.Number);
        Assert.Null(proforma.Payment);

        var send = await client.PostAsJsonAsync($"/api/invoices/{proforma.Id}/send", new SendInvoiceRequest { Version = proforma.Version });
        Assert.Equal(HttpStatusCode.Conflict, send.StatusCode);
        Assert.Contains("nie jest wysyłana do KSeF", await send.Content.ReadAsStringAsync());
        Assert.Empty(factory.Ksef.SentInvoices.Where(x => System.Text.Encoding.UTF8.GetString(x.Xml).Contains(proforma.Number)));
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync($"/api/invoices/{proforma.Id}/xml")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/invoices/{proforma.Id}/payments",
            new AddPaymentRequest { PaidOn = new DateOnly(2026, 11, 1), Amount = 10m })).StatusCode);

        // Numeracja faktur VAT niezależna od proform
        Assert.StartsWith("FV/1/", (await CreateAsync(client, TestUsers.SampleInvoice())).Number);
    }

    [Fact]
    public async Task Pdf_WyraznieOznaczonyJakoProforma()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        var proforma = await CreateAsync(client, Proforma());

        var pdf = await client.GetByteArrayAsync($"/api/invoices/{proforma.Id}/pdf");
        using var document = PdfDocument.Open(pdf);
        var text = string.Join(" ", document.GetPages().Select(p => p.Text));
        Assert.Contains("Faktura proforma", text);
        Assert.Contains("nie jest fakturą VAT", text);
        Assert.Contains(proforma.Number, text);
    }

    [Fact]
    public async Task ZamianaNaFaktureVat_RazZPowiazaniem_IFakturaTrafiaDoKsef()
    {
        var client = await factory.CreateClientWithKsefAsync();
        var request = Proforma();
        request.IssueDate = DateOnly.FromDateTime(DateTime.UtcNow);
        request.DueDate = request.IssueDate.AddDays(30);
        var proforma = await CreateAsync(client, request);

        var response = await client.PostAsJsonAsync($"/api/invoices/{proforma.Id}/convert", new { });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var vat = (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;
        Assert.Equal(InvoiceKind.Vat, vat.Kind);
        Assert.Equal(InvoiceStatus.Draft, vat.Status);
        Assert.StartsWith("FV/", vat.Number);
        Assert.Equal(proforma.DueDate, vat.DueDate); // termin z proformy, jeśli nie minął
        Assert.Equal(proforma.Items.Select(i => (i.Name, i.Quantity, i.UnitNetPrice)), vat.Items.Select(i => (i.Name, i.Quantity, i.UnitNetPrice)));
        Assert.Equal(proforma.TotalGross, vat.TotalGross);

        // Powiązania w obie strony
        Assert.Equal(proforma.Number, (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{vat.Id}"))!.Proforma!.Number);
        Assert.Equal(vat.Id, (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{proforma.Id}"))!.IssuedFromProforma!.Id);

        // Druga zamiana - konflikt (bez duplikatu faktury)
        var again = await client.PostAsJsonAsync($"/api/invoices/{proforma.Id}/convert", new { });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains(vat.Number, await again.Content.ReadAsStringAsync());

        // Faktura VAT z proformy to zwykła faktura - wysyłka do KSeF działa
        (await client.PostAsJsonAsync($"/api/invoices/{vat.Id}/send", new SendInvoiceRequest { Version = vat.Version })).EnsureSuccessStatusCode();
        for (var i = 0; i < 5 && await factory.ProcessKsefAsync() > 0; i++) { }
        Assert.Equal(InvoiceStatus.Accepted, (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{vat.Id}"))!.Status);
    }

    [Fact]
    public async Task EmailProformy_TylkoPdf_ZInformacjaZeToNieFakturaVat()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        var proforma = await CreateAsync(client, Proforma());
        var to = $"klient_{Guid.NewGuid():N}@example.com";

        (await client.PostAsJsonAsync($"/api/invoices/{proforma.Id}/email", new SendInvoiceEmailRequest { To = to })).EnsureSuccessStatusCode();
        await factory.ProcessEmailsAsync();

        var mail = Assert.Single(factory.Email.To(to));
        Assert.StartsWith("Faktura proforma PRO/", mail.Subject);
        Assert.Contains("nie jest fakturą VAT", mail.Body);
        Assert.Equal(["application/pdf"], mail.Attachments!.Select(a => a.ContentType));
    }

    [Fact]
    public async Task Walidacja_ZamianaNieProformy_ZaplataNaProformie_IZmianaRodzaju()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        var vat = await CreateAsync(client, TestUsers.SampleInvoice());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/invoices/{vat.Id}/convert", new { })).StatusCode);

        var paid = Proforma();
        paid.PaidOn = paid.IssueDate;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/invoices", paid)).StatusCode);

        // Rodzaju dokumentu nie można zmienić przy edycji
        var proforma = await CreateAsync(client, Proforma());
        var edit = TestUsers.SampleInvoice();
        edit.Version = proforma.Version;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/invoices/{proforma.Id}", edit)).StatusCode);
    }

    [Fact]
    public async Task UzytkownikB_NieZamieniaProformyA()
    {
        var clientA = await factory.CreateClientWithCompanyAsync();
        var proforma = await CreateAsync(clientA, Proforma());
        var clientB = await factory.CreateClientWithCompanyAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await clientB.PostAsJsonAsync($"/api/invoices/{proforma.Id}/convert", new { })).StatusCode);
    }
}
