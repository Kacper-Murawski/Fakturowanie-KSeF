using System.Net;
using System.Net.Http.Json;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Services.Email;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Models;
using FakturowanieKSeF.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FakturowanieKSeF.Tests;

/// <summary>Przypomnienia o płatności: ręczne, automatyczne (reguły odstępu i limitu), ustawienia, izolacja.</summary>
public class PaymentReminderTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Recipient(string? Email);

    /// <summary>Faktura przyjęta w KSeF, termin 15.09.2026 (po terminie), kontrahent z unikalnym adresem e-mail.</summary>
    private async Task<(HttpClient Client, InvoiceDto Invoice, string Email)> OverdueInvoiceAsync(HttpClient? client = null, bool withEmail = true)
    {
        client ??= await factory.CreateClientWithKsefAsync();
        var email = $"platnosci_{Guid.NewGuid():N}@kontrahent.example";
        var customerRequest = TestUsers.SampleCustomer(nip: TestUsers.RandomNip());
        customerRequest.Email = withEmail ? email : null;
        var customer = (await (await client.PostAsJsonAsync("/api/customers", customerRequest)).Content.ReadFromJsonAsync<CustomerDto>())!;

        var request = TestUsers.SampleInvoice(customer.Id);
        request.IssueDate = new DateOnly(2026, 9, 1);
        request.DueDate = new DateOnly(2026, 9, 15);
        return (client, await factory.AcceptedInvoiceAsync(client, request), email);
    }

    private async Task<List<EmailMessage>> RemindersAsync(int invoiceId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.EmailMessages.Where(m => m.InvoiceId == invoiceId && m.Kind == EmailKind.PaymentReminder).ToListAsync();
    }

    private async Task RunAutomaticAsync()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PaymentReminders>().RunAutomaticAsync();
    }

    private static Task<HttpResponseMessage> EnableAsync(HttpClient client, int days = 3) =>
        client.PutAsJsonAsync("/api/company/notifications", new SaveNotificationSettingsRequest { AutoReminders = true, ReminderDaysAfterDue = days });

    [Fact]
    public void Tresc_PodajeKwotePozostalaDniPoTerminieIRachunek()
    {
        var invoice = new Invoice { Number = "FV/3/09/2026", IssueDate = new DateOnly(2026, 9, 1), DueDate = new DateOnly(2026, 9, 15),
            Currency = "PLN", BankAccountNumber = "61109010140000071219812874", KsefNumber = "X-1" };

        var (subject, body) = EmailTemplates.PaymentReminder(invoice, new Company { Name = "Moja Firma" }, 897.5m, new DateOnly(2026, 9, 20), null);

        Assert.Equal("Przypomnienie o płatności - faktura FV/3/09/2026 - Moja Firma", subject);
        Assert.Contains("Termin płatności minął 15.09.2026 (5 dni temu)", body);
        Assert.Contains("Pozostało do zapłaty: 897,50 PLN", body);
        Assert.Contains("Tytuł przelewu: FV/3/09/2026", body);
        Assert.Contains("61 1090 1014 0000 0712 1981 2874", body);
        Assert.Contains("TESTOWEGO", body);
    }

    [Fact]
    public async Task RecznePrzypomnienie_ZKwotaPozostala_IFakturaWZalaczniku()
    {
        var (client, invoice, email) = await OverdueInvoiceAsync();
        (await client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/payments",
            new AddPaymentRequest { PaidOn = new DateOnly(2026, 9, 10), Amount = 1000m })).EnsureSuccessStatusCode();

        // Podpowiedź adresu - z kontrahenta
        Assert.Equal(email, (await client.GetFromJsonAsync<Recipient>($"/api/invoices/{invoice.Id}/email-recipient"))!.Email);

        var response = await client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/reminder",
            new SendInvoiceEmailRequest { To = email, Message = "Prosimy o pilną wpłatę." });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await factory.ProcessEmailsAsync();

        var mail = Assert.Single(factory.Email.To(email));
        Assert.StartsWith("Przypomnienie o płatności", mail.Subject);
        Assert.Contains("Pozostało do zapłaty: 897,50 PLN", mail.Body); // 1 897,50 - 1 000,00
        Assert.Contains("Prosimy o pilną wpłatę.", mail.Body);
        Assert.Equal(2, mail.Attachments!.Count);

        var history = (await client.GetFromJsonAsync<List<EmailMessageDto>>($"/api/invoices/{invoice.Id}/emails"))!;
        Assert.Equal(EmailKind.PaymentReminder, Assert.Single(history).Kind);
    }

    [Fact]
    public async Task Przypomnienie_NieDlaFakturyOpłaconej_SzkicuIKorekty()
    {
        var (client, invoice, email) = await OverdueInvoiceAsync();
        var draft = (await (await client.PostAsJsonAsync("/api/invoices", TestUsers.SampleInvoice())).Content.ReadFromJsonAsync<InvoiceDto>())!;
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync($"/api/invoices/{draft.Id}/reminder", new SendInvoiceEmailRequest { To = email })).StatusCode);

        (await client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/payments",
            new AddPaymentRequest { PaidOn = new DateOnly(2026, 9, 10), Amount = invoice.TotalGross })).EnsureSuccessStatusCode();
        var paid = await client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/reminder", new SendInvoiceEmailRequest { To = email });
        Assert.Equal(HttpStatusCode.Conflict, paid.StatusCode);
        Assert.Contains("opłacona", await paid.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Automatyczne_PoTerminie_CoSiedemDni_NajwyzejTrzy()
    {
        var (client, invoice, email) = await OverdueInvoiceAsync();

        // Wyłączone - brak przypomnień
        await RunAutomaticAsync();
        Assert.Empty(await RemindersAsync(invoice.Id));

        (await EnableAsync(client)).EnsureSuccessStatusCode();
        await RunAutomaticAsync();
        var first = Assert.Single(await RemindersAsync(invoice.Id));
        Assert.Equal(email, first.To);

        // Ponowne uruchomienie tego samego dnia nie dubluje przypomnienia
        await RunAutomaticAsync();
        Assert.Single(await RemindersAsync(invoice.Id));

        // Po 7 dniach kolejne, ale najwyżej 3 łącznie
        for (var i = 0; i < 4; i++)
        {
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.EmailMessages.Where(m => m.InvoiceId == invoice.Id)
                    .ExecuteUpdateAsync(u => u.SetProperty(m => m.CreatedAt, m => m.CreatedAt.AddDays(-8)));
            }
            await RunAutomaticAsync();
        }
        Assert.Equal(PaymentReminders.MaxReminders, (await RemindersAsync(invoice.Id)).Count);
    }

    [Fact]
    public async Task Automatyczne_PomijaOpłacone_NiedawnoPoTerminie_IBezAdresu()
    {
        var client = await factory.CreateClientWithKsefAsync();
        (await EnableAsync(client, days: 3)).EnsureSuccessStatusCode();

        var (_, paid, _) = await OverdueInvoiceAsync(client);
        (await client.PostAsJsonAsync($"/api/invoices/{paid.Id}/payments",
            new AddPaymentRequest { PaidOn = new DateOnly(2026, 9, 10), Amount = paid.TotalGross })).EnsureSuccessStatusCode();
        var (_, noAddress, _) = await OverdueInvoiceAsync(client, withEmail: false);

        // Termin wczoraj - za wcześnie na przypomnienie (3 dni po terminie)
        var recent = TestUsers.SampleInvoice();
        recent.Buyer!.Name = "Klient z terminem wczoraj";
        recent.IssueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10);
        recent.DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var recentInvoice = await factory.AcceptedInvoiceAsync(client, recent);
        (await client.PostAsJsonAsync($"/api/invoices/{recentInvoice.Id}/email", new SendInvoiceEmailRequest { To = "wczoraj@kontrahent.example" }))
            .EnsureSuccessStatusCode();

        await RunAutomaticAsync();

        Assert.Empty(await RemindersAsync(paid.Id));
        Assert.Empty(await RemindersAsync(noAddress.Id));
        Assert.Empty(await RemindersAsync(recentInvoice.Id));
    }

    [Fact]
    public async Task Ustawienia_WalidacjaIZapis()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        var initial = (await client.GetFromJsonAsync<NotificationSettingsDto>("/api/company/notifications"))!;
        Assert.False(initial.AutoReminders);
        Assert.Equal(3, initial.ReminderDaysAfterDue);
        Assert.True(initial.EmailConfigured);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/company/notifications",
            new SaveNotificationSettingsRequest { AutoReminders = true, ReminderDaysAfterDue = 61 })).StatusCode);
        (await EnableAsync(client, days: 7)).EnsureSuccessStatusCode();
        var saved = (await client.GetFromJsonAsync<NotificationSettingsDto>("/api/company/notifications"))!;
        Assert.True(saved.AutoReminders);
        Assert.Equal(7, saved.ReminderDaysAfterDue);
    }

    [Fact]
    public async Task UzytkownikB_NiePrzypominaOFakturzeA_INieWidziAdresu()
    {
        var (_, invoiceA, _) = await OverdueInvoiceAsync();
        var clientB = await factory.CreateClientWithKsefAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await clientB.PostAsJsonAsync($"/api/invoices/{invoiceA.Id}/reminder",
            new SendInvoiceEmailRequest { To = "x@example.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/invoices/{invoiceA.Id}/email-recipient")).StatusCode);
    }
}
