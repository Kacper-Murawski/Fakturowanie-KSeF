using System.Globalization;
using FakturowanieKSeF.Contracts;

namespace FakturowanieKSeF.Client.Services;

/// <summary>Formatowanie wartości do wyświetlenia (polskie ustawienia regionalne).</summary>
public static class Format
{
    public static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static string Money(decimal value, string currency = "PLN") => $"{value.ToString("N2", Pl)} {currency}";

    public static string Amount(decimal value) => value.ToString("N2", Pl);

    public static string Quantity(decimal value) => value.ToString("0.######", Pl);

    public static string Date(DateOnly? date) => date?.ToString("dd.MM.yyyy", Pl) ?? "—";

    public static string DateTime(DateTime? value) => value?.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Pl) ?? "—";

    public static string Nip(string? nip) =>
        nip is { Length: 10 } ? $"{nip[..3]}-{nip[3..6]}-{nip[6..8]}-{nip[8..]}" : nip ?? "—";

    public static string Status(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Draft => "Szkic",
        InvoiceStatus.Pending => "Wysłana do KSeF",
        InvoiceStatus.Accepted => "Przyjęta w KSeF",
        InvoiceStatus.Rejected => "Odrzucona przez KSeF",
        _ => status.ToString()
    };

    public static string StatusCss(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Draft => "bg-secondary",
        InvoiceStatus.Pending => "bg-warning text-dark",
        InvoiceStatus.Accepted => "bg-success",
        InvoiceStatus.Rejected => "bg-danger",
        _ => "bg-light text-dark"
    };

    public static string Payment(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Gotówka",
        PaymentMethod.Card => "Karta",
        PaymentMethod.Voucher => "Bon",
        PaymentMethod.Cheque => "Czek",
        PaymentMethod.Credit => "Kredyt",
        PaymentMethod.Transfer => "Przelew",
        PaymentMethod.Mobile => "Płatność mobilna",
        _ => method.ToString()
    };

    public static string BankAccount(string? account) =>
        account is null ? "—" : string.Join(' ', Chunk(account));

    private static IEnumerable<string> Chunk(string account)
    {
        // Format NRB: 2 cyfry kontrolne + grupy po 4
        if (account.Length == 26 && account.All(char.IsAsciiDigit))
        {
            yield return account[..2];
            for (var i = 2; i < 26; i += 4) yield return account.Substring(i, 4);
        }
        else yield return account;
    }
}
