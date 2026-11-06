using System.Net;
using System.Net.Http.Json;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Models;
using FakturowanieKSeF.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FakturowanieKSeF.Tests;

/// <summary>Reguły rozliczeń (wspólne dla API i interfejsu).</summary>
public class PaymentRulesTests
{
    [Theory]
    [InlineData(100, 0, PaymentStatus.Unpaid)]
    [InlineData(100, 40, PaymentStatus.PartiallyPaid)]
    [InlineData(100, 100, PaymentStatus.Paid)]
    [InlineData(0, 0, PaymentStatus.Paid)]
    public void Status_ZaleznyOdKwotyZaplaconej(decimal gross, decimal paid, PaymentStatus expected) =>
        Assert.Equal(expected, PaymentRules.Status(gross, paid));

    [Fact]
    public void PoTerminie_DopieroPoDniuTerminu_ITylkoGdyNieopłacona()
    {
        var due = new DateOnly(2026, 11, 15);
        Assert.False(PaymentRules.IsOverdue(due, 100, 0, new DateOnly(2026, 11, 15)));
        Assert.True(PaymentRules.IsOverdue(due, 100, 0, new DateOnly(2026, 11, 16)));
        Assert.True(PaymentRules.IsOverdue(due, 100, 99.99m, new DateOnly(2026, 11, 16)));
        Assert.False(PaymentRules.IsOverdue(due, 100, 100, new DateOnly(2026, 11, 16)));
        Assert.False(PaymentRules.IsOverdue(null, 100, 0, new DateOnly(2026, 11, 16)));
    }

    [Fact]
    public void Platnosci_TylkoDlaFakturPrzekazanychDoKsef()
    {
        Assert.False(PaymentRules.AcceptsPayments(InvoiceStatus.Draft));
        Assert.False(PaymentRules.AcceptsPayments(InvoiceStatus.Rejected));
        Assert.True(PaymentRules.AcceptsPayments(InvoiceStatus.Pending));
        Assert.True(PaymentRules.AcceptsPayments(InvoiceStatus.Accepted));
    }
}

/// <summary>Rejestrowanie płatności, stan rozliczenia, filtry list i podsumowanie należności/zobowiązań.</summary>
public class PaymentApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>Faktura przyjęta w KSeF (atrapa) - brutto 1 897,50 zł; opcjonalnie z terminem w przeszłości.</summary>
    private async Task<(HttpClient Client, InvoiceDto Invoice)> AcceptedInvoiceAsync(bool overdue = false, HttpClient? client = null)
    {
        if (client is null)
        {
            client = await factory.CreateClientWithCompanyAsync();
            (await client.PostAsync("/api/company/ksef/test-token", null)).EnsureSuccessStatusCode();
        }

        var request = TestUsers.SampleInvoice();
        if (overdue)
        {
            request.IssueDate = new DateOnly(2026, 9, 1);
            request.DueDate = new DateOnly(2026, 9, 15);
        }
        var draft = (await (await client.PostAsJsonAsync("/api/invoices", request)).Content.ReadFromJsonAsync<InvoiceDto>())!;
        Assert.Null(draft.Payment); // szkic nie jest należnością

        (await client.PostAsJsonAsync($"/api/invoices/{draft.Id}/send", new SendInvoiceRequest { Version = draft.Version })).EnsureSuccessStatusCode();
        for (var i = 0; i < 5 && await factory.ProcessKsefAsync() > 0; i++) { }

        var invoice = (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{draft.Id}"))!;
        Assert.Equal(InvoiceStatus.Accepted, invoice.Status);
        return (client, invoice);
    }

    private static async Task<PaymentInfoDto> PayAsync(HttpClient client, string url, decimal amount, DateOnly? paidOn = null)
    {
        var response = await client.PostAsJsonAsync($"{url}/payments",
            new AddPaymentRequest { PaidOn = paidOn ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), Amount = amount, Note = "przelew" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PaymentInfoDto>())!;
    }

    [Fact]
    public async Task WplatyCzesciowe_IPelne_ZmieniajaStanRozliczenia_IUsuniecieCofa()
    {
        var (client, invoice) = await AcceptedInvoiceAsync();
        var url = $"/api/invoices/{invoice.Id}";
        Assert.Equal(PaymentStatus.Unpaid, invoice.Payment!.Status);
        Assert.Equal(1897.50m, invoice.Payment.Remaining);

        var partial = await PayAsync(client, url, 1000m);
        Assert.Equal(PaymentStatus.PartiallyPaid, partial.Status);
        Assert.Equal(897.50m, partial.Remaining);

        var full = await PayAsync(client, url, 897.50m);
        Assert.Equal(PaymentStatus.Paid, full.Status);
        Assert.Equal(0m, full.Remaining);
        Assert.Equal(2, full.Payments.Count);
        Assert.Equal("przelew", full.Payments[0].Note);

        // Stan widoczny w szczegółach i na liście
        var details = await client.GetFromJsonAsync<InvoiceDto>(url);
        Assert.Equal(PaymentStatus.Paid, details!.Payment!.Status);
        var row = (await client.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices"))!.Items.Single(i => i.Id == invoice.Id);
        Assert.Equal(PaymentStatus.Paid, row.PaymentStatus);
        Assert.Equal(1897.50m, row.PaidAmount);

        // Usunięcie błędnej wpłaty
        var response = await client.DeleteAsync($"{url}/payments/{full.Payments[1].Id}");
        var after = (await response.Content.ReadFromJsonAsync<PaymentInfoDto>())!;
        Assert.Equal(PaymentStatus.PartiallyPaid, after.Status);
        Assert.Single(after.Payments);
    }

    [Fact]
    public async Task Walidacja_Nadplata_DataZPrzyszlosci_SzkicIFakturaOplacona()
    {
        var (client, invoice) = await AcceptedInvoiceAsync();
        var url = $"/api/invoices/{invoice.Id}/payments";

        var overpay = await client.PostAsJsonAsync(url, new AddPaymentRequest { PaidOn = new DateOnly(2026, 11, 1), Amount = 2000m });
        Assert.Equal(HttpStatusCode.BadRequest, overpay.StatusCode);
        // Treść JSON z kodowaniem znaków spoza ASCII (\u0105) i separatorem tysięcy pl-PL (spacja nierozdzielająca)
        var overpayText = System.Text.RegularExpressions.Regex.Unescape(await overpay.Content.ReadAsStringAsync());
        Assert.Contains("Kwota przekracza pozostałą do zapłaty (1\u00A0897,50)", overpayText);

        var future = await client.PostAsJsonAsync(url, new AddPaymentRequest { PaidOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5), Amount = 10m });
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        Assert.Contains("nie może być późniejsza niż dzisiaj", System.Text.RegularExpressions.Regex.Unescape(await future.Content.ReadAsStringAsync()));

        var zero = await client.PostAsJsonAsync(url, new AddPaymentRequest { PaidOn = new DateOnly(2026, 11, 1), Amount = 0m });
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);

        await PayAsync(client, $"/api/invoices/{invoice.Id}", 1897.50m);
        var paidAlready = await client.PostAsJsonAsync(url, new AddPaymentRequest { PaidOn = new DateOnly(2026, 11, 1), Amount = 1m });
        Assert.Equal(HttpStatusCode.Conflict, paidAlready.StatusCode);

        // Szkic nie przyjmuje wpłat
        var draft = (await (await client.PostAsJsonAsync("/api/invoices", TestUsers.SampleInvoice())).Content.ReadFromJsonAsync<InvoiceDto>())!;
        var toDraft = await client.PostAsJsonAsync($"/api/invoices/{draft.Id}/payments", new AddPaymentRequest { PaidOn = new DateOnly(2026, 11, 1), Amount = 1m });
        Assert.Equal(HttpStatusCode.Conflict, toDraft.StatusCode);
    }

    [Fact]
    public async Task Filtry_PoTerminie_Nieoplacone_Oplacone_IPodsumowanieNaleznosci()
    {
        var (client, overdue) = await AcceptedInvoiceAsync(overdue: true);
        var (_, current) = await AcceptedInvoiceAsync(client: client);
        var (_, paid) = await AcceptedInvoiceAsync(client: client);
        await PayAsync(client, $"/api/invoices/{paid.Id}", 1897.50m);
        await PayAsync(client, $"/api/invoices/{overdue.Id}", 97.50m);
        await client.PostAsJsonAsync("/api/invoices", TestUsers.SampleInvoice()); // szkic - nie jest należnością

        async Task<int[]> Ids(string filter) =>
            (await client.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>($"/api/invoices?payment={filter}"))!.Items.Select(i => i.Id).Order().ToArray();

        Assert.Equal([overdue.Id], await Ids("Overdue"));
        Assert.Equal(new[] { overdue.Id, current.Id }.Order().ToArray(), await Ids("Unpaid"));
        Assert.Equal([paid.Id], await Ids("Paid"));

        var row = (await client.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices?payment=Overdue"))!.Items.Single();
        Assert.True(row.IsOverdue);
        Assert.Equal(PaymentStatus.PartiallyPaid, row.PaymentStatus);

        var summary = (await client.GetFromJsonAsync<PaymentsSummaryDto>("/api/payments/summary"))!;
        Assert.Equal(2, summary.ReceivablesCount);
        Assert.Equal(1800m + 1897.50m, summary.ReceivablesTotal);
        Assert.Equal(1, summary.ReceivablesOverdueCount);
        Assert.Equal(1800m, summary.ReceivablesOverdue);
    }

    [Fact]
    public async Task FakturyOtrzymane_Zaplata_FiltrPoTerminie_IPodsumowanieZobowiazan()
    {
        var nip = TestUsers.RandomNip();
        var client = await factory.CreateClientWithCompanyAsync(nip);
        int overdueId, currentId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var company = await db.Companies.SingleAsync(c => c.Nip == nip);
            ReceivedInvoice New(string number, DateOnly due) => new()
            {
                CompanyId = company.Id, KsefNumber = $"1234563218-20261030-{Guid.NewGuid():N}"[..37] + "-00",
                InvoiceNumber = number, SellerNip = "1234563218", SellerName = "Hurtownia", IssueDate = new DateOnly(2026, 9, 1),
                DueDate = due, TotalNet = 100m, TotalVat = 23m, TotalGross = 123m, KsefAcceptedAt = DateTime.UtcNow,
                DownloadedAt = DateTime.UtcNow, Xml = ReceivedInvoicesTests.ForeignInvoiceXml("1234563218", nip, number, 3)
            };
            var overdue = New("FV/1", new DateOnly(2026, 9, 15));
            var current = New("FV/2", new DateOnly(2099, 1, 1));
            db.ReceivedInvoices.AddRange(overdue, current);
            await db.SaveChangesAsync();
            (overdueId, currentId) = (overdue.Id, current.Id);
        }

        var list = (await client.GetFromJsonAsync<PagedResult<ReceivedInvoiceListItemDto>>("/api/received-invoices?payment=Overdue"))!;
        Assert.Equal(overdueId, list.Items.Single().Id);
        Assert.True(list.Items.Single().IsOverdue);

        var summary = (await client.GetFromJsonAsync<PaymentsSummaryDto>("/api/payments/summary"))!;
        Assert.Equal(246m, summary.PayablesTotal);
        Assert.Equal(123m, summary.PayablesOverdue);

        var paid = await PayAsync(client, $"/api/received-invoices/{overdueId}", 123m);
        Assert.Equal(PaymentStatus.Paid, paid.Status);
        Assert.False(paid.IsOverdue);
        Assert.Empty((await client.GetFromJsonAsync<PagedResult<ReceivedInvoiceListItemDto>>("/api/received-invoices?payment=Overdue"))!.Items);

        var details = await client.GetFromJsonAsync<ReceivedInvoiceDto>($"/api/received-invoices/{overdueId}");
        Assert.Equal(PaymentStatus.Paid, details!.Payment!.Status);
        Assert.Equal(currentId, (await client.GetFromJsonAsync<PagedResult<ReceivedInvoiceListItemDto>>("/api/received-invoices?payment=Unpaid"))!.Items.Single().Id);
    }

    [Fact]
    public async Task UzytkownikB_NieRejestrujeINieUsuwaPlatnosciUzytkownikaA()
    {
        var (clientA, invoiceA) = await AcceptedInvoiceAsync();
        var info = await PayAsync(clientA, $"/api/invoices/{invoiceA.Id}", 100m);

        var (clientB, invoiceB) = await AcceptedInvoiceAsync();
        var payment = new AddPaymentRequest { PaidOn = new DateOnly(2026, 11, 1), Amount = 1m };

        Assert.Equal(HttpStatusCode.NotFound, (await clientB.PostAsJsonAsync($"/api/invoices/{invoiceA.Id}/payments", payment)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.DeleteAsync($"/api/invoices/{invoiceA.Id}/payments/{info.Payments[0].Id}")).StatusCode);
        // Cudza płatność „podpięta” pod własną fakturę
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.DeleteAsync($"/api/invoices/{invoiceB.Id}/payments/{info.Payments[0].Id}")).StatusCode);

        var summaryB = (await clientB.GetFromJsonAsync<PaymentsSummaryDto>("/api/payments/summary"))!;
        Assert.Equal(1, summaryB.ReceivablesCount);
        Assert.Single((await clientA.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{invoiceA.Id}"))!.Payment!.Payments);
    }

    [Fact]
    public async Task ZaplaconoPrzyWystawieniu_TrafiaDoXml_IRozliczenia_BezMozliwosciUsuniecia()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        (await client.PostAsync("/api/company/ksef/test-token", null)).EnsureSuccessStatusCode();

        var request = TestUsers.SampleInvoice();
        request.PaymentMethod = PaymentMethod.Cash;
        request.DueDate = null;
        request.PaidOn = request.IssueDate;
        var draft = (await (await client.PostAsJsonAsync("/api/invoices", request)).Content.ReadFromJsonAsync<InvoiceDto>())!;
        Assert.Equal(request.IssueDate, draft.PaidOn);
        Assert.Contains("<Zaplacono>1</Zaplacono>", await client.GetStringAsync($"/api/invoices/{draft.Id}/xml"));

        (await client.PostAsJsonAsync($"/api/invoices/{draft.Id}/send", new SendInvoiceRequest { Version = draft.Version })).EnsureSuccessStatusCode();
        for (var i = 0; i < 5 && await factory.ProcessKsefAsync() > 0; i++) { }

        var invoice = (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{draft.Id}"))!;
        Assert.Equal(InvoiceStatus.Accepted, invoice.Status);
        Assert.Equal(PaymentStatus.Paid, invoice.Payment!.Status);
        var payment = Assert.Single(invoice.Payment.Payments);
        Assert.True(payment.FromInvoice);
        Assert.Equal(invoice.TotalGross, payment.Amount);
        Assert.Equal(request.IssueDate, payment.PaidOn);

        // Zapłata zapisana w fakturze w KSeF nie może zniknąć z rozliczenia
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/invoices/{invoice.Id}/payments/{payment.Id}")).StatusCode);

        var paidList = (await client.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices?payment=Paid"))!;
        Assert.Contains(paidList.Items, i => i.Id == invoice.Id);
    }

    [Fact]
    public async Task ZaplaconoPrzyWystawieniu_PonownaWysylkaPoOdrzuceniu_NieDublujePlatnosci()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        (await client.PostAsync("/api/company/ksef/test-token", null)).EnsureSuccessStatusCode();

        var request = TestUsers.SampleInvoice();
        request.DueDate = null;
        request.PaidOn = request.IssueDate;
        var draft = (await (await client.PostAsJsonAsync("/api/invoices", request)).Content.ReadFromJsonAsync<InvoiceDto>())!;
        (await client.PostAsJsonAsync($"/api/invoices/{draft.Id}/send", new SendInvoiceRequest { Version = draft.Version })).EnsureSuccessStatusCode();

        // Symulacja odrzucenia przez KSeF przed przetworzeniem
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await db.Invoices.SingleAsync(i => i.Id == draft.Id);
            entity.Status = InvoiceStatus.Rejected;
            await db.SaveChangesAsync();
        }

        var rejected = (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{draft.Id}"))!;
        (await client.PostAsJsonAsync($"/api/invoices/{draft.Id}/send", new SendInvoiceRequest { Version = rejected.Version })).EnsureSuccessStatusCode();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.Payments.CountAsync(p => p.InvoiceId == draft.Id && p.FromInvoice));
        }
    }

    [Fact]
    public async Task ZaplaconoPrzyWystawieniu_DataPoWystawieniu_JestOdrzucana()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        var request = TestUsers.SampleInvoice();
        request.PaidOn = request.IssueDate.AddDays(1);

        var response = await client.PostAsJsonAsync("/api/invoices", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("PaidOn", await response.Content.ReadAsStringAsync());
    }
}
