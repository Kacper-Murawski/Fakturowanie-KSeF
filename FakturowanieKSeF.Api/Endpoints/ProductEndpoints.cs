using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Models;
using Microsoft.EntityFrameworkCore;

namespace FakturowanieKSeF.Api.Endpoints;

/// <summary>
/// Katalog towarów i usług firmy (D35). Jak książka adresowa: zapytania zawężone do firmy zalogowanego użytkownika,
/// cudza pozycja = 404. Pozycje katalogu są kopiowane do faktury, więc ich zmiana i usunięcie nie wpływa na faktury.
/// </summary>
public static class ProductEndpoints
{
    public static void MapProductEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/products").WithTags("Katalog").RequireAuthorization()
            .RequireRoleForChanges(CompanyRoles.Issuers); // księgowa - tylko odczyt (D46)

        group.MapGet("/", async (string? q, CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var query = db.Products.AsNoTracking().Where(p => p.CompanyId == company.Id);
                if (!string.IsNullOrWhiteSpace(q))
                    query = query.Where(p => EF.Functions.ILike(p.Name, $"%{q.Trim()}%"));

                var products = await query.OrderBy(p => p.Name).Take(1000).ToListAsync(ct);
                return Results.Ok(products.Select(ToDto));
            })
            .WithSummary("Katalog towarów i usług (opcjonalnie wyszukiwanie po nazwie)");

        group.MapGet("/{id:int}", async (int id, CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.CompanyId == company.Id, ct);
                return product is null ? ApiErrors.NotFound("pozycja katalogu") : Results.Ok(ToDto(product));
            })
            .WithSummary("Pozycja katalogu");

        group.MapPost("/", async (SaveProductRequest request, CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var product = new Product { CompanyId = company.Id };
                Apply(product, request);
                db.Products.Add(product);
                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/products/{product.Id}", ToDto(product));
            })
            .WithSummary("Dodanie pozycji do katalogu");

        group.MapPut("/{id:int}", async (int id, SaveProductRequest request, CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id && p.CompanyId == company.Id, ct);
                if (product is null) return ApiErrors.NotFound("pozycja katalogu");

                Apply(product, request);
                await db.SaveChangesAsync(ct);
                return Results.Ok(ToDto(product));
            })
            .WithSummary("Edycja pozycji katalogu (nie zmienia wystawionych faktur)");

        group.MapDelete("/{id:int}", async (int id, CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id && p.CompanyId == company.Id, ct);
                if (product is null) return ApiErrors.NotFound("pozycja katalogu");

                db.Products.Remove(product); // miękkie usunięcie
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .WithSummary("Usunięcie pozycji z katalogu");
    }

    /// <summary>Cena bez zer końcowych ze skali kolumny (numeric(22,8)) - w formularzu „1234.5”, a nie „1234.50000000”.</summary>
    private static ProductDto ToDto(Product p) =>
        new(p.Id, p.Name, p.Unit, p.UnitNetPrice / 1.000000000000000000000000000000000m, p.VatRate);

    private static void Apply(Product product, SaveProductRequest request)
    {
        product.Name = request.Name.Trim();
        product.Unit = Mapping.EmptyToNull(request.Unit);
        product.UnitNetPrice = request.UnitNetPrice;
        product.VatRate = request.VatRate;
    }
}
