using System.Net;
using System.Net.Http.Json;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Services;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Models;
using FakturowanieKSeF.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FakturowanieKSeF.Tests;

/// <summary>Wyznaczanie kolejnych dat faktur cyklicznych.</summary>
public class RecurringScheduleTests
{
    [Theory]
    [InlineData("2026-01-31", 1, 31, "2026-02-28")] // krótszy miesiąc - ostatni dzień
    [InlineData("2026-02-28", 1, 31, "2026-03-31")] // powrót do dnia 31
    [InlineData("2026-11-15", 3, 15, "2027-02-15")] // co kwartał, przez koniec roku
    [InlineData("2027-02-28", 12, 29, "2028-02-29")] // rok przestępny
    public void NastepnaData(string from, int months, int day, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), RecurringInvoices.Next(DateOnly.Parse(from), months, day));
}

/// <summary>Faktury cykliczne: harmonogramy, tworzenie szkiców, reguły, powiadomienia, izolacja.</summary>
public class RecurringInvoiceApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw")).DateTime);

    /// <summary>Firma z kontrahentem i fakturą-wzorem przyjętą w KSeF (termin 14 dni).</summary>
    private async Task<(HttpClient Client, InvoiceDto Template, CustomerDto Customer)> TemplateAsync()
    {
        var client = await factory.CreateClientWithKsefAsync();
        var customer = (await (await client.PostAsJsonAsync("/api/customers", TestUsers.SampleCustomer(nip: TestUsers.RandomNip())))
            .Content.ReadFromJsonAsync<CustomerDto>())!;
        var template = await factory.AcceptedInvoiceAsync(client, TestUsers.SampleInvoice(customer.Id));
        return (client, template, customer);
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, int templateId, DateOnly next, int months = 1,
        DateOnly? end = null, bool notify = false) =>
        client.PostAsJsonAsync("/api/recurring-invoices", new SaveRecurringInvoiceRequest
        {
            TemplateInvoiceId = templateId, IntervalMonths = months, NextIssueDate = next, EndDate = end, Name = "Abonament",
            NotifyByEmail = notify
        });

    private async Task RunAsync()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RecurringInvoices>().RunAsync();
    }

    [Fact]
    public async Task SzkicNaWzorFaktury_ZAktualnymiDanymiKontrahenta_IPrzesuniecieTerminu()
    {
        var (client, template, customer) = await TemplateAsync();
        var schedule = (await (await CreateAsync(client, template.Id, Today)).Content.ReadFromJsonAsync<RecurringInvoiceDto>())!;
        Assert.Equal(template.Number, schedule.TemplateNumber);

        // Kontrahent zmienił nazwę - szkic dostaje aktualne dane z książki adresowej
        var changed = TestUsers.SampleCustomer(nip: customer.Nip!, name: "Kontrahent po zmianie nazwy S.A.");
        (await client.PutAsJsonAsync($"/api/customers/{customer.Id}", changed)).EnsureSuccessStatusCode();

        await RunAsync();

        var after = (await client.GetFromJsonAsync<RecurringInvoiceDto>($"/api/recurring-invoices/{schedule.Id}"))!;
        Assert.Equal(1, after.IssuedCount);
        Assert.Equal(Today, after.LastIssuedOn);
        Assert.Equal(RecurringInvoices.Next(Today, 1, Today.Day), after.NextIssueDate);

        var draft = (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{after.LastInvoiceId}"))!;
        Assert.Equal(InvoiceStatus.Draft, draft.Status); // nie trafia do KSeF automatycznie
        Assert.NotEqual(template.Number, draft.Number);
        Assert.Equal(Today, draft.IssueDate);
        Assert.Equal(Today.AddDays(14), draft.DueDate); // ten sam odstęp co we wzorze (1.11 → 15.11)
        Assert.Equal("Kontrahent po zmianie nazwy S.A.", draft.Buyer.Name);
        Assert.Equal(template.Items.Select(i => (i.Name, i.Quantity, i.UnitNetPrice, i.VatRate)),
            draft.Items.Select(i => (i.Name, i.Quantity, i.UnitNetPrice, i.VatRate)));
        Assert.Equal(template.TotalGross, draft.TotalGross);

        // Ponowne uruchomienie tego samego dnia nie tworzy kolejnej faktury
        await RunAsync();
        Assert.Equal(1, (await client.GetFromJsonAsync<RecurringInvoiceDto>($"/api/recurring-invoices/{schedule.Id}"))!.IssuedCount);
    }

    [Fact]
    public async Task PoPrzestoju_JedenSzkic_ATerminWPrzyszlosci_IDataKoncowaKonczyHarmonogram()
    {
        var (client, template, _) = await TemplateAsync();
        var missed = (await (await CreateAsync(client, template.Id, Today)).Content.ReadFromJsonAsync<RecurringInvoiceDto>())!;
        var ending = (await (await CreateAsync(client, template.Id, Today, end: Today)).Content.ReadFromJsonAsync<RecurringInvoiceDto>())!;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.RecurringInvoices.Where(r => r.Id == missed.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.NextIssueDate, Today.AddMonths(-3)));
        }

        await RunAsync();

        var afterMissed = (await client.GetFromJsonAsync<RecurringInvoiceDto>($"/api/recurring-invoices/{missed.Id}"))!;
        Assert.Equal(1, afterMissed.IssuedCount);
        Assert.True(afterMissed.NextIssueDate > Today);
        var afterEnding = (await client.GetFromJsonAsync<RecurringInvoiceDto>($"/api/recurring-invoices/{ending.Id}"))!;
        Assert.Equal(1, afterEnding.IssuedCount);
        Assert.False(afterEnding.IsActive);
    }

    [Fact]
    public async Task WstrzymanyHarmonogram_NieTworzyFaktur()
    {
        var (client, template, _) = await TemplateAsync();
        var schedule = (await (await CreateAsync(client, template.Id, Today)).Content.ReadFromJsonAsync<RecurringInvoiceDto>())!;
        (await client.PutAsJsonAsync($"/api/recurring-invoices/{schedule.Id}", new SaveRecurringInvoiceRequest
        {
            TemplateInvoiceId = template.Id, IntervalMonths = 1, NextIssueDate = schedule.NextIssueDate, IsActive = false
        })).EnsureSuccessStatusCode();

        await RunAsync();

        Assert.Equal(0, (await client.GetFromJsonAsync<RecurringInvoiceDto>($"/api/recurring-invoices/{schedule.Id}"))!.IssuedCount);
    }

    [Fact]
    public async Task Walidacja_OkresDataWPrzeszlosciKorektaJakoWzor_IUsuniecieWzoru()
    {
        var (client, template, _) = await TemplateAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(client, template.Id, Today, months: 2)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(client, template.Id, Today.AddDays(-1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(client, template.Id, Today.AddDays(5), end: Today.AddDays(1))).StatusCode);

        // Szkic jako wzór: dozwolony, ale nie można go usunąć, dopóki jest wzorem
        var draft = (await (await client.PostAsJsonAsync("/api/invoices", TestUsers.SampleInvoice())).Content.ReadFromJsonAsync<InvoiceDto>())!;
        var schedule = (await (await CreateAsync(client, draft.Id, Today.AddDays(1))).Content.ReadFromJsonAsync<RecurringInvoiceDto>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/invoices/{draft.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/recurring-invoices/{schedule.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/invoices/{draft.Id}")).StatusCode);
    }

    [Fact]
    public async Task Powiadomienie_OPrzygotowanychSzkicach_NaAdresKonta()
    {
        var (client, template, _) = await TemplateAsync();
        var account = (await client.GetFromJsonAsync<NotificationSettingsDto>("/api/company/notifications"))!.AccountEmail!;
        (await CreateAsync(client, template.Id, Today, notify: true)).EnsureSuccessStatusCode();

        await RunAsync();
        await factory.ProcessEmailsAsync();

        var mail = Assert.Single(factory.Email.To(account), m => !m.Subject.Contains("Potwierdź adres")); // bez wiadomości z rejestracji
        Assert.StartsWith("Faktury cykliczne do wysłania: 1", mail.Subject);
        Assert.Contains("(Abonament)", mail.Body);
    }

    [Fact]
    public async Task UzytkownikB_NieWidziNieZmieniaHarmonogramuA_INieUzywaCudzegoWzoru()
    {
        var (clientA, template, _) = await TemplateAsync();
        var schedule = (await (await CreateAsync(clientA, template.Id, Today.AddDays(1))).Content.ReadFromJsonAsync<RecurringInvoiceDto>())!;
        var clientB = await factory.CreateClientWithKsefAsync();

        Assert.Empty((await clientB.GetFromJsonAsync<List<RecurringInvoiceDto>>("/api/recurring-invoices"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/recurring-invoices/{schedule.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.PutAsJsonAsync($"/api/recurring-invoices/{schedule.Id}",
            new SaveRecurringInvoiceRequest { TemplateInvoiceId = template.Id, NextIssueDate = Today.AddDays(2) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.DeleteAsync($"/api/recurring-invoices/{schedule.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(clientB, template.Id, Today.AddDays(1))).StatusCode);
    }
}
