using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Api.Services;

namespace FakturowanieKSeF.Api.Endpoints;

/// <summary>
/// Eksport dla księgowej (D34): podsumowanie okresu, zestawienia CSV i paczka ZIP z XML i UPO.
/// Dane wyłącznie firmy zalogowanego użytkownika (<see cref="CurrentCompany"/>).
/// </summary>
public static class ExportEndpoints
{
    public static void MapExportEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/exports").WithTags("Eksport").RequireAuthorization();

        group.MapGet("/summary", async (DateOnly? from, DateOnly? to, CurrentCompany current, AccountingExport export, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();
                if (AccountingExport.Validate(from, to) is { } error) return ApiErrors.Validation(nameof(to), error);
                var (start, end) = (from!.Value, to!.Value);

                var sales = await export.SalesAsync(company.Id, start, end, ct);
                var purchases = await export.PurchasesAsync(company.Id, start, end, ct);
                return Results.Ok(AccountingExport.Summary(start, end, sales, purchases));
            })
            .WithSummary("Podsumowanie okresu: liczba i kwoty faktur sprzedaży i zakupu");

        group.MapGet("/sales.csv", async (DateOnly? from, DateOnly? to, CurrentCompany current, AccountingExport export, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();
                if (AccountingExport.Validate(from, to) is { } error) return ApiErrors.Validation(nameof(to), error);
                var (start, end) = (from!.Value, to!.Value);

                var csv = AccountingExport.SalesCsv(await export.SalesAsync(company.Id, start, end, ct));
                return Results.File(csv, "text/csv; charset=utf-8", $"sprzedaz_{AccountingExport.FileSuffix(start, end)}.csv");
            })
            .WithSummary("Zestawienie faktur sprzedaży przyjętych w KSeF (CSV, według daty wystawienia)");

        group.MapGet("/purchases.csv", async (DateOnly? from, DateOnly? to, CurrentCompany current, AccountingExport export, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();
                if (AccountingExport.Validate(from, to) is { } error) return ApiErrors.Validation(nameof(to), error);
                var (start, end) = (from!.Value, to!.Value);

                var csv = AccountingExport.PurchasesCsv(await export.PurchasesAsync(company.Id, start, end, ct));
                return Results.File(csv, "text/csv; charset=utf-8", $"zakupy_{AccountingExport.FileSuffix(start, end)}.csv");
            })
            .WithSummary("Zestawienie faktur otrzymanych z KSeF (CSV, według daty otrzymania)");

        group.MapGet("/package.zip", async (DateOnly? from, DateOnly? to, CurrentCompany current, AccountingExport export, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();
                if (AccountingExport.Validate(from, to) is { } error) return ApiErrors.Validation(nameof(to), error);
                var (start, end) = (from!.Value, to!.Value);

                var sales = await export.SalesAsync(company.Id, start, end, ct);
                var purchases = await export.PurchasesAsync(company.Id, start, end, ct);
                return Results.File(AccountingExport.Package(start, end, sales, purchases), "application/zip",
                    $"ksiegowosc_{AccountingExport.FileSuffix(start, end)}.zip");
            })
            .WithSummary("Paczka dla księgowej: zestawienia CSV, XML faktur i UPO (ZIP)");
    }
}
