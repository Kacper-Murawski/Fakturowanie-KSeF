using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Contracts.Dtos;
using Microsoft.EntityFrameworkCore;

namespace FakturowanieKSeF.Api.Endpoints;

/// <summary>
/// Dane wykresów pulpitu (D40): sprzedaż i koszty w ostatnich 12 miesiącach, wiekowanie należności, najwięksi
/// kontrahenci. Agregacje liczone w bazie, wyłącznie dla firmy zalogowanego użytkownika.
/// </summary>
public static class DashboardEndpoints
{
    public const int Months = 12;

    public static void MapDashboardEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/dashboard", async (CurrentCompany current, AppDbContext db, TimeProvider time, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var today = PolishDate.Today(time);
                var from = new DateOnly(today.Year, today.Month, 1).AddMonths(-(Months - 1));

                // Sprzedaż: faktury przekazane do KSeF; korekty wliczane różnicą w miesiącu ich wystawienia
                var issued = db.Invoices.AsNoTracking()
                    .Where(i => i.CompanyId == company.Id && i.Currency == "PLN" && i.IssueDate >= from
                                && (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.Accepted));

                var sales = await issued
                    .GroupBy(i => new { i.IssueDate.Year, i.IssueDate.Month })
                    .Select(g => new { g.Key.Year, g.Key.Month, Net = g.Sum(i => i.TotalNet) })
                    .ToListAsync(ct);

                var costs = await db.ReceivedInvoices.AsNoTracking()
                    .Where(r => r.CompanyId == company.Id && r.Currency == "PLN" && r.IssueDate >= from)
                    .GroupBy(r => new { r.IssueDate.Year, r.IssueDate.Month })
                    .Select(g => new { g.Key.Year, g.Key.Month, Net = g.Sum(r => r.TotalNet) })
                    .ToListAsync(ct);

                var months = Enumerable.Range(0, Months).Select(n => from.AddMonths(n)).Select(m => new MonthlyTotalsDto(
                    m.Year, m.Month,
                    sales.Where(s => s.Year == m.Year && s.Month == m.Month).Sum(s => s.Net),
                    costs.Where(c => c.Year == m.Year && c.Month == m.Month).Sum(c => c.Net))).ToList();

                // Należności jak w podsumowaniu rozliczeń (D30): brutto + korekty - wpłaty
                var open = await db.Invoices.AsNoTracking()
                    .Where(i => i.CompanyId == company.Id && i.Kind == InvoiceKind.Vat && i.Currency == "PLN"
                                && (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.Accepted))
                    .Select(i => new
                    {
                        Due = i.DueDate,
                        Remaining = i.TotalGross
                                    + i.Corrections.Where(c => c.Status == InvoiceStatus.Pending || c.Status == InvoiceStatus.Accepted).Sum(c => c.TotalGross)
                                    - i.Payments.Sum(p => p.Amount)
                    })
                    .Where(x => x.Remaining > 0)
                    .ToListAsync(ct);

                int Overdue(DateOnly? due) => due is { } d && d < today ? today.DayNumber - d.DayNumber : 0;
                AgingBucketDto Bucket(string label, Func<int, bool> days)
                {
                    var items = open.Where(x => days(Overdue(x.Due))).ToList();
                    return new AgingBucketDto(label, items.Sum(x => x.Remaining), items.Count);
                }
                var aging = new List<AgingBucketDto>
                {
                    Bucket("w terminie", d => d == 0),
                    Bucket("1-30 dni po terminie", d => d is >= 1 and <= 30),
                    Bucket("31-60 dni po terminie", d => d is >= 31 and <= 60),
                    Bucket("ponad 60 dni po terminie", d => d > 60)
                };

                var top = await issued
                    .GroupBy(i => new { i.Buyer.Nip, i.Buyer.Name })
                    .Select(g => new { g.Key.Name, g.Key.Nip, Net = g.Sum(i => i.TotalNet), Count = g.Count(i => i.Kind == InvoiceKind.Vat) })
                    .OrderByDescending(x => x.Net)
                    .Take(5)
                    .ToListAsync(ct);

                return Results.Ok(new DashboardDto(months, aging,
                    top.Where(t => t.Net > 0).Select(t => new TopCustomerDto(t.Name, t.Nip, t.Net, t.Count)).ToList()));
            })
            .WithTags("Pulpit").RequireAuthorization()
            .WithSummary("Dane wykresów pulpitu: sprzedaż i koszty (12 miesięcy), wiekowanie należności, najwięksi kontrahenci");
    }
}
