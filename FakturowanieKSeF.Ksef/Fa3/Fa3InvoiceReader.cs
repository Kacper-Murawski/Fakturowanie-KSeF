using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using FakturowanieKSeF.Contracts.Dtos;

namespace FakturowanieKSeF.Ksef.Fa3;

/// <summary>
/// Odczyt treści faktury z XML w strukturze FA (faktury otrzymane od innych wystawców) - na potrzeby podglądu
/// i wydruku. Elementy wyszukiwane są po nazwach lokalnych, dzięki czemu czytnik obsługuje zarówno FA(3),
/// jak i starszą FA(2). Przetwarzanie DTD jest wyłączone (ochrona przed XXE).
/// </summary>
public static class Fa3InvoiceReader
{
    private static readonly (string Suffix, string Label, bool HasVat)[] VatGroups =
    [
        ("1", "23% / 22%", true), ("2", "8% / 7%", true), ("3", "5%", true), ("4", "ryczałt taxi", true), ("5", "procedura OSS", true),
        ("6_1", "0% (kraj)", false), ("6_2", "0% (WDT)", false), ("6_3", "0% (eksport)", false), ("7", "zw", false),
        ("8", "np I", false), ("9", "np II", false), ("10", "odwrotne obciążenie", false), ("11", "marża", false)
    ];

    private static readonly Dictionary<string, string> PaymentMethods = new()
    {
        ["1"] = "Gotówka", ["2"] = "Karta", ["3"] = "Bon", ["4"] = "Czek", ["5"] = "Kredyt", ["6"] = "Przelew", ["7"] = "Płatność mobilna"
    };

    public static InvoiceDocumentDto Read(string xml)
    {
        XDocument doc;
        using (var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            doc = XDocument.Load(reader);

        var fa = One(doc.Root!, "Fa") ?? throw new FormatException("Brak elementu Fa - dokument nie jest fakturą w strukturze FA.");

        var lines = Many(fa, "FaWiersz").Select((w, i) => new DocumentLineDto(
            int.TryParse(Text(w, "NrWierszaFa"), out var n) ? n : i + 1,
            Text(w, "P_7") ?? "",
            Text(w, "P_8A"),
            Dec(w, "P_8B"),
            Dec(w, "P_9A"),
            Dec(w, "P_11"),
            Text(w, "P_12"),
            Text(w, "StanPrzed") == "1")).ToList();

        var vat = new List<DocumentVatSummaryDto>();
        foreach (var (suffix, label, hasVat) in VatGroups)
        {
            if (Dec(fa, $"P_13_{suffix}") is { } net)
                vat.Add(new DocumentVatSummaryDto(label, net, hasVat ? Dec(fa, $"P_14_{suffix}") ?? 0 : null,
                    hasVat ? Dec(fa, $"P_14_{suffix}W") : null));
        }

        var payment = One(fa, "Platnosc");
        return new InvoiceDocumentDto(
            Text(fa, "P_2") ?? "",
            Text(fa, "RodzajFaktury") ?? "VAT",
            Date(fa, "P_1") ?? default,
            Date(fa, "P_6"),
            Text(fa, "KodWaluty") ?? "PLN",
            Party(One(doc.Root!, "Podmiot1")),
            Party(One(doc.Root!, "Podmiot2")),
            lines,
            vat,
            Dec(fa, "P_15") ?? 0,
            payment is null ? null : Date(One(payment, "TerminPlatnosci") ?? payment, "Termin"),
            payment is null ? null : Text(payment, "FormaPlatnosci") is { } f ? PaymentMethods.GetValueOrDefault(f, f) : null,
            payment is null ? null : Text(One(payment, "RachunekBankowy") ?? payment, "NrRB"),
            Text(fa, "PrzyczynaKorekty"),
            Many(fa, "DaneFaKorygowanej").Select(d => new DocumentCorrectedInvoiceDto(
                Text(d, "NrFaKorygowanej") ?? "", Date(d, "DataWystFaKorygowanej"), Text(d, "NrKSeFFaKorygowanej"))).ToList(),
            payment is not null && Text(payment, "Zaplacono") == "1" ? Date(payment, "DataZaplaty") : null,
            Many(fa, "FaWiersz").Select(w => Dec(w, "KursWaluty")).FirstOrDefault(r => r is not null));
    }

    private static DocumentPartyDto Party(XElement? podmiot)
    {
        if (podmiot is null) return new DocumentPartyDto("", null, null, null, null);
        var id = One(podmiot, "DaneIdentyfikacyjne");
        var addr = One(podmiot, "Adres");
        return new DocumentPartyDto(
            Text(id, "Nazwa") ?? "",
            Text(id, "NIP") ?? (Text(id, "NrVatUE") is { } ue ? Text(id, "KodUE") + ue : Text(id, "NrID")),
            Text(addr, "AdresL1"),
            Text(addr, "AdresL2"),
            Text(addr, "KodKraju"));
    }

    // Pomocnicze - wyszukiwanie wyłącznie wśród bezpośrednich dzieci (kolejność i zagnieżdżenie jak w schemacie)
    private static XElement? One(XElement? parent, string name) => parent?.Elements().FirstOrDefault(e => e.Name.LocalName == name);
    private static IEnumerable<XElement> Many(XElement parent, string name) => parent.Elements().Where(e => e.Name.LocalName == name);
    private static string? Text(XElement? parent, string name) => One(parent, name)?.Value.Trim() is { Length: > 0 } v ? v : null;
    private static decimal? Dec(XElement? parent, string name) =>
        decimal.TryParse(Text(parent, name), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    private static DateOnly? Date(XElement? parent, string name) =>
        DateOnly.TryParseExact(Text(parent, name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
