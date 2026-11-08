using Microsoft.EntityFrameworkCore;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Models;

namespace FakturowanieKSeF.Api.Endpoints;

/// <summary>Profil firmy zalogowanego użytkownika (1 konto = 1 firma, D4).</summary>
public static class CompanyEndpoints
{
    public static void MapCompanyEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/company").WithTags("Firma").RequireAuthorization()
            .RequireRoleForChanges(CompanyRole.Owner); // dane firmy zmienia tylko właściciel (D46)

        group.MapGet("/", async (CurrentCompany current, CancellationToken ct) =>
                await current.FindAsync(ct) is { } company
                    ? Results.Ok(company.ToDto())
                    : ApiErrors.NotFound("firma (jeszcze nie została utworzona)"))
            .WithSummary("Dane firmy zalogowanego użytkownika");

        group.MapPut("/", async (SaveCompanyRequest request, CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                var company = await current.FindAsync(ct);
                var created = company is null;

                if (company is null)
                {
                    company = new Company { OwnerId = current.UserId };
                    db.Companies.Add(company);
                }

                var nip = Mapping.NormalizeNip(request.Nip)!;
                if (!created && nip != company.Nip)
                {
                    // NIP to tożsamość firmy w KSeF (kontekst tokenu, sprzedawca na fakturach, nabywca faktur otrzymanych).
                    // Po pierwszej wymianie dokumentów z KSeF zmiana NIP pomieszałaby dane dwóch podmiotów (D51).
                    var usedInKsef = await db.Invoices.AnyAsync(i => i.CompanyId == company.Id && i.KsefSubmissions.Any(), ct)
                                     || await db.ReceivedInvoices.AnyAsync(r => r.CompanyId == company.Id, ct);
                    if (usedInKsef)
                        return ApiErrors.Conflict("NIP firmy nie można zmienić po wysłaniu lub pobraniu faktur z KSeF. Dla innego NIP załóż nową firmę.");

                    // Token KSeF jest wydany dla konkretnego NIP - po zmianie NIP trzeba wygenerować nowy
                    company.KsefTokenEncrypted = null;
                    company.KsefTokenUpdatedAt = null;
                    company.ReceivedInvoicesSyncedUntil = null;
                    company.ReceivedInvoicesLastSyncAt = null;
                    company.ReceivedInvoicesLastSyncError = null;
                    company.IssuedInvoicesSyncedUntil = null;
                    company.IssuedInvoicesLastSyncAt = null;
                    company.IssuedInvoicesLastSyncError = null;
                }

                company.Name = request.Name.Trim();
                company.Nip = nip;
                company.Address = request.Address.ToEntity();
                company.Email = Mapping.EmptyToNull(request.Email);
                company.Phone = Mapping.EmptyToNull(request.Phone);
                company.BankAccountNumber = Mapping.NormalizeBankAccount(request.BankAccountNumber);

                await db.SaveChangesAsync(ct);
                current.Set(company);

                return created
                    ? Results.Created("/api/company", company.ToDto())
                    : Results.Ok(company.ToDto());
            })
            .WithSummary("Utworzenie lub aktualizacja danych firmy");
    }
}
