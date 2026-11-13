using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FakturowanieKSeF.Tests;

/// <summary>Dostęp pracownika i księgowej do firmy (D46): zaproszenia, uprawnienia ról, wybór firmy, izolacja.</summary>
public class CompanyRoleTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record LoginResponse(string AccessToken);

    private async Task<HttpClient> UserAsync(string email)
    {
        var client = factory.CreateClient();
        await factory.RegisterConfirmedAsync(client, email);
        var login = await (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Haslo123!" })).Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    private static string NewEmail(string prefix) => $"{prefix}_{Guid.NewGuid():N}@example.com";

    private static async Task<InvitationCreatedDto> InviteAsync(HttpClient owner, string email, CompanyRole role)
    {
        var response = await owner.PostAsJsonAsync("/api/company/members/invitations", new InviteMemberRequest { Email = email, Role = role });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<InvitationCreatedDto>())!;
    }

    private static string Token(InvitationCreatedDto invitation) => invitation.Link[(invitation.Link.IndexOf("token=", StringComparison.Ordinal) + 6)..];

    /// <summary>Właściciel z firmą (token KSeF, faktura przyjęta) i osoba z rolą, która przyjęła zaproszenie.</summary>
    private async Task<(HttpClient Owner, HttpClient Member, InvoiceDto Invoice)> TeamAsync(CompanyRole role)
    {
        var owner = await factory.CreateClientWithKsefAsync();
        var invoice = await factory.AcceptedInvoiceAsync(owner);
        var email = NewEmail(role.ToString().ToLowerInvariant());
        var invitation = await InviteAsync(owner, email, role);
        var member = await UserAsync(email);
        (await member.PostAsJsonAsync("/api/account/invitations/accept", new AcceptInvitationRequest { Token = Token(invitation) })).EnsureSuccessStatusCode();
        return (owner, member, invoice);
    }

    [Fact]
    public async Task Ksiegowa_CzytaIEksportuje_RejestrujePlatnosci_AleNieWystawiaINieZmieniaFirmy()
    {
        var (_, accountant, invoice) = await TeamAsync(CompanyRole.Accountant);

        Assert.Equal(invoice.Number, (await accountant.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices"))!.Items.Single().Number);
        Assert.Equal(HttpStatusCode.OK, (await accountant.GetAsync($"/api/invoices/{invoice.Id}/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await accountant.GetAsync("/api/exports/package.zip?from=2026-11-01&to=2026-11-30")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await accountant.PostAsJsonAsync($"/api/invoices/{invoice.Id}/payments",
            new AddPaymentRequest { PaidOn = new DateOnly(2026, 11, 2), Amount = 100m })).StatusCode);

        var create = await accountant.PostAsJsonAsync("/api/invoices", TestUsers.SampleInvoice());
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Contains("księgowa", await create.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.PutAsJsonAsync("/api/company", TestUsers.SampleCompany())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.DeleteAsync("/api/company/ksef/token")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.PostAsJsonAsync("/api/customers", TestUsers.SampleCustomer())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.PostAsJsonAsync($"/api/invoices/{invoice.Id}/email",
            new SendInvoiceEmailRequest { To = "x@example.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.GetAsync("/api/company/members")).StatusCode);
    }

    [Fact]
    public async Task Pracownik_WystawiaIWysylaDoKsef_AleNieZmieniaUstawienFirmyIUzytkownikow()
    {
        var (owner, employee, _) = await TeamAsync(CompanyRole.Employee);

        var draft = (await (await employee.PostAsJsonAsync("/api/invoices", TestUsers.SampleInvoice())).Content.ReadFromJsonAsync<InvoiceDto>())!;
        (await employee.PostAsJsonAsync($"/api/invoices/{draft.Id}/send", new SendInvoiceRequest { Version = draft.Version })).EnsureSuccessStatusCode();
        for (var i = 0; i < 5 && await factory.ProcessKsefAsync() > 0; i++) { }
        Assert.Equal(InvoiceStatus.Accepted, (await owner.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{draft.Id}"))!.Status);

        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PutAsJsonAsync("/api/company", TestUsers.SampleCompany())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PutAsJsonAsync("/api/company/notifications",
            new SaveNotificationSettingsRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PostAsJsonAsync("/api/company/members/invitations",
            new InviteMemberRequest { Email = NewEmail("x"), Role = CompanyRole.Employee })).StatusCode);
    }

    [Fact]
    public async Task Zaproszenie_WymagaTokenuIAdresu_JestJednorazowe_WygasaIMoznaJeOdwolac()
    {
        var owner = await factory.CreateClientWithCompanyAsync();
        var email = NewEmail("ksiegowa");
        var invitation = await InviteAsync(owner, email, CompanyRole.Accountant);
        Assert.True(invitation.EmailSent); // e-mail w kolejce (poczta skonfigurowana w testach)

        // Inne konto z linkiem - odmowa (dowód: dostęp do zapraszanej skrzynki i konto z tym adresem)
        var stranger = await UserAsync(NewEmail("obcy"));
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsJsonAsync("/api/account/invitations/accept",
            new AcceptInvitationRequest { Token = Token(invitation) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync("/api/account/invitations/accept",
            new AcceptInvitationRequest { Token = "zgadywany-token" })).StatusCode);

        var accountant = await UserAsync(email);
        // Token we fragmencie linku i w treści żądania - nie w adresie (logi proxy, SEC-9)
        Assert.Contains("/zaproszenie#token=", invitation.Link);
        Assert.Equal(HttpStatusCode.NotFound, (await accountant.GetAsync($"/api/account/invitations/{Token(invitation)}")).StatusCode);
        var preview = (await (await accountant.PostAsJsonAsync("/api/account/invitations/preview",
            new AcceptInvitationRequest { Token = Token(invitation) })).Content.ReadFromJsonAsync<InvitationPreviewDto>())!;
        Assert.Equal(("Moja Firma sp. z o.o.", CompanyRole.Accountant), (preview.CompanyName, preview.Role));
        (await accountant.PostAsJsonAsync("/api/account/invitations/accept", new AcceptInvitationRequest { Token = Token(invitation) })).EnsureSuccessStatusCode();
        // Jednorazowe
        Assert.Equal(HttpStatusCode.NotFound, (await accountant.PostAsJsonAsync("/api/account/invitations/accept",
            new AcceptInvitationRequest { Token = Token(invitation) })).StatusCode);

        // Odwołane i wygasłe - nieważne
        var revokedEmail = NewEmail("odwolane");
        var revoked = await InviteAsync(owner, revokedEmail, CompanyRole.Employee);
        (await owner.DeleteAsync($"/api/company/members/invitations/{revoked.Invitation.Id}")).EnsureSuccessStatusCode();
        var expiredEmail = NewEmail("wygasle");
        var expired = await InviteAsync(owner, expiredEmail, CompanyRole.Employee);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.CompanyInvitations.Where(i => i.Id == expired.Invitation.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(i => i.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
            Assert.DoesNotContain(await db.CompanyInvitations.Select(i => i.TokenHash).ToListAsync(), h => h == Token(invitation)); // tylko skrót
        }
        Assert.Equal(HttpStatusCode.NotFound, (await (await UserAsync(revokedEmail)).PostAsJsonAsync("/api/account/invitations/accept",
            new AcceptInvitationRequest { Token = Token(revoked) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await UserAsync(expiredEmail)).PostAsJsonAsync("/api/account/invitations/accept",
            new AcceptInvitationRequest { Token = Token(expired) })).StatusCode);

        // Lista: właściciel i księgowa; nieważnych zaproszeń brak
        var members = (await owner.GetFromJsonAsync<CompanyMembersDto>("/api/company/members"))!;
        Assert.Equal([CompanyRole.Owner, CompanyRole.Accountant], members.Members.Select(m => m.Role));
        Assert.Empty(members.Invitations);

        // Walidacja: właściciel jest jeden
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/company/members/invitations",
            new InviteMemberRequest { Email = NewEmail("x"), Role = CompanyRole.Owner })).StatusCode);
    }

    [Fact]
    public async Task WyborFirmy_TylkoSposrodDostepnych_ObcyIdentyfikatorNieDajeDostepu()
    {
        var (ownerA, accountant, invoiceA) = await TeamAsync(CompanyRole.Accountant);
        var ownerB = await factory.CreateClientWithKsefAsync();
        var invoiceB = await factory.AcceptedInvoiceAsync(ownerB);
        int companyB;
        using (var scope = factory.Services.CreateScope())
            companyB = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Invoices.SingleAsync(i => i.Id == invoiceB.Id)).CompanyId;

        // Księgowa firmy A wskazuje firmę B nagłówkiem - wskazanie ignorowane, nadal tylko dane A
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/invoices");
        request.Headers.Add(CurrentCompany.HeaderName, companyB.ToString());
        var list = (await (await accountant.SendAsync(request)).Content.ReadFromJsonAsync<PagedResult<InvoiceListItemDto>>())!;
        Assert.DoesNotContain(list.Items, i => i.Id == invoiceB.Id);
        Assert.Contains(list.Items, i => i.Id == invoiceA.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await accountant.PostAsJsonAsync("/api/account/active-company",
            new SelectCompanyRequest { CompanyId = companyB })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await accountant.GetAsync($"/api/invoices/{invoiceB.Id}")).StatusCode);
    }

    [Fact]
    public async Task BiuroRachunkowe_WlasnaFirmaIFirmaKlienta_PrzelaczanieIUprawnieniaWKazdej()
    {
        var ownerA = await factory.CreateClientWithKsefAsync();
        var invoiceA = await factory.AcceptedInvoiceAsync(ownerA);
        var email = NewEmail("biuro");
        var invitation = await InviteAsync(ownerA, email, CompanyRole.Accountant);
        var office = await UserAsync(email);
        (await office.PutAsJsonAsync("/api/company", TestUsers.SampleCompany(TestUsers.RandomNip()))).EnsureSuccessStatusCode();
        (await office.PostAsJsonAsync("/api/account/invitations/accept", new AcceptInvitationRequest { Token = Token(invitation) })).EnsureSuccessStatusCode();

        var companies = (await office.GetFromJsonAsync<List<AccessibleCompanyDto>>("/api/account/companies"))!;
        Assert.Equal(2, companies.Count);
        var own = companies.Single(c => c.Role == CompanyRole.Owner);
        var client = companies.Single(c => c.Role == CompanyRole.Accountant);
        Assert.True(client.Active); // po przyjęciu zaproszenia - firma klienta

        Assert.Contains((await office.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices"))!.Items, i => i.Id == invoiceA.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await office.PostAsJsonAsync("/api/invoices", TestUsers.SampleInvoice())).StatusCode);

        // Przełączenie na własną firmę: pełne uprawnienia, brak faktur klienta
        (await office.PostAsJsonAsync("/api/account/active-company", new SelectCompanyRequest { CompanyId = own.Id })).EnsureSuccessStatusCode();
        Assert.Equal(0, (await office.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices"))!.TotalCount);
        Assert.Equal(HttpStatusCode.Created, (await office.PostAsJsonAsync("/api/invoices", TestUsers.SampleInvoice())).StatusCode);
    }

    [Fact]
    public async Task OdebranieDostepu_KonczyDostepDoDanych()
    {
        var (owner, employee, invoice) = await TeamAsync(CompanyRole.Employee);
        var member = (await owner.GetFromJsonAsync<CompanyMembersDto>("/api/company/members"))!.Members.Single(m => m.MemberId is not null);

        (await owner.PutAsJsonAsync($"/api/company/members/{member.MemberId}", new ChangeMemberRoleRequest { Role = CompanyRole.Accountant })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PostAsJsonAsync("/api/invoices", TestUsers.SampleInvoice())).StatusCode);

        (await owner.DeleteAsync($"/api/company/members/{member.MemberId}")).EnsureSuccessStatusCode();
        // Bez żadnej firmy - 409 „utwórz firmę”, żadnych danych firmy A
        var details = await employee.GetAsync($"/api/invoices/{invoice.Id}");
        Assert.Equal(HttpStatusCode.Conflict, details.StatusCode);
        Assert.DoesNotContain(invoice.Number, await details.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await employee.GetAsync("/api/invoices")).StatusCode);
    }
}
