using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Models;
using Microsoft.EntityFrameworkCore;

namespace FakturowanieKSeF.Api.Endpoints;

/// <summary>
/// Książka adresowa kontrahentów. Każde zapytanie jest zawężone do firmy zalogowanego użytkownika -
/// cudzy kontrahent jest traktowany jak nieistniejący (404), żeby nie ujawniać nawet jego istnienia.
/// </summary>
public static class CustomerEndpoints
{
    public static void MapCustomerEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/customers").WithTags("Kontrahenci").RequireAuthorization();

        group.MapGet("/", async (string? q, CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var query = db.Customers.AsNoTracking().Where(c => c.CompanyId == company.Id);
                if (!string.IsNullOrWhiteSpace(q))
                {
                    var pattern = $"%{q.Trim()}%";
                    query = query.Where(c => EF.Functions.ILike(c.Name, pattern) || EF.Functions.ILike(c.Nip!, pattern));
                }

                var customers = await query.OrderBy(c => c.Name).Take(500).ToListAsync(ct);
                return Results.Ok(customers.Select(c => c.ToDto()));
            })
            .WithSummary("Lista kontrahentów (opcjonalnie wyszukiwanie po nazwie lub NIP)");

        group.MapGet("/{id:int}", async (int id, CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var customer = await db.Customers.AsNoTracking()
                    .SingleOrDefaultAsync(c => c.Id == id && c.CompanyId == company.Id, ct);
                return customer is null ? ApiErrors.NotFound("kontrahent") : Results.Ok(customer.ToDto());
            })
            .WithSummary("Szczegóły kontrahenta");

        group.MapPost("/", async (SaveCustomerRequest request, CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var customer = new Customer { CompanyId = company.Id };
                Apply(customer, request);
                db.Customers.Add(customer);
                await db.SaveChangesAsync(ct);

                return Results.Created($"/api/customers/{customer.Id}", customer.ToDto());
            })
            .WithSummary("Dodanie kontrahenta");

        group.MapPut("/{id:int}", async (int id, SaveCustomerRequest request, CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == id && c.CompanyId == company.Id, ct);
                if (customer is null) return ApiErrors.NotFound("kontrahent");

                // Zmiana kontrahenta nie wpływa na wystawione faktury - mają własną kopię danych nabywcy
                Apply(customer, request);
                await db.SaveChangesAsync(ct);
                return Results.Ok(customer.ToDto());
            })
            .WithSummary("Edycja kontrahenta");

        group.MapDelete("/{id:int}", async (int id, CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == id && c.CompanyId == company.Id, ct);
                if (customer is null) return ApiErrors.NotFound("kontrahent");

                db.Customers.Remove(customer); // miękkie usunięcie - faktury zachowują odwołanie i własną kopię danych
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .WithSummary("Usunięcie kontrahenta");
    }

    private static void Apply(Customer customer, SaveCustomerRequest request)
    {
        customer.Name = request.Name.Trim();
        customer.IsCompany = request.IsCompany;
        customer.Nip = Mapping.NormalizeNip(request.Nip);
        customer.Address = request.Address.ToEntity();
        customer.Email = Mapping.EmptyToNull(request.Email);
    }
}
