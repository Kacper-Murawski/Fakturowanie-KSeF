using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Api.Services.Email;
using FakturowanieKSeF.Contracts.Dtos;
using Microsoft.EntityFrameworkCore;

namespace FakturowanieKSeF.Api.Endpoints;

/// <summary>Przypomnienia o płatności i ustawienia powiadomień e-mail (D37).</summary>
public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/invoices/{id:int}/reminder", async (int id, SendInvoiceEmailRequest request, CurrentCompany current,
                AppDbContext db, PaymentReminders reminders, EmailOutbox outbox, EmailWorkSignal signal, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var invoice = await db.Invoices.Include(i => i.Payments).Include(i => i.Corrections)
                    .AsSplitQuery()
                    .SingleOrDefaultAsync(i => i.Id == id && i.CompanyId == company.Id, ct);
                if (invoice is null) return ApiErrors.NotFound("faktura");
                if (!PaymentReminders.CanRemind(invoice))
                    return ApiErrors.Conflict("Przypomnienie można wysłać dla faktury VAT przyjętej w KSeF.");
                if (PaymentReminders.Remaining(invoice) <= 0)
                    return ApiErrors.Conflict("Faktura jest opłacona - przypomnienie nie jest potrzebne.");
                if (!outbox.IsConfigured)
                    return ApiErrors.Conflict("Wysyłka e-mail nie jest skonfigurowana na serwerze.");

                try
                {
                    var message = await reminders.EnqueueAsync(invoice, company, request.To, request.Message, ct);
                    await db.SaveChangesAsync(ct);
                    signal.Pulse();
                    return Results.Accepted($"/api/invoices/{id}/emails", EmailOutbox.ToDto(message));
                }
                catch (EmailLimitException ex)
                {
                    return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status429TooManyRequests);
                }
            })
            .WithTags("E-mail").RequireAuthorization()
            .WithSummary("Przypomnienie o płatności dla nabywcy (kwota pozostała do zapłaty, faktura w załączniku)");

        api.MapGet("/invoices/{id:int}/email-recipient", async (int id, CurrentCompany current, AppDbContext db,
                PaymentReminders reminders, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var invoice = await db.Invoices.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id && i.CompanyId == company.Id, ct);
                return invoice is null ? ApiErrors.NotFound("faktura") : Results.Ok(new { email = await reminders.RecipientAsync(invoice, ct) });
            })
            .WithTags("E-mail").RequireAuthorization()
            .WithSummary("Podpowiedź adresu nabywcy: e-mail kontrahenta albo ostatni adres, na który wysłano fakturę");

        var settings = api.MapGroup("/company/notifications").WithTags("Firma").RequireAuthorization();

        settings.MapGet("/", async (CurrentCompany current, EmailOutbox outbox, AppDbContext db, CancellationToken ct) =>
            await current.FindAsync(ct) is not { } company
                ? ApiErrors.CompanyRequired()
                : Results.Ok(await ToDtoAsync(company, outbox, db, ct)))
            .WithSummary("Ustawienia powiadomień e-mail");

        settings.MapPut("/", async (SaveNotificationSettingsRequest request, CurrentCompany current, AppDbContext db,
                EmailOutbox outbox, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                company.AutoRemindersEnabled = request.AutoReminders;
                company.ReminderDaysAfterDue = request.ReminderDaysAfterDue;
                company.NotifyReceivedInvoices = request.NotifyReceivedInvoices;
                company.NotificationEmail = Mapping.EmptyToNull(request.NotificationEmail);
                await db.SaveChangesAsync(ct);
                return Results.Ok(await ToDtoAsync(company, outbox, db, ct));
            })
            .WithSummary("Zmiana ustawień powiadomień e-mail");
    }

    private static async Task<NotificationSettingsDto> ToDtoAsync(FakturowanieKSeF.Models.Company company, EmailOutbox outbox,
        AppDbContext db, CancellationToken ct) =>
        new(company.AutoRemindersEnabled, company.ReminderDaysAfterDue, outbox.IsConfigured,
            company.NotifyReceivedInvoices, company.NotificationEmail,
            await db.Users.Where(u => u.Id == company.OwnerId).Select(u => u.Email).SingleOrDefaultAsync(ct));
}
