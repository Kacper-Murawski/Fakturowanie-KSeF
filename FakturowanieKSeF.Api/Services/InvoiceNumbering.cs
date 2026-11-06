using FakturowanieKSeF.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FakturowanieKSeF.Api.Services;

/// <summary>
/// Automatyczna numeracja faktur w formacie <c>FV/{numer}/{MM}/{RRRR}</c> (faktury korygujące: osobna seria
/// <c>KOR/{numer}/{MM}/{RRRR}</c>), z numeracją narastającą w ramach miesiąca (najczęściej stosowany schemat
/// w małych firmach). Użytkownik może podać własny numer - wymagana jest jedynie unikalność w ramach firmy
/// (pilnuje jej indeks unikalny w bazie).
/// </summary>
public sealed class InvoiceNumbering(AppDbContext db)
{
    public static string Prefix(InvoiceKind kind) => kind switch
    {
        InvoiceKind.Correction => "KOR/",
        InvoiceKind.Proforma => "PRO/",
        _ => "FV/"
    };

    public static string Format(int sequence, DateOnly issueDate, InvoiceKind kind = InvoiceKind.Vat) =>
        $"{Prefix(kind)}{sequence}/{issueDate:MM}/{issueDate:yyyy}";

    public async Task<string> NextAsync(int companyId, DateOnly issueDate, CancellationToken ct = default) =>
        await NextAsync(companyId, issueDate, InvoiceKind.Vat, ct);

    public async Task<string> NextAsync(int companyId, DateOnly issueDate, InvoiceKind kind, CancellationToken ct = default)
    {
        var prefix = Prefix(kind);
        var suffix = $"/{issueDate:MM}/{issueDate:yyyy}";

        var numbers = await db.Invoices.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.Number.StartsWith(prefix) && i.Number.EndsWith(suffix))
            .Select(i => i.Number)
            .ToListAsync(ct);

        var max = numbers
            .Select(n => int.TryParse(n[prefix.Length..^suffix.Length], out var seq) ? seq : 0)
            .DefaultIfEmpty(0)
            .Max();

        return Format(max + 1, issueDate, kind);
    }
}
