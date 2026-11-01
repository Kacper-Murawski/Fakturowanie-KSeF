using System.Globalization;
using System.Text;
using System.Xml;
using FakturowanieKSeF.Contracts;
using FakturowanieKSeF.Contracts.Calculation;
using FakturowanieKSeF.Models;

namespace FakturowanieKSeF.Ksef.Fa3;

/// <summary>Faktury nie da się przedstawić w strukturze FA(3) (np. nieobsługiwana stawka lub waluta).</summary>
public sealed class Fa3GenerationException(string message) : Exception(message);

/// <summary>
/// Generuje XML faktury w strukturze logicznej FA(3) (schemat_FA(3)_v1-0E.xsd) - postaci, w jakiej faktura
/// jest przesyłana do KSeF. Kolejność elementów odpowiada dokładnie sekwencjom ze schematu XSD.
/// <para>Zakres: faktura VAT podstawowa (RodzajFaktury = VAT) i faktura korygująca (KOR) w PLN. Wymagania techniczne KSeF:
/// kodowanie UTF-8 bez znacznika BOM, brak instrukcji przetwarzania poza deklaracją XML.</para>
/// </summary>
public sealed class Fa3XmlGenerator(TimeProvider timeProvider)
{
    public const string Namespace = "http://crd.gov.pl/wzor/2025/06/25/13775/";
    public const string SystemInfo = "Fakturowanie KSeF (projekt inżynierski)";

    public Fa3XmlGenerator() : this(TimeProvider.System) { }

    /// <summary>Tworzy XML FA(3) i zwraca go jako bajty UTF-8 (bez BOM) - dokładnie w tej postaci trafia do KSeF.</summary>
    public byte[] GenerateBytes(Invoice invoice)
    {
        EnsureSupported(invoice);

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n"
        };

        using var stream = new MemoryStream();
        using (var w = XmlWriter.Create(stream, settings))
        {
            w.WriteStartDocument();
            w.WriteStartElement("Faktura", Namespace);

            WriteHeader(w);
            WriteSeller(w, invoice.Seller);
            WriteBuyer(w, invoice.Buyer);
            WriteFa(w, invoice);

            w.WriteEndElement();
            w.WriteEndDocument();
        }

        return stream.ToArray();
    }

    public string Generate(Invoice invoice) => Encoding.UTF8.GetString(GenerateBytes(invoice));

    /// <summary>Sprawdza, czy fakturę da się przedstawić w FA(3) w zakresie obsługiwanym przez aplikację.</summary>
    public static void EnsureSupported(Invoice invoice)
    {
        if (invoice.Currency != "PLN")
            throw new Fa3GenerationException("Obsługiwane są wyłącznie faktury w PLN (waluta obca wymaga kwot podatku w PLN i kursu waluty).");

        if (invoice.Items.Count == 0)
            throw new Fa3GenerationException("Faktura nie ma pozycji.");

        if (invoice.Kind == InvoiceKind.Correction
            && (invoice.CorrectedInvoiceNumber is null || invoice.CorrectedInvoiceIssueDate is null || invoice.CorrectedInvoiceKsefNumber is null
                || string.IsNullOrWhiteSpace(invoice.CorrectionReason)))
            throw new Fa3GenerationException("Brak danych faktury korygowanej (numer, data, numer KSeF) lub przyczyny korekty.");

        if (invoice.Items.Any(i => i.VatRate is VatRate.Rate4 or VatRate.Rate3))
            throw new Fa3GenerationException("Stawki 4% i 3% (ryczałt dla taksówek osobowych) nie są obsługiwane.");

        if (invoice.Items.Any(i => i.VatRate == VatRate.Exempt) && string.IsNullOrWhiteSpace(invoice.ExemptionBasis))
            throw new Fa3GenerationException("Dla pozycji zwolnionych z VAT wymagana jest podstawa prawna zwolnienia.");

        if (string.IsNullOrWhiteSpace(invoice.Seller.Nip))
            throw new Fa3GenerationException("Brak NIP sprzedawcy.");
    }

    // ------------------------------------------------------------------ Naglowek

    private void WriteHeader(XmlWriter w)
    {
        w.WriteStartElement("Naglowek", Namespace);

        w.WriteStartElement("KodFormularza", Namespace);
        w.WriteAttributeString("kodSystemowy", "FA (3)");
        w.WriteAttributeString("wersjaSchemy", "1-0E");
        w.WriteString("FA");
        w.WriteEndElement();

        Element(w, "WariantFormularza", "3");
        Element(w, "DataWytworzeniaFa", timeProvider.GetUtcNow().UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        Element(w, "SystemInfo", SystemInfo);

        w.WriteEndElement();
    }

    // ------------------------------------------------------------------ Podmiot1 / Podmiot2

    private static void WriteSeller(XmlWriter w, InvoiceParty seller)
    {
        w.WriteStartElement("Podmiot1", Namespace);

        w.WriteStartElement("DaneIdentyfikacyjne", Namespace);
        Element(w, "NIP", seller.Nip!);
        Element(w, "Nazwa", seller.Name);
        w.WriteEndElement();

        WriteAddress(w, seller.Address);

        w.WriteEndElement();
    }

    private static void WriteBuyer(XmlWriter w, InvoiceParty buyer)
    {
        w.WriteStartElement("Podmiot2", Namespace);

        w.WriteStartElement("DaneIdentyfikacyjne", Namespace);
        if (!string.IsNullOrWhiteSpace(buyer.Nip))
            Element(w, "NIP", buyer.Nip);
        else
            Element(w, "BrakID", "1"); // osoba prywatna - brak identyfikatora podatkowego
        Element(w, "Nazwa", buyer.Name);
        w.WriteEndElement();

        WriteAddress(w, buyer.Address);

        Element(w, "JST", "2"); // nie dotyczy jednostki podrzędnej JST
        Element(w, "GV", "2");  // nie dotyczy członka grupy VAT

        w.WriteEndElement();
    }

    private static void WriteAddress(XmlWriter w, Address address)
    {
        w.WriteStartElement("Adres", Namespace);
        Element(w, "KodKraju", address.CountryCode);
        Element(w, "AdresL1", address.Street);
        Element(w, "AdresL2", $"{address.PostalCode} {address.City}");
        w.WriteEndElement();
    }

    // ------------------------------------------------------------------ Fa

    private static void WriteFa(XmlWriter w, Invoice invoice)
    {
        var items = invoice.Items.OrderBy(i => i.Position).ToList();
        var correction = invoice.Kind == InvoiceKind.Correction;
        LineInput Input(InvoiceItem i) => new(i.Quantity, i.UnitNetPrice, i.VatRate);

        // Faktura korygująca metodą „przed i po”: kwoty P_13_x / P_14_x / P_15 to różnica (po - przed),
        // a wiersze stanu przed korektą oznaczone są znacznikiem StanPrzed
        IReadOnlyList<VatRateSummary> summary;
        decimal totalGross;
        if (correction)
        {
            var c = InvoiceCalculator.CalculateCorrection(
                items.Where(i => i.BeforeCorrection).Select(Input), items.Where(i => !i.BeforeCorrection).Select(Input));
            summary = c.Difference;
            totalGross = c.TotalGross;
        }
        else
        {
            var t = InvoiceCalculator.Calculate(items.Select(Input));
            summary = t.ByVatRate;
            totalGross = t.TotalGross;
        }

        w.WriteStartElement("Fa", Namespace);

        Element(w, "KodWaluty", invoice.Currency);
        Element(w, "P_1", Date(invoice.IssueDate));
        if (!string.IsNullOrWhiteSpace(invoice.IssuePlace)) Element(w, "P_1M", invoice.IssuePlace);
        Element(w, "P_2", invoice.Number);
        if (invoice.SaleDate is { } saleDate && saleDate != invoice.IssueDate) Element(w, "P_6", Date(saleDate));

        WriteVatSummary(w, summary);
        Element(w, "P_15", Money(totalGross));

        WriteAnnotations(w, invoice, items);
        Element(w, "RodzajFaktury", correction ? "KOR" : "VAT");
        if (correction) WriteCorrectedInvoice(w, invoice);

        for (var n = 0; n < items.Count; n++)
            WriteLine(w, n + 1, items[n], InvoiceCalculator.CalculateLine(Input(items[n])));

        // Płatność na korekcie tylko przy dopłacie; zwrot nadpłaty nie ma terminu płatności dla nabywcy
        if (!correction || totalGross > 0) WritePayment(w, invoice);

        w.WriteEndElement();
    }

    /// <summary>Dane faktury korygowanej (sekwencja dla RodzajFaktury = KOR).</summary>
    private static void WriteCorrectedInvoice(XmlWriter w, Invoice invoice)
    {
        Element(w, "PrzyczynaKorekty", invoice.CorrectionReason!.Trim());
        if (invoice.CorrectionType is { } type) Element(w, "TypKorekty", ((int)type).ToString(CultureInfo.InvariantCulture));

        w.WriteStartElement("DaneFaKorygowanej", Namespace);
        Element(w, "DataWystFaKorygowanej", Date(invoice.CorrectedInvoiceIssueDate!.Value));
        Element(w, "NrFaKorygowanej", invoice.CorrectedInvoiceNumber!);
        Element(w, "NrKSeF", "1"); // faktura korygowana wystawiona w KSeF
        Element(w, "NrKSeFFaKorygowanej", invoice.CorrectedInvoiceKsefNumber!);
        w.WriteEndElement();
    }

    /// <summary>
    /// Podsumowanie per stawka (P_13_x - netto, P_14_x - podatek). Pary pól występują wyłącznie dla stawek
    /// użytych na fakturze, w kolejności wymaganej przez schemat.
    /// </summary>
    private static void WriteVatSummary(XmlWriter w, IReadOnlyList<VatRateSummary> byVatRate)
    {
        decimal Net(params VatRate[] rates) => byVatRate.Where(s => rates.Contains(s.VatRate)).Sum(s => s.Net);
        decimal Vat(params VatRate[] rates) => byVatRate.Where(s => rates.Contains(s.VatRate)).Sum(s => s.Vat);
        bool Used(params VatRate[] rates) => byVatRate.Any(s => rates.Contains(s.VatRate));

        void Pair(string suffix, params VatRate[] rates)
        {
            if (!Used(rates)) return;
            Element(w, $"P_13_{suffix}", Money(Net(rates)));
            Element(w, $"P_14_{suffix}", Money(Vat(rates)));
        }

        void NetOnly(string field, VatRate rate)
        {
            if (Used(rate)) Element(w, field, Money(Net(rate)));
        }

        Pair("1", VatRate.Rate23, VatRate.Rate22);   // stawka podstawowa
        Pair("2", VatRate.Rate8, VatRate.Rate7);     // stawka obniżona pierwsza
        Pair("3", VatRate.Rate5);                    // stawka obniżona druga
        NetOnly("P_13_6_1", VatRate.Rate0Domestic);  // 0% kraj
        NetOnly("P_13_6_2", VatRate.Rate0Wdt);       // 0% WDT
        NetOnly("P_13_6_3", VatRate.Rate0Export);    // 0% eksport
        NetOnly("P_13_7", VatRate.Exempt);           // zwolnione
        NetOnly("P_13_8", VatRate.NotSubjectI);      // np I
        NetOnly("P_13_9", VatRate.NotSubjectII);     // np II (art. 100 ust. 1 pkt 4)
        NetOnly("P_13_10", VatRate.ReverseCharge);   // odwrotne obciążenie
    }

    /// <summary>Adnotacje - wszystkie pola obowiązkowe; wartość „2” oznacza „nie dotyczy”.</summary>
    private static void WriteAnnotations(XmlWriter w, Invoice invoice, List<InvoiceItem> items)
    {
        w.WriteStartElement("Adnotacje", Namespace);

        Element(w, "P_16", "2");  // metoda kasowa - nie
        Element(w, "P_17", "2");  // samofakturowanie - nie
        Element(w, "P_18", items.Any(i => i.VatRate == VatRate.ReverseCharge) ? "1" : "2"); // odwrotne obciążenie
        Element(w, "P_18A", "2"); // mechanizm podzielonej płatności - nieobsługiwany w MVP

        w.WriteStartElement("Zwolnienie", Namespace);
        if (items.Any(i => i.VatRate == VatRate.Exempt))
        {
            Element(w, "P_19", "1");
            Element(w, "P_19A", invoice.ExemptionBasis!.Trim()); // przepis ustawy, np. „art. 113 ust. 1 ustawy o VAT”
        }
        else
        {
            Element(w, "P_19N", "1");
        }
        w.WriteEndElement();

        w.WriteStartElement("NoweSrodkiTransportu", Namespace);
        Element(w, "P_22N", "1");
        w.WriteEndElement();

        Element(w, "P_23", "2");  // procedura uproszczona (transakcja trójstronna) - nie

        w.WriteStartElement("PMarzy", Namespace);
        Element(w, "P_PMarzyN", "1");
        w.WriteEndElement();

        w.WriteEndElement();
    }

    private static void WriteLine(XmlWriter w, int number, InvoiceItem item, LineResult line)
    {
        w.WriteStartElement("FaWiersz", Namespace);

        Element(w, "NrWierszaFa", number.ToString(CultureInfo.InvariantCulture));
        Element(w, "P_7", item.Name);
        if (!string.IsNullOrWhiteSpace(item.Unit)) Element(w, "P_8A", item.Unit);
        Element(w, "P_8B", Decimal(item.Quantity, 6));
        Element(w, "P_9A", Decimal(item.UnitNetPrice, 8));
        Element(w, "P_11", Money(line.NetAmount));
        Element(w, "P_12", item.VatRate.ToFaCode());
        if (item.BeforeCorrection) Element(w, "StanPrzed", "1"); // wiersz stanu przed korektą

        w.WriteEndElement();
    }

    private static void WritePayment(XmlWriter w, Invoice invoice)
    {
        w.WriteStartElement("Platnosc", Namespace);

        if (invoice.DueDate is { } due)
        {
            w.WriteStartElement("TerminPlatnosci", Namespace);
            Element(w, "Termin", Date(due));
            w.WriteEndElement();
        }

        Element(w, "FormaPlatnosci", ((int)invoice.PaymentMethod).ToString(CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(invoice.BankAccountNumber))
        {
            w.WriteStartElement("RachunekBankowy", Namespace);
            Element(w, "NrRB", invoice.BankAccountNumber);
            w.WriteEndElement();
        }

        w.WriteEndElement();
    }

    // ------------------------------------------------------------------ formatowanie (niezależne od kultury)

    private static void Element(XmlWriter w, string name, string value) => w.WriteElementString(name, Namespace, value);

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Kwota z dokładnie dwoma miejscami po przecinku i kropką dziesiętną (typ TKwotowy).</summary>
    private static string Money(decimal value) => InvoiceCalculator.RoundMoney(value).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Liczba z maksymalnie <paramref name="decimals"/> miejscami po przecinku, bez zbędnych zer.</summary>
    private static string Decimal(decimal value, int decimals) =>
        Math.Round(value, decimals).ToString("0." + new string('#', decimals), CultureInfo.InvariantCulture);
}
