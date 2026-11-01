using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Api.Services;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Ksef;
using FakturowanieKSeF.Ksef.Fa3;
using Microsoft.EntityFrameworkCore;

namespace FakturowanieKSeF.Api.Endpoints;

/// <summary>
/// Faktury otrzymane (zakupowe) pobrane z KSeF. Wszystkie odczyty z lokalnej bazy (zalecenie MF - bez wywołań KSeF
/// przy przeglądaniu); kontakt z KSeF wyłącznie podczas synchronizacji.
/// </summary>
public static class ReceivedInvoiceEndpoints
{
    /// <summary>Minimalny odstęp między ręcznymi synchronizacjami (ochrona limitów API KSeF).</summary>
    public static readonly TimeSpan ManualSyncCooldown = TimeSpan.FromSeconds(60);

    public static void MapReceivedInvoiceEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/received-invoices").WithTags("Faktury otrzymane").RequireAuthorization();

        group.MapGet("/", async (string? q, DateOnly? from, DateOnly? to, int? page, int? pageSize,
                CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var query = db.ReceivedInvoices.AsNoTracking().Where(r => r.CompanyId == company.Id);
                if (from is not null) query = query.Where(r => r.IssueDate >= from);
                if (to is not null) query = query.Where(r => r.IssueDate <= to);
                if (!string.IsNullOrWhiteSpace(q))
                {
                    var pattern = $"%{q.Trim()}%";
                    query = query.Where(r => EF.Functions.ILike(r.InvoiceNumber, pattern) || EF.Functions.ILike(r.SellerName, pattern)
                                             || EF.Functions.ILike(r.SellerNip, pattern) || EF.Functions.ILike(r.KsefNumber, pattern));
                }

                var p = Math.Max(page ?? 1, 1);
                var size = Math.Clamp(pageSize ?? 20, 1, 100);
                var total = await query.CountAsync(ct);
                var items = await query
                    .OrderByDescending(r => r.KsefAcceptedAt).ThenByDescending(r => r.Id)
                    .Skip((p - 1) * size).Take(size)
                    .Select(r => new ReceivedInvoiceListItemDto(r.Id, r.KsefNumber, r.InvoiceNumber, r.IssueDate, r.SellerNip,
                        r.SellerName, r.TotalNet, r.TotalVat, r.TotalGross, r.Currency, r.KsefAcceptedAt))
                    .ToListAsync(ct);

                return Results.Ok(new PagedResult<ReceivedInvoiceListItemDto>(items, total, p, size));
            })
            .WithSummary("Lista faktur otrzymanych (z lokalnej bazy)");

        group.MapGet("/{id:int}", async (int id, CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var r = await db.ReceivedInvoices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.CompanyId == company.Id, ct);
                if (r is null) return ApiErrors.NotFound("faktura otrzymana");

                InvoiceDocumentDto? document = null;
                string? error = null;
                try
                {
                    document = Fa3InvoiceReader.Read(r.Xml);
                }
                catch (Exception ex) when (ex is FormatException or System.Xml.XmlException)
                {
                    error = "Nie udało się odczytać treści faktury: " + ex.Message;
                }

                return Results.Ok(new ReceivedInvoiceDto(r.Id, r.KsefNumber, r.KsefAcceptedAt, r.DownloadedAt, document, error));
            })
            .WithSummary("Szczegóły faktury otrzymanej (treść odczytana z XML)");

        group.MapGet("/{id:int}/xml", async (int id, CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var r = await db.ReceivedInvoices.AsNoTracking().Where(x => x.Id == id && x.CompanyId == company.Id)
                    .Select(x => new { x.Xml, x.KsefNumber }).SingleOrDefaultAsync(ct);
                return r is null
                    ? ApiErrors.NotFound("faktura otrzymana")
                    : Results.File(System.Text.Encoding.UTF8.GetBytes(r.Xml), "application/xml", $"{r.KsefNumber}.xml");
            })
            .WithSummary("XML faktury otrzymanej (w postaci pobranej z KSeF)");

        group.MapGet("/{id:int}/pdf", async (int id, CurrentCompany current, AppDbContext db, InvoicePdfRenderer renderer, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var r = await db.ReceivedInvoices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.CompanyId == company.Id, ct);
                if (r is null) return ApiErrors.NotFound("faktura otrzymana");

                InvoiceDocumentDto document;
                try
                {
                    document = Fa3InvoiceReader.Read(r.Xml);
                }
                catch (Exception ex) when (ex is FormatException or System.Xml.XmlException)
                {
                    return Results.Problem(title: "Nie można odczytać treści faktury.", detail: ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
                }

                // Skrót pliku z metadanych KSeF; gdy go brak - liczony z pobranego XML
                var hash = r.InvoiceHash ?? Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(r.Xml)));
                var qr = new PdfVerificationQr(KsefQr.InvoiceVerificationUrl(r.SellerNip, document.IssueDate, hash), r.KsefNumber);

                return Results.File(renderer.Render(document, qr, null), "application/pdf", $"{r.KsefNumber}.pdf");
            })
            .WithSummary("Wizualizacja faktury otrzymanej w PDF (z kodem QR weryfikacji KSeF)");

        group.MapGet("/sync-status", async (CurrentCompany current, CancellationToken ct) =>
                await current.FindAsync(ct) is { } c
                    ? Results.Ok(new ReceivedSyncStatusDto(c.KsefTokenEncrypted is not null, c.ReceivedInvoicesLastSyncAt,
                        c.ReceivedInvoicesSyncedUntil, c.ReceivedInvoicesLastSyncError))
                    : ApiErrors.CompanyRequired())
            .WithSummary("Stan synchronizacji faktur otrzymanych");

        group.MapPost("/sync", async (CurrentCompany current, ReceivedInvoiceSync sync, TimeProvider time, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                if (company.KsefTokenEncrypted is null)
                    return ApiErrors.Conflict("Najpierw skonfiguruj token KSeF w ustawieniach firmy.");

                if (company.ReceivedInvoicesLastSyncAt is { } last && time.GetUtcNow().UtcDateTime - last < ManualSyncCooldown)
                    return Results.Problem(title: "Synchronizacja była uruchomiona przed chwilą. Spróbuj ponownie za minutę.",
                        statusCode: StatusCodes.Status429TooManyRequests);

                var result = await sync.SyncAsync(company, ct);
                return company.ReceivedInvoicesLastSyncError is { } error
                    ? Results.Problem(title: "Synchronizacja z KSeF nie powiodła się.", detail: error, statusCode: StatusCodes.Status502BadGateway)
                    : Results.Ok(result);
            })
            .WithSummary("Ręczna synchronizacja faktur otrzymanych z KSeF (nie częściej niż raz na minutę)");
    }
}
