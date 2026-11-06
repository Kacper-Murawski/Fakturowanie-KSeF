namespace FakturowanieKSeF.Contracts.Calculation;

/// <summary>Waluty faktur (D43): PLN i waluty z tabeli A kursów średnich NBP.</summary>
public static class Currencies
{
    public const string Pln = "PLN";

    public static readonly IReadOnlyList<string> Supported = [Pln, "EUR", "USD", "GBP", "CHF", "CZK", "SEK", "NOK", "DKK", "JPY", "CAD"];

    public static bool IsForeign(string currency) => currency != Pln;
}
