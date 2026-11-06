using System.Globalization;
using System.IO.Compression;
using System.Text;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Ksef.Fa3;
using FakturowanieKSeF.Models;
using Microsoft.EntityFrameworkCore;

namespace FakturowanieKSeF.Api.Services;

/// <summary>
/// Eksport dokumentów dla księgowej za wybrany okres (D34): zestawienia CSV sprzedaży i zakupów oraz paczka ZIP
/// z plikami XML faktur (w postaci przekazanej do / pobranej z KSeF) i UPO.
/// <para>Sprzedaż - faktury przyjęte w KSeF (z numerem KSeF), według daty wystawienia. Zakupy - faktury otrzymane
/// według daty otrzymania, czyli nadania numeru KSeF (od niej liczy się prawo do odliczenia VAT).</para>
/// <para>Kwoty pochodzą z dokumentów XML (dla sprzedaży - z dokładnie wysłanego XML), a nie z pól pomocniczych bazy:
/// zestawienie odpowiada temu, co jest w KSeF.</para>
/// </summary>
public sealed partial class AccountingExport(AppDbContext db)
{
    /// <summary>Maksymalny okres jednego eksportu - paczka budowana jest w pamięci.</summary>
    public const int MaxDays = 366;

    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");
    private static readonly TimeZoneInfo Warsaw = FindZone();

    /// <summary>Kolumny kwot per stawka: nagłówek → etykiety grup VAT z <see cref="Fa3InvoiceReader"/>.</summary>
    private static readonly (string Header, string[] Labels, bool Vat)[] RateColumns =
    [
        ("23%", ["23% / 22%"], true),
        ("8%", ["8% / 7%"], true),
        ("5%", ["5%"], true),
        ("0%", ["0% (kraj)", "0% (WDT)", "0% (eksport)"], false),
        ("zw", ["zw"], false),
        ("np", ["np I", "np II"], false),
        ("oo", ["odwrotne obciążenie"], false)
    ];

    public sealed record SaleRow(Invoice Invoice, KsefSubmission Submission, InvoiceDocumentDto Document);

    public sealed record PurchaseRow(ReceivedInvoice Invoice, InvoiceDocumentDto Document);

    public async Task<List<SaleRow>> SalesAsync(int companyId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var invoices = await db.Invoices.AsNoTracking().Include(i => i.KsefSubmissions)
            .Where(i => i.CompanyId == companyId && i.Status == InvoiceStatus.Accepted && i.IssueDate >= from && i.IssueDate <= to)
            .OrderBy(i => i.IssueDate).ThenBy(i => i.Id)
            .AsSplitQuery()
            .ToListAsync(ct);

        return invoices
            .Select(i => (Invoice: i, Submission: i.KsefSubmissions.Where(s => s.KsefNumber != null).OrderByDescending(s => s.Id).FirstOrDefault()))
            .Where(x => x.Submission is not null)
            .Select(x => new SaleRow(x.Invoice, x.Submission!, Fa3InvoiceReader.Read(x.Submission!.SentXml)))
            .ToList();
    }

    public async Task<List<PurchaseRow>> PurchasesAsync(int companyId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var (start, end) = UtcRange(from, to);
        var invoices = await db.ReceivedInvoices.AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.KsefAcceptedAt >= start && r.KsefAcceptedAt < end)
            .OrderBy(r => r.KsefAcceptedAt).ThenBy(r => r.Id)
            .ToListAsync(ct);

        return invoices.Select(r => new PurchaseRow(r, Fa3InvoiceReader.Read(r.Xml))).ToList();
    }

    public static ExportSummaryDto Summary(DateOnly from, DateOnly to, List<SaleRow> sales, List<PurchaseRow> purchases) => new(
        from, to,
        sales.Count, sales.Sum(s => Net(s.Document)), sales.Sum(s => Vat(s.Document)), sales.Sum(s => s.Document.TotalGross),
        purchases.Count, purchases.Count(p => p.Document.Currency != "PLN"),
        purchases.Where(p => p.Document.Currency == "PLN").Sum(p => Net(p.Document)),
        purchases.Where(p => p.Document.Currency == "PLN").Sum(p => Vat(p.Document)),
        purchases.Where(p => p.Document.Currency == "PLN").Sum(p => p.Document.TotalGross));

    // ------------------------------------------------------------------ CSV

    public static byte[] SalesCsv(IEnumerable<SaleRow> rows)
    {
        var csv = new CsvBuilder();
        csv.Row(["Rodzaj", "Numer faktury", "Data wystawienia", "Data sprzedaży", "Numer KSeF", "Data nadania numeru KSeF",
                 "Nabywca", "NIP nabywcy", "Faktura korygowana", "Przyczyna korekty",
                 .. RateHeaders(), "Razem netto", "Razem VAT", "Brutto", "Waluta", "Forma płatności", "Termin płatności", "Data zapłaty"]);

        foreach (var (invoice, submission, doc) in rows)
        {
            csv.Row([
                doc.InvoiceType, doc.InvoiceNumber, Date(doc.IssueDate), Date(doc.SaleDate), submission.KsefNumber,
                Date(LocalDate(submission.KsefAssignedAt ?? invoice.KsefAcceptedAt)),
                doc.Buyer.Name, doc.Buyer.Nip,
                string.Join(", ", (doc.CorrectedInvoices ?? []).Select(c => c.Number)), doc.CorrectionReason,
                .. RateValues(doc), Money(Net(doc)), Money(Vat(doc)), Money(doc.TotalGross), doc.Currency,
                doc.PaymentMethod, Date(doc.DueDate), Date(doc.PaidOn)
            ]);
        }
        return csv.ToBytes();
    }

    public static byte[] PurchasesCsv(IEnumerable<PurchaseRow> rows)
    {
        var csv = new CsvBuilder();
        csv.Row(["Rodzaj", "Numer faktury", "Data wystawienia", "Data otrzymania (KSeF)", "Numer KSeF",
                 "Sprzedawca", "NIP sprzedawcy", "Faktura korygowana",
                 .. RateHeaders(), "Razem netto", "Razem VAT", "Brutto", "Waluta", "Forma płatności", "Termin płatności",
                 "Rachunek", "Rachunek na białej liście"]);

        foreach (var (invoice, doc) in rows)
        {
            csv.Row([
                doc.InvoiceType, doc.InvoiceNumber, Date(doc.IssueDate), Date(LocalDate(invoice.KsefAcceptedAt)), invoice.KsefNumber,
                doc.Seller.Name, doc.Seller.Nip,
                string.Join(", ", (doc.CorrectedInvoices ?? []).Select(c => c.Number)),
                .. RateValues(doc), Money(Net(doc)), Money(Vat(doc)), Money(doc.TotalGross), doc.Currency,
                doc.PaymentMethod, Date(doc.DueDate), doc.BankAccount,
                invoice.BankAccountOnWhiteList switch
                {
                    true => $"tak ({Date(invoice.BankAccountCheckedOn)})",
                    false => $"NIE ({Date(invoice.BankAccountCheckedOn)})",
                    null => "nie sprawdzano"
                }
            ]);
        }
        return csv.ToBytes();
    }

    // ------------------------------------------------------------------ ZIP

    /// <summary>
    /// Paczka: oba zestawienia CSV, XML faktur sprzedaży z UPO (katalog <c>sprzedaz/</c>) i XML faktur zakupu
    /// (katalog <c>zakupy/</c>). Nazwy plików powstają z numerów faktur (znaki spoza liter i cyfr zamieniane na „_”).
    /// </summary>
    public static byte[] Package(DateOnly from, DateOnly to, List<SaleRow> sales, List<PurchaseRow> purchases)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string name, byte[] content)
            {
                var unique = name;
                for (var n = 2; !used.Add(unique); n++)
                    unique = Path.ChangeExtension(name, null) + $"_{n}" + Path.GetExtension(name);
                var entry = zip.CreateEntry(unique, CompressionLevel.Optimal);
                using var s = entry.Open();
                s.Write(content);
            }

            Add("sprzedaz.csv", SalesCsv(sales));
            Add("zakupy.csv", PurchasesCsv(purchases));
            Add("README.txt", Encoding.UTF8.GetBytes(Readme(from, to, sales.Count, purchases.Count)));

            foreach (var (invoice, submission, _) in sales)
            {
                var name = SafeName(invoice.Number);
                Add($"sprzedaz/{name}.xml", Encoding.UTF8.GetBytes(submission.SentXml));
                if (submission.UpoXml is not null) Add($"sprzedaz/{name}_UPO.xml", Encoding.UTF8.GetBytes(submission.UpoXml));
            }

            foreach (var (invoice, _) in purchases)
                Add($"zakupy/{SafeName(invoice.KsefNumber)}.xml", Encoding.UTF8.GetBytes(invoice.Xml));
        }
        return stream.ToArray();
    }

    private static string Readme(DateOnly from, DateOnly to, int sales, int purchases) => $"""
        Eksport dokumentów z aplikacji Fakturowanie KSeF
        Okres: {Date(from)} - {Date(to)}

        sprzedaz.csv  - faktury wystawione przyjęte w KSeF ({sales}), według daty wystawienia
        zakupy.csv    - faktury otrzymane z KSeF ({purchases}), według daty otrzymania (nadania numeru KSeF)
        sprzedaz/     - XML faktur (FA(3), dokładnie w postaci przesłanej do KSeF) i UPO (*_UPO.xml)
        zakupy/       - XML faktur pobranych z KSeF (nazwa pliku = numer KSeF)

        Pliki CSV: separator średnik, przecinek dziesiętny, kodowanie UTF-8 (otwierają się w Excelu).
        Kwoty korekt są różnicą (po korekcie - przed korektą), tak jak w fakturze korygującej.
        """;

    // ------------------------------------------------------------------ pomocnicze

    public static string? Validate(DateOnly? from, DateOnly? to) =>
        from is null || to is null ? "Podaj okres eksportu (od i do)."
        : to < from ? "Data końcowa nie może być wcześniejsza niż początkowa."
        : to.Value.DayNumber - from.Value.DayNumber + 1 > MaxDays ? $"Jednorazowo można wyeksportować najwyżej {MaxDays} dni."
        : null;

    public static string FileSuffix(DateOnly from, DateOnly to) =>
        from.Day == 1 && to == from.AddMonths(1).AddDays(-1) ? from.ToString("yyyy-MM", CultureInfo.InvariantCulture)
        : $"{from:yyyy-MM-dd}_{to:yyyy-MM-dd}";

    private static IEnumerable<string> RateHeaders() =>
        RateColumns.SelectMany(c => c.Vat ? new[] { $"Netto {c.Header}", $"VAT {c.Header}" } : [$"Netto {c.Header}"]);

    private static IEnumerable<string?> RateValues(InvoiceDocumentDto doc) => RateColumns.SelectMany(c =>
    {
        var lines = doc.VatSummary.Where(v => c.Labels.Contains(v.Label)).ToList();
        string? net = lines.Count == 0 ? null : Money(lines.Sum(v => v.Net));
        string? vat = lines.Count == 0 ? null : Money(lines.Sum(v => v.Vat ?? 0));
        return c.Vat ? new[] { net, vat } : [net];
    });

    private static decimal Net(InvoiceDocumentDto doc) => doc.VatSummary.Sum(v => v.Net);
    private static decimal Vat(InvoiceDocumentDto doc) => doc.VatSummary.Sum(v => v.Vat ?? 0);

    private static string Money(decimal value) => value.ToString("0.00", Pl);
    private static string? Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string SafeName(string s) => string.Concat(s.Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '_'));

    /// <summary>Dzień kalendarzowy (czas polski) chwili zapisanej w UTC.</summary>
    private static DateOnly? LocalDate(DateTime? utc) =>
        utc is { } u ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(u, DateTimeKind.Utc), Warsaw)) : null;

    /// <summary>Okres [od 00:00 pierwszego dnia; do 00:00 dnia po ostatnim) w czasie polskim, wyrażony w UTC.</summary>
    private static (DateTime Start, DateTime End) UtcRange(DateOnly from, DateOnly to) => (
        TimeZoneInfo.ConvertTimeToUtc(from.ToDateTime(TimeOnly.MinValue), Warsaw),
        TimeZoneInfo.ConvertTimeToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue), Warsaw));

    private static TimeZoneInfo FindZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }

    /// <summary>
    /// CSV w formacie otwieranym wprost przez polskiego Excela: średnik, przecinek dziesiętny, UTF-8 ze znacznikiem BOM.
    /// Pola tekstowe zaczynające się od =, +, -, @ są poprzedzane apostrofem - ochrona przed wstrzyknięciem formuł
    /// (dane kontrahentów pochodzą także od osób trzecich, z faktur otrzymanych).
    /// </summary>
    public sealed partial class CsvBuilder
    {
        [System.Text.RegularExpressions.GeneratedRegex(@"^-\d+(,\d+)?$")]
        private static partial System.Text.RegularExpressions.Regex NegativeAmount();

        private readonly StringBuilder _sb = new();

        public void Row(IEnumerable<string?> fields)
        {
            _sb.AppendJoin(';', fields.Select(Escape));
            _sb.Append("\r\n");
        }

        public byte[] ToBytes() => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(_sb.ToString())];

        public static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            // Liczby ujemne (np. kwoty korekt) nie są formułą - zabezpieczamy wyłącznie tekst
            if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' && !NegativeAmount().IsMatch(value))
                value = "'" + value;
            return value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }
    }
}
