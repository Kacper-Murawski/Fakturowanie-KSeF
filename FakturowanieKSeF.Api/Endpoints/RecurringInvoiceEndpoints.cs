using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Api.Services;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Models;
using Microsoft.EntityFrameworkCore;

namespace FakturowanieKSeF.Api.Endpoints;

/// <summary>Harmonogramy faktur cyklicznych (D39); izolacja per firma - cudzy harmonogram lub wzór = 404.</summary>
public static class RecurringInvoiceEndpoints
{
    public static void MapRecurringInvoiceEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/recurring-invoices").WithTags("Faktury cykliczne").RequireAuthorization();

        group.MapGet("/", async (CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var list = await db.RecurringInvoices.AsNoTracking().Include(r => r.TemplateInvoice)
                    .Where(r => r.CompanyId == company.Id)
                    .OrderByDescending(r => r.IsActive).ThenBy(r => r.NextIssueDate).ThenBy(r => r.Id)
                    .ToListAsync(ct);
                return Results.Ok(list.Select(RecurringInvoices.ToDto));
            })
            .WithSummary("Lista harmonogramów faktur cyklicznych");

        group.MapGet("/{id:int}", async (int id, CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var r = await db.RecurringInvoices.AsNoTracking().Include(x => x.TemplateInvoice)
                    .SingleOrDefaultAsync(x => x.Id == id && x.CompanyId == company.Id, ct);
                return r is null ? ApiErrors.NotFound("faktura cykliczna") : Results.Ok(RecurringInvoices.ToDto(r));
            })
            .WithSummary("Harmonogram faktury cyklicznej");

        group.MapPost("/", async (SaveRecurringInvoiceRequest request, CurrentCompany current, AppDbContext db, TimeProvider time,
                CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var template = await db.Invoices.SingleOrDefaultAsync(i => i.Id == request.TemplateInvoiceId && i.CompanyId == company.Id, ct);
                if (template is null) return ApiErrors.Validation(nameof(request.TemplateInvoiceId), "Nie znaleziono faktury-wzoru.");
                if (template.Kind != InvoiceKind.Vat)
                    return ApiErrors.Validation(nameof(request.TemplateInvoiceId), "Wzorem faktury cyklicznej może być tylko faktura VAT (nie korekta).");
                if (request.NextIssueDate < PolishDate.Today(time))
                    return ApiErrors.Validation(nameof(request.NextIssueDate), "Data pierwszego wystawienia nie może być w przeszłości.");

                var schedule = new RecurringInvoice { CompanyId = company.Id, TemplateInvoiceId = template.Id, TemplateInvoice = template };
                Apply(schedule, request);
                db.RecurringInvoices.Add(schedule);
                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/recurring-invoices/{schedule.Id}", RecurringInvoices.ToDto(schedule));
            })
            .WithSummary("Utworzenie harmonogramu faktury cyklicznej na wzór faktury VAT");

        group.MapPut("/{id:int}", async (int id, SaveRecurringInvoiceRequest request, CurrentCompany current, AppDbContext db,
                TimeProvider time, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var schedule = await db.RecurringInvoices.Include(r => r.TemplateInvoice)
                    .SingleOrDefaultAsync(r => r.Id == id && r.CompanyId == company.Id, ct);
                if (schedule is null) return ApiErrors.NotFound("faktura cykliczna");
                if (request.NextIssueDate != schedule.NextIssueDate && request.NextIssueDate < PolishDate.Today(time))
                    return ApiErrors.Validation(nameof(request.NextIssueDate), "Data najbliższego wystawienia nie może być w przeszłości.");

                Apply(schedule, request);
                if (schedule.IsActive) schedule.LastError = null; // wznowienie po wstrzymaniu
                await db.SaveChangesAsync(ct);
                return Results.Ok(RecurringInvoices.ToDto(schedule));
            })
            .WithSummary("Zmiana harmonogramu (okres, daty, wstrzymanie i wznowienie); wzoru nie można zmienić");

        group.MapDelete("/{id:int}", async (int id, CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var schedule = await db.RecurringInvoices.SingleOrDefaultAsync(r => r.Id == id && r.CompanyId == company.Id, ct);
                if (schedule is null) return ApiErrors.NotFound("faktura cykliczna");

                db.RecurringInvoices.Remove(schedule); // miękkie usunięcie - utworzone faktury pozostają
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .WithSummary("Usunięcie harmonogramu (utworzone faktury pozostają)");
    }

    private static void Apply(RecurringInvoice schedule, SaveRecurringInvoiceRequest request)
    {
        schedule.Name = Mapping.EmptyToNull(request.Name);
        schedule.IntervalMonths = request.IntervalMonths;
        schedule.NextIssueDate = request.NextIssueDate!.Value;
        schedule.DayOfMonth = schedule.NextIssueDate.Day;
        schedule.EndDate = request.EndDate;
        schedule.IsActive = request.IsActive;
        schedule.NotifyByEmail = request.NotifyByEmail;
    }
}
