namespace FakturowanieKSeF.Contracts.Calculation;

/// <summary>Dane pozycji potrzebne do obliczeń.</summary>
public readonly record struct LineInput(decimal Quantity, decimal UnitNetPrice, VatRate VatRate);

public sealed record LineResult(decimal NetAmount, decimal VatAmount, decimal GrossAmount);

/// <summary>Podsumowanie dla jednej stawki VAT (FA(3): P_13_x - netto, P_14_x - podatek).</summary>
public sealed record VatRateSummary(VatRate VatRate, decimal Net, decimal Vat, decimal Gross);

public sealed record InvoiceTotals(
    IReadOnlyList<LineResult> Lines,
    IReadOnlyList<VatRateSummary> ByVatRate,
    decimal TotalNet,
    decimal TotalVat,
    decimal TotalGross);

/// <summary>
/// Kwoty faktury korygującej: stan przed i po korekcie oraz różnica per stawka (ujemna przy obniżce).
/// Różnica trafia do pól P_13_x / P_14_x / P_15 faktury korygującej.
/// </summary>
public sealed record CorrectionTotals(
    InvoiceTotals Before,
    InvoiceTotals After,
    IReadOnlyList<VatRateSummary> Difference,
    decimal TotalNet,
    decimal TotalVat,
    decimal TotalGross);

/// <summary>
/// Obliczanie kwot faktury metodą „od netto”. Ten sam kod liczy sumy w API i na żywo w formularzu Blazor,
/// więc użytkownik widzi dokładnie te kwoty, które zostaną zapisane i wysłane do KSeF.
/// <para>Zasady:</para>
/// <list type="number">
/// <item>wartość netto pozycji = ilość × cena netto, zaokrąglona do groszy (P_11),</item>
/// <item>podatek liczony jest od SUMY wartości netto w ramach danej stawki i zaokrąglany raz (P_14_x)
///   - zgodnie z praktyką i strukturą FA(3), w której kwoty podatku raportuje się per stawka,</item>
/// <item>VAT i brutto pozycji są informacyjne (zaokrąglane per pozycja), dlatego ich suma może różnić się
///   o grosze od podatku faktury; wiążące są kwoty per stawka i sumy faktury,</item>
/// <item>zaokrąglenie „połówkowe od zera” (0,005 → 0,01), stosowane w rozliczeniach podatkowych.</item>
/// </list>
/// </summary>
public static class InvoiceCalculator
{
    public static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Kwoty podatku w PLN dla faktury w walucie obcej (art. 106e ust. 11 ustawy o VAT, pola P_14_xW): podatek w walucie
    /// przeliczony kursem i zaokrąglony do grosza, osobno dla każdej stawki z podatkiem (D43).
    /// </summary>
    public static IReadOnlyList<VatRateSummary> VatInPln(IEnumerable<VatRateSummary> byVatRate, decimal exchangeRate) =>
        byVatRate.Where(s => s.VatRate.HasVatAmount())
            .Select(s => s with { Vat = RoundMoney(s.Vat * exchangeRate), Net = RoundMoney(s.Net * exchangeRate), Gross = RoundMoney(s.Gross * exchangeRate) })
            .ToList();

    public static LineResult CalculateLine(LineInput line)
    {
        var net = RoundMoney(line.Quantity * line.UnitNetPrice);
        var vat = RoundMoney(net * line.VatRate.GetPercentage());
        return new LineResult(net, vat, net + vat);
    }

    public static InvoiceTotals Calculate(IEnumerable<LineInput> lines)
    {
        var input = lines.ToList();
        var results = input.Select(CalculateLine).ToList();

        var byRate = input
            .Zip(results, (l, r) => (l.VatRate, r.NetAmount))
            .GroupBy(x => x.VatRate)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var net = g.Sum(x => x.NetAmount);
                var vat = RoundMoney(net * g.Key.GetPercentage());
                return new VatRateSummary(g.Key, net, vat, net + vat);
            })
            .ToList();

        var totalNet = byRate.Sum(s => s.Net);
        var totalVat = byRate.Sum(s => s.Vat);

        return new InvoiceTotals(results, byRate, totalNet, totalVat, totalNet + totalVat);
    }

    /// <summary>
    /// Korekta metodą „przed i po”: kwoty obu stanów liczone są tak samo jak na fakturze (podatek od sumy netto
    /// w stawce), a różnica per stawka to po - przed. Stawki występujące tylko w jednym ze stanów też są wykazywane.
    /// </summary>
    public static CorrectionTotals CalculateCorrection(IEnumerable<LineInput> before, IEnumerable<LineInput> after)
    {
        var b = Calculate(before);
        var a = Calculate(after);

        var difference = b.ByVatRate.Select(s => s.VatRate).Union(a.ByVatRate.Select(s => s.VatRate))
            .Order()
            .Select(rate =>
            {
                var x = b.ByVatRate.FirstOrDefault(s => s.VatRate == rate);
                var y = a.ByVatRate.FirstOrDefault(s => s.VatRate == rate);
                var net = (y?.Net ?? 0) - (x?.Net ?? 0);
                var vat = (y?.Vat ?? 0) - (x?.Vat ?? 0);
                return new VatRateSummary(rate, net, vat, net + vat);
            })
            .ToList();

        return new CorrectionTotals(b, a, difference,
            a.TotalNet - b.TotalNet, a.TotalVat - b.TotalVat, a.TotalGross - b.TotalGross);
    }
}
