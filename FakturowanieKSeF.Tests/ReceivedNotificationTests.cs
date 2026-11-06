using System.Net.Http.Json;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Services;
using FakturowanieKSeF.Api.Services.Email;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Ksef;
using FakturowanieKSeF.Models;
using FakturowanieKSeF.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FakturowanieKSeF.Tests;

/// <summary>Treść powiadomienia o nowych fakturach kosztowych.</summary>
public class ReceivedNotificationTemplateTests
{
    private static ReceivedInvoice R(string number, decimal gross, DateOnly? due = null, string currency = "PLN") => new()
    {
        InvoiceNumber = number, SellerName = "Hurtownia sp. z o.o.", SellerNip = "1234563218", TotalGross = gross,
        Currency = currency, DueDate = due, IssueDate = new DateOnly(2026, 11, 1)
    };

    [Fact]
    public void Lista_KwotyTerminy_OstrzezenieOBialejLiscie_ILink()
    {
        var company = new Company { Name = "Moja Firma", Nip = "5260250274" };

        var (subject, body) = EmailTemplates.ReceivedInvoices(company,
            [R("FV/2", 20000m, new DateOnly(2026, 11, 20)), R("FV/1", 123m, new DateOnly(2026, 11, 10)), R("EUR/1", 20000m, null, "EUR")],
            "https://faktury.example/");

        Assert.Equal("Nowe faktury kosztowe w KSeF: 3 - Moja Firma", subject);
        Assert.Contains("1. FV/1 - Hurtownia sp. z o.o.", body); // najpierw najbliższy termin
        Assert.Contains("kwota 123,00 PLN, termin płatności 10.11.2026", body);
        Assert.Single(body.Split('\n'), l => l.Contains("białej liście")); // tylko faktura w PLN powyżej 15 000 zł
        Assert.Contains("https://faktury.example/faktury-otrzymane", body);
    }

    [Fact]
    public void DlugaLista_JestSkracana()
    {
        var invoices = Enumerable.Range(1, EmailTemplates.MaxListedInvoices + 5).Select(i => R($"FV/{i}", 10m)).ToList();
        var (_, body) = EmailTemplates.ReceivedInvoices(new Company { Name = "F", Nip = "5260250274" }, invoices, null);
        Assert.Contains("... i 5 kolejnych.", body);
        Assert.Contains("zakładce „Faktury otrzymane”", body);
    }
}

/// <summary>Powiadomienia po synchronizacji faktur otrzymanych: ustawienia, adres, brak powiadomień bez nowych faktur.</summary>
public class ReceivedNotificationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<(HttpClient Client, string Nip)> CompanyAsync()
    {
        var nip = TestUsers.RandomNip();
        var client = await factory.CreateClientWithCompanyAsync(nip);
        (await client.PostAsync("/api/company/ksef/test-token", null)).EnsureSuccessStatusCode();
        return (client, nip);
    }

    private void Publish(string buyerNip, string number, decimal gross)
    {
        var storedAt = DateTimeOffset.UtcNow.AddHours(-1);
        var ksefNumber = $"1234563218-20261105-{Random.Shared.NextInt64(0, 0xFFFFFFFFFFFF):X12}-00";
        var meta = new KsefInvoiceMetadata(ksefNumber, number, new DateOnly(2026, 10, 30), storedAt, storedAt,
            "1234563218", "Hurtownia Dostawca sp. z o.o.", gross - gross * 0.23m / 1.23m, gross * 0.23m / 1.23m, gross, "PLN");
        lock (factory.Ksef.Received)
        {
            if (!factory.Ksef.Received.TryGetValue(buyerNip, out var list)) factory.Ksef.Received[buyerNip] = list = [];
            list.Add((meta, ReceivedInvoicesTests.ForeignInvoiceXml("1234563218", buyerNip, number, 3)));
        }
    }

    private async Task SyncAsync(string nip)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var company = await db.Companies.SingleAsync(c => c.Nip == nip);
        await scope.ServiceProvider.GetRequiredService<ReceivedInvoiceSync>().SyncAsync(company);
    }

    private async Task<List<EmailMessage>> NotificationsAsync(string nip)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.EmailMessages.Where(m => m.Company.Nip == nip && m.Kind == EmailKind.ReceivedInvoices).ToListAsync();
    }

    [Fact]
    public async Task NoweFaktury_JednaZbiorczaWiadomosc_NaUstawionyAdres()
    {
        var (client, nip) = await CompanyAsync();
        var address = $"ksiegowa_{Guid.NewGuid():N}@example.com";
        (await client.PutAsJsonAsync("/api/company/notifications",
            new SaveNotificationSettingsRequest { NotifyReceivedInvoices = true, NotificationEmail = address })).EnsureSuccessStatusCode();
        Publish(nip, "FV/101/10/2026", 134.25m);
        Publish(nip, "FV/102/10/2026", 18450m);

        await SyncAsync(nip);
        await factory.ProcessEmailsAsync();

        var mail = Assert.Single(factory.Email.To(address));
        Assert.Equal("Nowe faktury kosztowe w KSeF: 2 - Moja Firma sp. z o.o.", mail.Subject);
        Assert.Contains("FV/101/10/2026", mail.Body);
        Assert.Contains("FV/102/10/2026", mail.Body);
        Assert.Contains("białej liście", mail.Body);
        Assert.Null(mail.ReplyTo); // wiadomość do użytkownika, nie do kontrahenta
        Assert.Empty(mail.Attachments!);

        // Kolejna synchronizacja bez nowych faktur - bez powiadomienia
        await SyncAsync(nip);
        Assert.Single(await NotificationsAsync(nip));
    }

    [Fact]
    public async Task DomyslnieWylaczone_APustyAdres_ToAdresKonta()
    {
        var (client, nip) = await CompanyAsync();
        Publish(nip, "FV/201/10/2026", 100m);
        await SyncAsync(nip);
        Assert.Empty(await NotificationsAsync(nip));

        var settings = (await (await client.PutAsJsonAsync("/api/company/notifications",
            new SaveNotificationSettingsRequest { NotifyReceivedInvoices = true })).Content.ReadFromJsonAsync<NotificationSettingsDto>())!;
        Assert.Null(settings.NotificationEmail);
        Assert.StartsWith("user_", settings.AccountEmail);

        Publish(nip, "FV/202/10/2026", 100m);
        await SyncAsync(nip);
        var notification = Assert.Single(await NotificationsAsync(nip));
        Assert.Equal(settings.AccountEmail, notification.To);
        Assert.Contains("FV/202/10/2026", notification.Body);
        Assert.DoesNotContain("FV/201/10/2026", notification.Body); // tylko faktury nowe w tej synchronizacji
    }

    [Fact]
    public async Task NieprawidlowyAdres_JestOdrzucany()
    {
        var (client, _) = await CompanyAsync();
        var response = await client.PutAsJsonAsync("/api/company/notifications",
            new SaveNotificationSettingsRequest { NotifyReceivedInvoices = true, NotificationEmail = "zly-adres" });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }
}
