using System.Net.Http.Json;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Models;
using FakturowanieKSeF.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FakturowanieKSeF.Tests;

/// <summary>Dane wykresów pulpitu: miesiące, wiekowanie należności, najwięksi nabywcy, izolacja.</summary>
public class DashboardTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw")).DateTime);

    private static SaveInvoiceRequest Invoice(string buyer, DateOnly issue, DateOnly due, decimal price)
    {
        var r = TestUsers.SampleInvoice();
        r.Buyer!.Name = buyer;
        r.IssueDate = issue;
        r.DueDate = due;
        r.Items = [new SaveInvoiceItemRequest { Name = "Usługa", Quantity = 1, UnitNetPrice = price, VatRate = VatRate.Rate23 }];
        return r;
    }

    [Fact]
    public async Task Miesiace_Wiekowanie_INajwiekszyNabywcy()
    {
        var client = await factory.CreateClientWithKsefAsync();
        var thisMonth = new DateOnly(Today.Year, Today.Month, 1);

        // Bieżący miesiąc: 1 000 + 500 netto (dwóch nabywców), w terminie
        var a = await factory.AcceptedInvoiceAsync(client, Invoice("Nabywca Duży", thisMonth, Today.AddDays(10), 1000m));
        await factory.AcceptedInvoiceAsync(client, Invoice("Nabywca Mały", thisMonth, Today.AddDays(10), 500m));
        // Dwa miesiące temu: 2 000 netto, 45 dni po terminie, częściowo zapłacona
        var old = await factory.AcceptedInvoiceAsync(client, Invoice("Nabywca Duży", thisMonth.AddMonths(-2), Today.AddDays(-45), 2000m));
        (await client.PostAsJsonAsync($"/api/invoices/{old.Id}/payments",
            new AddPaymentRequest { PaidOn = Today.AddDays(-1), Amount = 460m })).EnsureSuccessStatusCode();
        // Szkic nie jest sprzedażą
        (await client.PostAsJsonAsync("/api/invoices", Invoice("Szkic", thisMonth, Today, 99999m))).EnsureSuccessStatusCode();

        // Koszt w bieżącym miesiącu (faktura otrzymana)
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var companyId = (await db.Invoices.SingleAsync(i => i.Id == a.Id)).CompanyId;
            db.ReceivedInvoices.Add(new ReceivedInvoice
            {
                CompanyId = companyId, KsefNumber = $"1234563218-20261101-{Random.Shared.NextInt64(0, 0xFFFFFFFFFFFF):X12}-00",
                InvoiceNumber = "K/1", SellerNip = "1234563218", SellerName = "Dostawca", IssueDate = thisMonth, Currency = "PLN",
                TotalNet = 300m, TotalVat = 69m, TotalGross = 369m, KsefAcceptedAt = DateTime.UtcNow, Xml = "<x/>", DownloadedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var d = (await client.GetFromJsonAsync<DashboardDto>("/api/dashboard"))!;

        Assert.Equal(12, d.Months.Count);
        Assert.Equal((Today.Year, Today.Month), (d.Months[^1].Year, d.Months[^1].Month));
        Assert.Equal(1500m, d.Months[^1].SalesNet);
        Assert.Equal(300m, d.Months[^1].CostsNet);
        Assert.Equal(2000m, d.Months[^3].SalesNet);
        Assert.Equal(0m, d.Months[0].SalesNet);

        Assert.Equal(["w terminie", "1-30 dni po terminie", "31-60 dni po terminie", "ponad 60 dni po terminie"],
            d.ReceivablesAging.Select(x => x.Label));
        Assert.Equal(1845m, d.ReceivablesAging[0].Amount); // 1 230 + 615
        Assert.Equal(2, d.ReceivablesAging[0].Count);
        Assert.Equal(2000m, d.ReceivablesAging[2].Amount); // 2 460 - 460
        Assert.Equal(0, d.ReceivablesAging[3].Count);

        Assert.Equal("Nabywca Duży", d.TopCustomers[0].Name);
        Assert.Equal(3000m, d.TopCustomers[0].SalesNet);
        Assert.Equal(2, d.TopCustomers[0].Count);
        Assert.DoesNotContain(d.TopCustomers, c => c.Name == "Szkic");
    }

    [Fact]
    public async Task UzytkownikB_WidziTylkoWlasneDane()
    {
        var clientA = await factory.CreateClientWithKsefAsync();
        await factory.AcceptedInvoiceAsync(clientA, Invoice("Nabywca A", new DateOnly(Today.Year, Today.Month, 1), Today, 700m));
        var clientB = await factory.CreateClientWithCompanyAsync();

        var d = (await clientB.GetFromJsonAsync<DashboardDto>("/api/dashboard"))!;

        Assert.All(d.Months, m => Assert.Equal(0m, m.SalesNet + m.CostsNet));
        Assert.All(d.ReceivablesAging, b => Assert.Equal(0, b.Count));
        Assert.Empty(d.TopCustomers);
    }
}
