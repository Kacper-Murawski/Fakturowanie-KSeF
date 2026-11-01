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
        var group = api.MapGroup("/company").WithTags("Firma").RequireAuthorization();

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

                company.Name = request.Name.Trim();
                company.Nip = Mapping.NormalizeNip(request.Nip)!;
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
