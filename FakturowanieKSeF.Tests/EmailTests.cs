using System.Net;
using System.Net.Http.Json;
using System.Text;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Services.Email;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Models;
using FakturowanieKSeF.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;

namespace FakturowanieKSeF.Tests;

/// <summary>Budowa wiadomości MIME i treść wiadomości z fakturą.</summary>
public class EmailMessageTests
{
    private static readonly EmailOptions Options = new() { From = "faktury@fakturowanie.test", FromName = "Fakturowanie KSeF", SmtpHost = "x" };

    [Fact]
    public void Nadawca_ToAdresAplikacji_ZNazwaFirmy_IOdpowiedziaNaAdresFirmy()
    {
        var mime = SmtpEmailSender.Build(new OutgoingEmail("jan@kontrahent.pl", "Faktura FV/1\r\nBcc: ofiara@example.com", "Treść",
            "Moja Firma sp. z o.o.", "biuro@mojafirma.pl",
            [new EmailAttachment("FV_1.pdf", "application/pdf", [1, 2, 3])]), Options);

        var from = (MailboxAddress)mime.From.Single();
        Assert.Equal("faktury@fakturowanie.test", from.Address);
        Assert.Equal("Moja Firma sp. z o.o.", from.Name);
        Assert.Equal("biuro@mojafirma.pl", ((MailboxAddress)mime.ReplyTo.Single()).Address);
        Assert.Equal("jan@kontrahent.pl", ((MailboxAddress)mime.To.Single()).Address);
        // Koniec linii w temacie nie pozwala dopisać nagłówka (wstrzyknięcie nagłówków)
        Assert.Equal("Faktura FV/1Bcc: ofiara@example.com", mime.Subject);
        Assert.Empty(mime.Bcc);
        Assert.Equal("FV_1.pdf", mime.Attachments.OfType<MimePart>().Single().FileName);
        Assert.Equal("Treść", mime.TextBody);
    }

    [Fact]
    public void BezNazwyFirmy_NadawcaToNazwaAplikacji()
    {
        var mime = SmtpEmailSender.Build(new OutgoingEmail("jan@example.com", "Temat", "Treść"), Options);
        Assert.Equal("Fakturowanie KSeF", ((MailboxAddress)mime.From.Single()).Name);
        Assert.Empty(mime.ReplyTo);
    }

    [Fact]
    public void TrescFaktury_ZawieraKwoteTerminRachunekNumerKsef_IOstrzezenieOSrodowiskuTestowym()
    {
        var invoice = new Invoice
        {
            Number = "FV/7/11/2026", IssueDate = new DateOnly(2026, 11, 1), DueDate = new DateOnly(2026, 11, 15),
            Currency = "PLN", TotalGross = 1897.5m, BankAccountNumber = "61109010140000071219812874", KsefNumber = "5260250274-20261101-ABC-00"
        };
        var company = new Company { Name = "Moja Firma", Email = "biuro@mojafirma.pl" };

        var (subject, body) = EmailTemplates.Invoice(invoice, company, "Dziękujemy za współpracę!");

        Assert.Equal("Faktura FV/7/11/2026 - Moja Firma", subject);
        Assert.Contains("Kwota do zapłaty: 1 897,50 PLN", body);
        Assert.Contains("Termin płatności: 15.11.2026", body);
        Assert.Contains("61 1090 1014 0000 0712 1981 2874", body);
        Assert.Contains("Numer KSeF: 5260250274-20261101-ABC-00", body);
        Assert.Contains("Dziękujemy za współpracę!", body);
        Assert.Contains("TESTOWEGO", body);
    }

    [Fact]
    public void TrescKorekty_PodajeKwoteDoZwrotu()
    {
        var invoice = new Invoice { Number = "KOR/1/11/2026", Kind = InvoiceKind.Correction, CorrectedInvoiceNumber = "FV/7/11/2026",
            IssueDate = new DateOnly(2026, 11, 3), Currency = "PLN", TotalGross = -246m };

        var (subject, body) = EmailTemplates.Invoice(invoice, new Company { Name = "Moja Firma" }, null);

        Assert.StartsWith("Faktura korygująca KOR/1/11/2026", subject);
        Assert.Contains("Korekta dotyczy faktury FV/7/11/2026", body);
        Assert.Contains("Kwota do zwrotu: 246,00 PLN", body);
    }
}

/// <summary>Wysyłka faktury e-mailem: kolejka, załączniki, ponawianie, limity, izolacja.</summary>
public class EmailApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string NewAddress() => $"klient_{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task Wysylka_TrafiaDoKolejki_IJestWysylanaZPdfIXml()
    {
        var client = await factory.CreateClientWithKsefAsync();
        var invoice = await factory.AcceptedInvoiceAsync(client);
        var to = NewAddress();

        var response = await client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/email",
            new SendInvoiceEmailRequest { To = to, Message = "W razie pytań proszę o kontakt." });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(EmailStatus.Pending, (await response.Content.ReadFromJsonAsync<EmailMessageDto>())!.Status);
        Assert.Empty(factory.Email.To(to)); // wysyła dopiero usługa w tle

        await factory.ProcessEmailsAsync();

        var mail = Assert.Single(factory.Email.To(to));
        Assert.Equal("Moja Firma sp. z o.o.", mail.SenderName);
        Assert.Equal("biuro@mojafirma.pl", mail.ReplyTo);
        Assert.Contains(invoice.Number, mail.Subject);
        Assert.Contains("W razie pytań proszę o kontakt.", mail.Body);
        Assert.Equal(["application/pdf", "application/xml"], mail.Attachments!.Select(a => a.ContentType));
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(mail.Attachments![0].Content[..4]));
        // XML w załączniku to dokładnie dokument wysłany do KSeF
        Assert.Equal(await client.GetStringAsync($"/api/invoices/{invoice.Id}/xml"), Encoding.UTF8.GetString(mail.Attachments[1].Content));

        var history = (await client.GetFromJsonAsync<List<EmailMessageDto>>($"/api/invoices/{invoice.Id}/emails"))!;
        var sent = Assert.Single(history);
        Assert.Equal(EmailStatus.Sent, sent.Status);
        Assert.Equal(to, sent.To);
        Assert.NotNull(sent.SentAt);
    }

    [Fact]
    public async Task Szkic_NiePrawidlowyAdres_SaOdrzucane()
    {
        var client = await factory.CreateClientWithKsefAsync();
        var draft = (await (await client.PostAsJsonAsync("/api/invoices", TestUsers.SampleInvoice())).Content.ReadFromJsonAsync<InvoiceDto>())!;

        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync($"/api/invoices/{draft.Id}/email", new SendInvoiceEmailRequest { To = NewAddress() })).StatusCode);

        var invoice = await factory.AcceptedInvoiceAsync(client);
        var invalid = await client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/email", new SendInvoiceEmailRequest { To = "to-nie-jest-adres" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("To", await invalid.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AwariaSerwera_PonawiaWysylke_APoWyczerpaniuProbOznaczaBlad()
    {
        var client = await factory.CreateClientWithKsefAsync();
        var invoice = await factory.AcceptedInvoiceAsync(client);
        var to = NewAddress();
        (await client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/email", new SendInvoiceEmailRequest { To = to })).EnsureSuccessStatusCode();

        factory.Email.Failure = new IOException("Serwer SMTP niedostępny");
        try
        {
            await factory.ProcessEmailsAsync();
            var afterFirst = (await client.GetFromJsonAsync<List<EmailMessageDto>>($"/api/invoices/{invoice.Id}/emails"))!.Single();
            Assert.Equal(EmailStatus.Pending, afterFirst.Status);
            Assert.Equal(1, afterFirst.Attempts);
            Assert.Contains("SMTP", afterFirst.LastError);

            // Kolejna próba dopiero po odstępie - natychmiastowy przebieg nie wysyła ponownie
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var message = await db.EmailMessages.SingleAsync(m => m.InvoiceId == invoice.Id);
                Assert.True(message.NextAttemptAt > DateTime.UtcNow);
            }

            // Wyczerpanie wszystkich prób (przesuwamy czas następnej próby na „teraz”)
            for (var i = 0; i < EmailOutbox.RetryDelays.Length; i++)
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.EmailMessages.Where(m => m.InvoiceId == invoice.Id)
                    .ExecuteUpdateAsync(u => u.SetProperty(m => m.NextAttemptAt, DateTime.UtcNow.AddMinutes(-1)));
                await factory.ProcessEmailsAsync();
            }
        }
        finally
        {
            factory.Email.Failure = null;
        }

        var final = (await client.GetFromJsonAsync<List<EmailMessageDto>>($"/api/invoices/{invoice.Id}/emails"))!.Single();
        Assert.Equal(EmailStatus.Failed, final.Status);
        Assert.Equal(EmailOutbox.RetryDelays.Length + 1, final.Attempts);
        Assert.Empty(factory.Email.To(to));
    }

    [Fact]
    public async Task DziennyLimitFirmy_ChroniPrzedRozsylaniemSpamu()
    {
        var client = await factory.CreateClientWithKsefAsync();
        var invoice = await factory.AcceptedInvoiceAsync(client);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var companyId = (await db.Invoices.SingleAsync(i => i.Id == invoice.Id)).CompanyId;
            db.EmailMessages.AddRange(Enumerable.Range(0, 100).Select(_ => new EmailMessage
            {
                CompanyId = companyId, Kind = EmailKind.Invoice, To = "x@example.com", Subject = "s", Body = "b",
                Status = EmailStatus.Sent, CreatedAt = DateTime.UtcNow.AddHours(-1), NextAttemptAt = DateTime.UtcNow
            }));
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/email", new SendInvoiceEmailRequest { To = NewAddress() });
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task UzytkownikB_NieWysylaINieWidziWiadomosciFakturyA()
    {
        var clientA = await factory.CreateClientWithKsefAsync();
        var invoiceA = await factory.AcceptedInvoiceAsync(clientA);
        (await clientA.PostAsJsonAsync($"/api/invoices/{invoiceA.Id}/email", new SendInvoiceEmailRequest { To = NewAddress() })).EnsureSuccessStatusCode();
        var clientB = await factory.CreateClientWithKsefAsync();

        Assert.Equal(HttpStatusCode.NotFound,
            (await clientB.PostAsJsonAsync($"/api/invoices/{invoiceA.Id}/email", new SendInvoiceEmailRequest { To = NewAddress() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/invoices/{invoiceA.Id}/emails")).StatusCode);
    }
}
