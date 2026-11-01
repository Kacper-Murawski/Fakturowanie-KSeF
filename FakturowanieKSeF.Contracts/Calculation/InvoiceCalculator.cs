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
}
