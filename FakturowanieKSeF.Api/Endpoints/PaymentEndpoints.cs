using System.Globalization;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Models;
using Microsoft.EntityFrameworkCore;

namespace FakturowanieKSeF.Api.Endpoints;

/// <summary>
/// Rozliczenia faktur: rejestrowanie wpłat od nabywców (faktury wystawione - należności) i zapłat sprzedawcom
/// (faktury otrzymane - zobowiązania). Płatność jest zawsze powiązana z fakturą firmy zalogowanego użytkownika;
/// cudza faktura lub płatność = 404.
/// </summary>
public static class PaymentEndpoints
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static void MapPaymentEndpoints(this RouteGroupBuilder api)
    {
        // ---------------- faktury wystawione ----------------
        var issued = api.MapGroup("/invoices/{id:int}/payments").WithTags("Płatności").RequireAuthorization();

        issued.MapPost("/", async (int id, AddPaymentRequest request, CurrentCompany current, AppDbContext db,
                TimeProvider time, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var invoice = await db.Invoices.Include(i => i.Payments).Include(i => i.Corrections)
                    .SingleOrDefaultAsync(i => i.Id == id && i.CompanyId == company.Id, ct);
                if (invoice is null) return ApiErrors.NotFound("faktura");
                if (invoice.Kind == InvoiceKind.Correction)
                    return ApiErrors.Conflict("Wpłaty rejestruj przy fakturze korygowanej - korekta zmienia jej kwotę należną.");
                if (!PaymentRules.AcceptsPayments(invoice.Status))
                    return ApiErrors.Conflict("Wpłaty można rejestrować dla faktur przekazanych do KSeF (nie dla szkiców i faktur odrzuconych).");

                var today = PolishDate.Today(time);
                if (Validate(request, invoice.TotalDue(), invoice.Payments, today) is { } error) return error;

                invoice.Payments.Add(NewPayment(company.Id, request, time));
                await db.SaveChangesAsync(ct);
                return Results.Ok(Mapping.ToPaymentInfo(invoice.TotalDue(), invoice.DueDate, invoice.Payments, today));
            })
            .WithSummary("Rejestracja wpłaty za fakturę wystawioną");

        issued.MapDelete("/{paymentId:int}", async (int id, int paymentId, CurrentCompany current, AppDbContext db,
                TimeProvider time, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var invoice = await db.Invoices.Include(i => i.Payments).Include(i => i.Corrections)
                    .SingleOrDefaultAsync(i => i.Id == id && i.CompanyId == company.Id, ct);
                var payment = invoice?.Payments.SingleOrDefault(p => p.Id == paymentId);
                if (invoice is null || payment is null) return ApiErrors.NotFound("płatność");

                db.Payments.Remove(payment);
                invoice.Payments.Remove(payment);
                await db.SaveChangesAsync(ct);
                return Results.Ok(Mapping.ToPaymentInfo(invoice.TotalDue(), invoice.DueDate, invoice.Payments, PolishDate.Today(time)));
            })
            .WithSummary("Usunięcie błędnie zarejestrowanej wpłaty");

        // ---------------- faktury otrzymane ----------------
        var received = api.MapGroup("/received-invoices/{id:int}/payments").WithTags("Płatności").RequireAuthorization();

        received.MapPost("/", async (int id, AddPaymentRequest request, CurrentCompany current, AppDbContext db,
                TimeProvider time, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var invoice = await db.ReceivedInvoices.Include(r => r.Payments)
                    .SingleOrDefaultAsync(r => r.Id == id && r.CompanyId == company.Id, ct);
                if (invoice is null) return ApiErrors.NotFound("faktura otrzymana");

                var today = PolishDate.Today(time);
                if (Validate(request, invoice.TotalGross, invoice.Payments, today) is { } error) return error;

                invoice.Payments.Add(NewPayment(company.Id, request, time));
                await db.SaveChangesAsync(ct);
                return Results.Ok(Mapping.ToPaymentInfo(invoice.TotalGross, invoice.DueDate, invoice.Payments, today));
            })
            .WithSummary("Rejestracja zapłaty za fakturę otrzymaną");

        received.MapDelete("/{paymentId:int}", async (int id, int paymentId, CurrentCompany current, AppDbContext db,
                TimeProvider time, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var invoice = await db.ReceivedInvoices.Include(r => r.Payments)
                    .SingleOrDefaultAsync(r => r.Id == id && r.CompanyId == company.Id, ct);
                var payment = invoice?.Payments.SingleOrDefault(p => p.Id == paymentId);
                if (invoice is null || payment is null) return ApiErrors.NotFound("płatność");

                db.Payments.Remove(payment);
                invoice.Payments.Remove(payment);
                await db.SaveChangesAsync(ct);
                return Results.Ok(Mapping.ToPaymentInfo(invoice.TotalGross, invoice.DueDate, invoice.Payments, PolishDate.Today(time)));
            })
            .WithSummary("Usunięcie błędnie zarejestrowanej zapłaty");

        // ---------------- podsumowanie ----------------
        api.MapGet("/payments/summary", async (CurrentCompany current, AppDbContext db, TimeProvider time, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();
                var today = PolishDate.Today(time);

                // Należność = brutto faktury VAT powiększone o korekty przekazane do KSeF
                var receivables = await db.Invoices.AsNoTracking()
                    .Where(i => i.CompanyId == company.Id && i.Kind == InvoiceKind.Vat
                                && (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.Accepted))
                    .Select(i => new
                    {
                        Gross = i.TotalGross + i.Corrections
                            .Where(c => c.Status == InvoiceStatus.Pending || c.Status == InvoiceStatus.Accepted).Sum(c => c.TotalGross),
                        Due = i.DueDate,
                        Paid = i.Payments.Sum(p => p.Amount)
                    })
                    .Where(x => x.Paid < x.Gross)
                    .Select(x => new Open(x.Gross, x.Due, x.Paid))
                    .ToListAsync(ct);

                // Zobowiązania sumowane tylko w PLN - faktury w walutach obcych nie są przeliczane
                var payables = await db.ReceivedInvoices.AsNoTracking()
                    .Where(r => r.CompanyId == company.Id && r.Currency == "PLN")
                    .Select(r => new { Gross = r.TotalGross, Due = r.DueDate, Paid = r.Payments.Sum(p => p.Amount) })
                    .Where(x => x.Paid < x.Gross)
                    .Select(x => new Open(x.Gross, x.Due, x.Paid))
                    .ToListAsync(ct);

                var overdueReceivables = receivables.Where(x => x.Due < today).ToList();
                var overduePayables = payables.Where(x => x.Due < today).ToList();
                return Results.Ok(new PaymentsSummaryDto(
                    receivables.Sum(x => x.Gross - x.Paid), receivables.Count,
                    overdueReceivables.Sum(x => x.Gross - x.Paid), overdueReceivables.Count,
                    payables.Sum(x => x.Gross - x.Paid), payables.Count,
                    overduePayables.Sum(x => x.Gross - x.Paid), overduePayables.Count));
            })
            .WithTags("Płatności")
            .RequireAuthorization()
            .WithSummary("Podsumowanie należności i zobowiązań (pozostało do zapłaty, w tym po terminie)");
    }

    private sealed record Open(decimal Gross, DateOnly? Due, decimal Paid);

    private static IResult? Validate(AddPaymentRequest request, decimal totalGross, IEnumerable<Payment> payments, DateOnly today)
    {
        if (request.PaidOn > today)
            return ApiErrors.Validation(nameof(AddPaymentRequest.PaidOn), "Data płatności nie może być późniejsza niż dzisiaj.");

        var remaining = totalGross - payments.Sum(p => p.Amount);
        if (remaining <= 0)
            return ApiErrors.Conflict("Faktura jest już opłacona w całości.");
        if (request.Amount > remaining)
            return ApiErrors.Validation(nameof(AddPaymentRequest.Amount),
                $"Kwota przekracza pozostałą do zapłaty ({remaining.ToString("N2", Pl)}).");
        return null;
    }

    private static Payment NewPayment(int companyId, AddPaymentRequest request, TimeProvider time) => new()
    {
        CompanyId = companyId,
        PaidOn = request.PaidOn!.Value,
        Amount = request.Amount,
        Note = Mapping.EmptyToNull(request.Note),
        CreatedAt = time.GetUtcNow().UtcDateTime
    };
}
