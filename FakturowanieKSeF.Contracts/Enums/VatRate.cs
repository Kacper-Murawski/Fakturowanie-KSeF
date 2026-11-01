using System.Text.Json.Serialization;

namespace FakturowanieKSeF.Contracts;

/// <summary>
/// Stawka VAT pozycji - pełny zbiór wartości typu TStawkaPodatku schemy FA(3).
/// Nie jest to liczba: część „stawek” (zw, oo, np) oznacza brak naliczania podatku z różnych przyczyn prawnych,
/// a 0% występuje w trzech wariantach (kraj, WDT, eksport) raportowanych osobno.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<VatRate>))]
public enum VatRate
{
    Rate23,
    Rate22,
    Rate8,
    Rate7,
    Rate5,
    Rate4,
    Rate3,
    /// <summary>0% - sprzedaż krajowa (0 KR).</summary>
    Rate0Domestic,
    /// <summary>0% - wewnątrzwspólnotowa dostawa towarów (0 WDT).</summary>
    Rate0Wdt,
    /// <summary>0% - eksport towarów (0 EX).</summary>
    Rate0Export,
    /// <summary>Zwolnione od podatku (zw).</summary>
    Exempt,
    /// <summary>Odwrotne obciążenie (oo) - podatek rozlicza nabywca.</summary>
    ReverseCharge,
    /// <summary>Niepodlegające opodatkowaniu - dostawa towarów / świadczenie usług poza terytorium kraju (np I).</summary>
    NotSubjectI,
    /// <summary>Niepodlegające opodatkowaniu na terytorium kraju - świadczenie usług z art. 100 ust. 1 pkt 4 (np II).</summary>
    NotSubjectII
}

public static class VatRateExtensions
{
    /// <summary>Stawki dostępne przy wystawianiu faktury (bez ryczałtu dla taksówek: 4% i 3%).</summary>
    public static IReadOnlyList<VatRate> Selectable { get; } =
        Enum.GetValues<VatRate>().Where(r => r is not (VatRate.Rate4 or VatRate.Rate3)).ToArray();

    /// <summary>Kod stawki w schemie FA(3) (element P_12).</summary>
    public static string ToFaCode(this VatRate rate) => rate switch
    {
        VatRate.Rate23 => "23",
        VatRate.Rate22 => "22",
        VatRate.Rate8 => "8",
        VatRate.Rate7 => "7",
        VatRate.Rate5 => "5",
        VatRate.Rate4 => "4",
        VatRate.Rate3 => "3",
        VatRate.Rate0Domestic => "0 KR",
        VatRate.Rate0Wdt => "0 WDT",
        VatRate.Rate0Export => "0 EX",
        VatRate.Exempt => "zw",
        VatRate.ReverseCharge => "oo",
        VatRate.NotSubjectI => "np I",
        VatRate.NotSubjectII => "np II",
        _ => throw new ArgumentOutOfRangeException(nameof(rate), rate, null)
    };

    /// <summary>
    /// Stawka procentowa jako ułamek (0.23 dla 23%) używana do wyliczenia kwoty VAT.
    /// Dla stawek 0%, zw, oo i np zwraca 0 - podatek nie jest naliczany na fakturze.
    /// </summary>
    public static decimal GetPercentage(this VatRate rate) => rate switch
    {
        VatRate.Rate23 => 0.23m,
        VatRate.Rate22 => 0.22m,
        VatRate.Rate8 => 0.08m,
        VatRate.Rate7 => 0.07m,
        VatRate.Rate5 => 0.05m,
        VatRate.Rate4 => 0.04m,
        VatRate.Rate3 => 0.03m,
        _ => 0m
    };

    /// <summary>Nazwa stawki do wyświetlenia użytkownikowi.</summary>
    public static string ToDisplayName(this VatRate rate) => rate switch
    {
        VatRate.Rate0Domestic => "0% (kraj)",
        VatRate.Rate0Wdt => "0% (WDT)",
        VatRate.Rate0Export => "0% (eksport)",
        VatRate.Exempt => "zw",
        VatRate.ReverseCharge => "odwrotne obciążenie",
        VatRate.NotSubjectI => "np I",
        VatRate.NotSubjectII => "np II",
        _ => $"{rate.ToFaCode()}%"
    };
}
