using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Api.Services.Email;
using FakturowanieKSeF.Contracts.Dtos;
using Microsoft.EntityFrameworkCore;

namespace FakturowanieKSeF.Api.Endpoints;

/// <summary>Wysyłka faktur e-mailem (D36): wiadomość trafia do kolejki, wysyła ją usługa w tle.</summary>
public static class EmailEndpoints
{
    public static void MapEmailEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/invoices/{id:int}").WithTags("E-mail").RequireAuthorization();

        group.MapPost("/email", async (int id, SendInvoiceEmailRequest request, CurrentCompany current, AppDbContext db,
                EmailOutbox outbox, EmailWorkSignal signal, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var invoice = await db.Invoices.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id && i.CompanyId == company.Id, ct);
                if (invoice is null) return ApiErrors.NotFound("faktura");
                if (invoice.Status != InvoiceStatus.Accepted)
                    return ApiErrors.Conflict("Fakturę można wysłać e-mailem po przyjęciu przez KSeF (z numerem KSeF).");
                if (!outbox.IsConfigured)
                    return ApiErrors.Conflict("Wysyłka e-mail nie jest skonfigurowana na serwerze.");

                var (subject, body) = EmailTemplates.Invoice(invoice, company, request.Message);
                try
                {
                    var message = await outbox.EnqueueAsync(company.Id, EmailKind.Invoice, request.To, subject, body, invoice.Id, ct);
                    await db.SaveChangesAsync(ct);
                    signal.Pulse();
                    return Results.Accepted($"/api/invoices/{id}/emails", EmailOutbox.ToDto(message));
                }
                catch (EmailLimitException ex)
                {
                    return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status429TooManyRequests);
                }
            })
            .WithSummary("Wysłanie faktury e-mailem (PDF i XML w załączniku); wysyłka asynchroniczna");

        group.MapGet("/emails", async (int id, CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();
                if (!await db.Invoices.AnyAsync(i => i.Id == id && i.CompanyId == company.Id, ct)) return ApiErrors.NotFound("faktura");

                var messages = await db.EmailMessages.AsNoTracking()
                    .Where(m => m.InvoiceId == id && m.CompanyId == company.Id)
                    .OrderByDescending(m => m.Id)
                    .ToListAsync(ct);
                return Results.Ok(messages.Select(EmailOutbox.ToDto));
            })
            .WithSummary("Historia wiadomości e-mail dotyczących faktury");
    }
}
