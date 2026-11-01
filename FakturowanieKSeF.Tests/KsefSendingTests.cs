using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Services;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Ksef;
using FakturowanieKSeF.Ksef.Fa3;
using FakturowanieKSeF.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FakturowanieKSeF.Tests;

/// <summary>Konfiguracja tokenu KSeF firmy: weryfikacja, szyfrowanie, generowanie tokenu testowego.</summary>
public class KsefSettingsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task PoprawnyToken_JestZapisywanyZaszyfrowany_INieWracaDoKlienta()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        Assert.False((await client.GetFromJsonAsync<KsefSettingsDto>("/api/company/ksef"))!.HasToken);

        var response = await client.PutAsJsonAsync("/api/company/ksef/token", new SaveKsefTokenRequest { Token = FakeKsefGateway.ValidToken });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(FakeKsefGateway.ValidToken, body); // token nigdy nie wraca do przeglądarki

        var settings = await client.GetFromJsonAsync<KsefSettingsDto>("/api/company/ksef");
        Assert.True(settings!.HasToken);
        Assert.Contains("Testowe", settings.Environment);

        // W bazie token jest zaszyfrowany i daje się odszyfrować tylko kluczem aplikacji
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Companies.Where(c => c.KsefTokenEncrypted != null).Select(c => c.KsefTokenEncrypted!).ToListAsync();
        Assert.DoesNotContain(FakeKsefGateway.ValidToken, stored);
        Assert.Contains(stored, s => scope.ServiceProvider.GetRequiredService<KsefTokenProtector>().Unprotect(s) == FakeKsefGateway.ValidToken);
    }

    [Fact]
    public async Task TokenOdrzuconyPrzezKsef_Zwraca400_INieJestZapisywany()
    {
        var client = await factory.CreateClientWithCompanyAsync();

        var response = await client.PutAsJsonAsync("/api/company/ksef/token", new SaveKsefTokenRequest { Token = "zly-token" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("odrzucił token", await response.Content.ReadAsStringAsync());
        Assert.False((await client.GetFromJsonAsync<KsefSettingsDto>("/api/company/ksef"))!.HasToken);
    }

    [Fact]
    public async Task TokenTestowy_JestGenerowany_AUsuniecieGoKasuje()
    {
        var client = await factory.CreateClientWithCompanyAsync();

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/company/ksef/test-token", null)).StatusCode);
        Assert.True((await client.GetFromJsonAsync<KsefSettingsDto>("/api/company/ksef"))!.HasToken);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/company/ksef/token")).StatusCode);
        Assert.False((await client.GetFromJsonAsync<KsefSettingsDto>("/api/company/ksef"))!.HasToken);
    }
}

/// <summary>Wysyłka faktur do KSeF: endpoint, maszyna stanów usługi w tle, odporność na błędy.</summary>
public class KsefSendingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<(HttpClient client, InvoiceDto invoice)> DraftWithTokenAsync()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        (await client.PostAsync("/api/company/ksef/test-token", null)).EnsureSuccessStatusCode();
        var invoice = await (await client.PostAsJsonAsync("/api/invoices", TestUsers.SampleInvoice())).Content.ReadFromJsonAsync<InvoiceDto>();
        return (client, invoice!);
    }

    private static async Task<InvoiceDto> SendAsync(HttpClient client, InvoiceDto invoice)
    {
        var response = await client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/send", new SendInvoiceRequest { Version = invoice.Version });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;
    }

    private static Task<InvoiceDto?> GetAsync(HttpClient client, int id) => client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{id}");

    private async Task ProcessUntilIdleAsync(int maxPasses = 5)
    {
        for (var i = 0; i < maxPasses && await factory.ProcessKsefAsync() > 0; i++) { }
    }

    [Fact]
    public async Task Wysylka_BezTokenuKsef_Zwraca409()
    {
        var client = await factory.CreateClientWithCompanyAsync();
        var invoice = await (await client.PostAsJsonAsync("/api/invoices", TestUsers.SampleInvoice())).Content.ReadFromJsonAsync<InvoiceDto>();

        var response = await client.PostAsJsonAsync($"/api/invoices/{invoice!.Id}/send", new SendInvoiceRequest { Version = invoice.Version });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("token KSeF", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PelnyPrzeplyw_Wysylka_NumerKsef_Upo()
    {
        var (client, draft) = await DraftWithTokenAsync();

        // 1. Wysłanie: faktura w toku, zapisany dokładnie wysyłany XML i jego skrót
        var pending = await SendAsync(client, draft);
        Assert.Equal(InvoiceStatus.Pending, pending.Status);
        Assert.NotNull(pending.LastSubmission);

        using (var scope = factory.Services.CreateScope())
        {
            var submission = await scope.ServiceProvider.GetRequiredService<AppDbContext>().KsefSubmissions.SingleAsync(s => s.InvoiceId == draft.Id);
            var bytes = Encoding.UTF8.GetBytes(submission.SentXml);
            Assert.True(new Fa3SchemaValidator().Validate(bytes).IsValid);
            Assert.Equal(Convert.ToBase64String(SHA256.HashData(bytes)), submission.InvoiceHash);
        }

        // Faktury w toku nie można edytować ani wysłać ponownie
        var again = await client.PostAsJsonAsync($"/api/invoices/{draft.Id}/send", new SendInvoiceRequest { Version = pending.Version });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        // 2. Usługa w tle: wysyłka -> status -> UPO
        await ProcessUntilIdleAsync();

        var accepted = (await GetAsync(client, draft.Id))!;
        Assert.Equal(InvoiceStatus.Accepted, accepted.Status);
        Assert.NotNull(accepted.KsefNumber);
        Assert.StartsWith("5260250274-", accepted.KsefNumber);
        Assert.NotNull(accepted.KsefAcceptedAt);
        Assert.True(accepted.LastSubmission!.HasUpo);
        using (var scope = factory.Services.CreateScope())
        {
            var session = await scope.ServiceProvider.GetRequiredService<AppDbContext>().KsefSubmissions
                .Where(s => s.InvoiceId == draft.Id).Select(s => s.SessionReferenceNumber).SingleAsync();
            Assert.Contains(session!, factory.Ksef.ClosedSessions); // sesja zamknięta po przetworzeniu (warunek wydania UPO)
        }

        // 3. UPO i wysłany XML do pobrania
        var upo = await client.GetAsync($"/api/invoices/{draft.Id}/upo");
        Assert.Equal(HttpStatusCode.OK, upo.StatusCode);
        Assert.Contains(accepted.KsefNumber!, await upo.Content.ReadAsStringAsync());

        var sentXml = await client.GetByteArrayAsync($"/api/invoices/{draft.Id}/xml");
        Assert.Equal(factory.Ksef.SentInvoices.Last().Xml, sentXml); // dokładnie ten dokument, który trafił do KSeF
    }

    [Fact]
    public async Task FakturaOdrzuconaPrzezKsef_PokazujeBledy_IMozeBycPoprawionaIWyslanaPonownie()
    {
        var (client, draft) = await DraftWithTokenAsync();
        factory.Ksef.StatusResponses.Enqueue(new KsefInvoiceStatus(150, "Przetwarzanie", [], null, null));
        factory.Ksef.StatusResponses.Enqueue(new KsefInvoiceStatus(450, "Błąd weryfikacji semantyki dokumentu faktury",
            ["Nieprawidłowy NIP nabywcy"], null, null));

        await SendAsync(client, draft);
        await ProcessUntilIdleAsync();

        var rejected = (await GetAsync(client, draft.Id))!;
        Assert.Equal(InvoiceStatus.Rejected, rejected.Status);
        Assert.Equal(450, rejected.LastSubmission!.StatusCode);
        Assert.Contains("Nieprawidłowy NIP nabywcy", rejected.LastSubmission.StatusDetails);
        Assert.Null(rejected.KsefNumber);

        // Poprawka i ponowna wysyłka - nowa próba, historia zachowana
        var edit = TestUsers.SampleInvoice();
        edit.Version = rejected.Version;
        var corrected = await (await client.PutAsJsonAsync($"/api/invoices/{draft.Id}", edit)).Content.ReadFromJsonAsync<InvoiceDto>();
        Assert.Equal(InvoiceStatus.Draft, corrected!.Status);

        await SendAsync(client, corrected);
        await ProcessUntilIdleAsync();

        Assert.Equal(InvoiceStatus.Accepted, (await GetAsync(client, draft.Id))!.Status);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<AppDbContext>().KsefSubmissions.CountAsync(s => s.InvoiceId == draft.Id));
    }

    [Fact]
    public async Task AwariaSieciPrzedWyslaniem_JestPonawiana_APoLimicieWysylkaPrzerwana()
    {
        var (client, draft) = await DraftWithTokenAsync();
        factory.Ksef.SendFailure = new HttpRequestException("Brak połączenia z KSeF");
        try
        {
            await SendAsync(client, draft);

            await factory.ProcessKsefAsync();
            var retrying = (await GetAsync(client, draft.Id))!;
            Assert.Equal(InvoiceStatus.Pending, retrying.Status);           // nadal w toku - będzie ponowione
            Assert.Equal(1, retrying.LastSubmission!.FailedAttempts);
            Assert.Contains("Brak połączenia", retrying.LastSubmission.LastError);

            for (var i = 1; i < KsefSubmissionProcessor.MaxSendAttempts; i++) await factory.ProcessKsefAsync();

            var aborted = (await GetAsync(client, draft.Id))!;
            Assert.Equal(InvoiceStatus.Rejected, aborted.Status);           // faktura na pewno nie trafiła do KSeF
            Assert.Contains("Nie wysłano", aborted.LastSubmission!.StatusDescription);
        }
        finally
        {
            factory.Ksef.SendFailure = null;
        }
    }

    [Fact]
    public async Task AwariaSieciPoWyslaniu_NigdyNieOznaczaFakturyJakoOdrzuconej()
    {
        var (client, draft) = await DraftWithTokenAsync();
        await SendAsync(client, draft);
        await factory.ProcessKsefAsync(); // faktura dotarła do KSeF (ma numer referencyjny)

        factory.Ksef.StatusFailure = new HttpRequestException("KSeF niedostępny");
        try
        {
            for (var i = 0; i < KsefSubmissionProcessor.MaxSendAttempts + 2; i++) await factory.ProcessKsefAsync();

            // KSeF mógł fakturę przyjąć - nie wolno jej oznaczyć jako odrzuconej (groziłoby to duplikatem)
            var invoice = (await GetAsync(client, draft.Id))!;
            Assert.Equal(InvoiceStatus.Pending, invoice.Status);
            Assert.True(invoice.LastSubmission!.FailedAttempts > KsefSubmissionProcessor.MaxSendAttempts);
        }
        finally
        {
            factory.Ksef.StatusFailure = null;
        }

        await ProcessUntilIdleAsync();
        Assert.Equal(InvoiceStatus.Accepted, (await GetAsync(client, draft.Id))!.Status);
    }

    [Fact]
    public async Task PodwojneKlikniecieWyslij_ZtaSamaWersja_TworzyJednaWysylke()
    {
        var (client, draft) = await DraftWithTokenAsync();

        var first = client.PostAsJsonAsync($"/api/invoices/{draft.Id}/send", new SendInvoiceRequest { Version = draft.Version });
        var second = client.PostAsJsonAsync($"/api/invoices/{draft.Id}/send", new SendInvoiceRequest { Version = draft.Version });
        var results = await Task.WhenAll(first, second);

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Accepted);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().KsefSubmissions.CountAsync(s => s.InvoiceId == draft.Id));
    }

    [Fact]
    public async Task UpoNiedostepne_DlaFakturyNiewyslanej_Zwraca404()
    {
        var (client, draft) = await DraftWithTokenAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/invoices/{draft.Id}/upo")).StatusCode);
    }

    [Fact]
    public async Task UzytkownikB_NieMozeWyslacAniPobracUpoFakturyUzytkownikaA()
    {
        var (_, draftA) = await DraftWithTokenAsync();
        var userB = await factory.CreateClientWithCompanyAsync();
        (await userB.PostAsync("/api/company/ksef/test-token", null)).EnsureSuccessStatusCode();

        var send = await userB.PostAsJsonAsync($"/api/invoices/{draftA.Id}/send", new SendInvoiceRequest { Version = draftA.Version });
        Assert.Equal(HttpStatusCode.NotFound, send.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await userB.GetAsync($"/api/invoices/{draftA.Id}/upo")).StatusCode);
    }
}
