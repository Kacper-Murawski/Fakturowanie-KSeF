using System.Globalization;
using System.Text;
using FakturowanieKSeF.Models;

namespace FakturowanieKSeF.Api.Services.Email;

/// <summary>
/// Treści wiadomości (zwykły tekst). Każda wiadomość do kontrahenta zawiera informację, że aplikacja korzysta
/// z testowego środowiska KSeF - faktury z niego nie mają mocy prawnej (D8, D24).
/// </summary>
public static class EmailTemplates
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public const string TestEnvironmentNotice =
        "UWAGA: wiadomość wysłana z wersji testowej aplikacji Fakturowanie KSeF. Faktura pochodzi z TESTOWEGO "
        + "środowiska KSeF i nie ma mocy prawnej - nie należy jej opłacać ani księgować.";

    public static (string Subject, string Body) Invoice(Invoice invoice, Company company, string? message)
    {
        var correction = invoice.Kind == InvoiceKind.Correction;
        var subject = $"{(correction ? "Faktura korygująca" : "Faktura")} {invoice.Number} - {company.Name}";

        var b = new StringBuilder();
        b.AppendLine("Dzień dobry,").AppendLine();
        b.AppendLine($"w załączeniu przesyłamy {(correction ? "fakturę korygującą" : "fakturę")} {invoice.Number} z dnia {Date(invoice.IssueDate)}.");
        if (correction && invoice.CorrectedInvoiceNumber is not null)
            b.AppendLine($"Korekta dotyczy faktury {invoice.CorrectedInvoiceNumber}.");
        if (!string.IsNullOrWhiteSpace(message))
            b.AppendLine().AppendLine(message.Trim());
        b.AppendLine();

        if (correction)
            b.AppendLine(invoice.TotalGross < 0
                ? $"Kwota do zwrotu: {Money(-invoice.TotalGross, invoice.Currency)}"
                : $"Kwota do dopłaty: {Money(invoice.TotalGross, invoice.Currency)}");
        else if (invoice.PaidOn is { } paidOn)
            b.AppendLine($"Kwota: {Money(invoice.TotalGross, invoice.Currency)} - zapłacono {Date(paidOn)}.");
        else
            b.AppendLine($"Kwota do zapłaty: {Money(invoice.TotalGross, invoice.Currency)}");

        if (invoice.PaidOn is null && invoice.DueDate is { } due) b.AppendLine($"Termin płatności: {Date(due)}");
        if (invoice.PaidOn is null && invoice.BankAccountNumber is not null) b.AppendLine($"Numer rachunku: {Account(invoice.BankAccountNumber)}");
        b.AppendLine($"Numer KSeF: {invoice.KsefNumber}");
        b.AppendLine();
        b.AppendLine("Faktura została wystawiona w Krajowym Systemie e-Faktur (KSeF). Jej oryginałem jest plik XML,");
        b.AppendLine("a plik PDF jest wizualizacją z kodem QR do weryfikacji faktury w KSeF.");
        AppendSignature(b, company);
        return (subject, b.ToString());
    }

    public static (string Subject, string Body) PaymentReminder(Invoice invoice, Company company, decimal remaining, DateOnly today, string? message)
    {
        var overdueDays = invoice.DueDate is { } d ? today.DayNumber - d.DayNumber : 0;
        var subject = $"Przypomnienie o płatności - faktura {invoice.Number} - {company.Name}";

        var b = new StringBuilder();
        b.AppendLine("Dzień dobry,").AppendLine();
        b.AppendLine($"uprzejmie przypominamy o płatności za fakturę {invoice.Number} z dnia {Date(invoice.IssueDate)}.");
        if (invoice.DueDate is { } due)
            b.AppendLine(overdueDays > 0
                ? $"Termin płatności minął {Date(due)} ({overdueDays} {(overdueDays == 1 ? "dzień" : "dni")} temu)."
                : $"Termin płatności: {Date(due)}.");
        if (!string.IsNullOrWhiteSpace(message))
            b.AppendLine().AppendLine(message.Trim());
        b.AppendLine();
        b.AppendLine($"Pozostało do zapłaty: {Money(remaining, invoice.Currency)}");
        if (invoice.BankAccountNumber is not null) b.AppendLine($"Numer rachunku: {Account(invoice.BankAccountNumber)}");
        b.AppendLine($"Tytuł przelewu: {invoice.Number}");
        b.AppendLine($"Numer KSeF: {invoice.KsefNumber}");
        b.AppendLine();
        b.AppendLine("Jeśli płatność została już zrealizowana, prosimy potraktować tę wiadomość jako nieaktualną.");
        b.AppendLine("Faktura w załączniku.");
        AppendSignature(b, company);
        return (subject, b.ToString());
    }

    /// <summary>Próg z art. 19 ustawy Prawo przedsiębiorców - powyżej niego zapłata na rachunek spoza białej listy ma skutki podatkowe (D29).</summary>
    public const decimal WhiteListThreshold = 15000m;

    public const int MaxListedInvoices = 20;

    public static (string Subject, string Body) ReceivedInvoices(Company company, IReadOnlyList<ReceivedInvoice> invoices, string? appUrl)
    {
        var subject = $"Nowe faktury kosztowe w KSeF: {invoices.Count} - {company.Name}";

        var b = new StringBuilder();
        b.AppendLine("Dzień dobry,").AppendLine();
        b.AppendLine($"do KSeF trafiły nowe faktury wystawione na firmę {company.Name} (NIP {company.Nip}):").AppendLine();

        var n = 0;
        foreach (var r in invoices.OrderBy(r => r.DueDate ?? DateOnly.MaxValue).ThenBy(r => r.IssueDate).Take(MaxListedInvoices))
        {
            b.AppendLine($"{++n}. {r.InvoiceNumber} - {r.SellerName} (NIP {r.SellerNip})");
            b.Append($"   kwota {Money(r.TotalGross, r.Currency)}");
            b.AppendLine(r.DueDate is { } due ? $", termin płatności {Date(due)}" : "");
            if (r.Currency == "PLN" && r.TotalGross > WhiteListThreshold)
                b.AppendLine("   Kwota powyżej 15 000 zł - przed zapłatą sprawdź rachunek sprzedawcy na białej liście.");
        }
        if (invoices.Count > MaxListedInvoices)
            b.AppendLine($"... i {invoices.Count - MaxListedInvoices} kolejnych.");

        b.AppendLine();
        b.AppendLine(string.IsNullOrWhiteSpace(appUrl)
            ? "Faktury są dostępne w aplikacji w zakładce „Faktury otrzymane”."
            : $"Faktury są dostępne w aplikacji: {appUrl.TrimEnd('/')}/faktury-otrzymane");
        b.AppendLine("Powiadomienia wyłączysz na stronie „Moja firma” w sekcji „Powiadomienia e-mail”.");
        b.AppendLine().AppendLine("--").AppendLine("Fakturowanie KSeF (wersja testowa - dane z testowego środowiska KSeF).");
        return (subject, b.ToString());
    }

    internal static void AppendSignature(StringBuilder b, Company company)
    {
        b.AppendLine().AppendLine("Pozdrawiamy").AppendLine(company.Name);
        if (!string.IsNullOrWhiteSpace(company.Email)) b.AppendLine(company.Email);
        if (!string.IsNullOrWhiteSpace(company.Phone)) b.AppendLine($"tel. {company.Phone}");
        b.AppendLine().AppendLine("--").AppendLine(TestEnvironmentNotice);
    }

    internal static string Money(decimal value, string currency) => $"{value.ToString("N2", Pl)} {currency}";

    internal static string Date(DateOnly date) => date.ToString("dd.MM.yyyy", Pl);

    /// <summary>Numer rachunku w grupach po 4 cyfry (2 + 6 × 4 dla NRB).</summary>
    internal static string Account(string account) =>
        account.Length == 26 ? account[..2] + " " + string.Join(" ", Enumerable.Range(0, 6).Select(i => account.Substring(2 + i * 4, 4))) : account;
}
