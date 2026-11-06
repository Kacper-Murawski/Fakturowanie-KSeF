namespace FakturowanieKSeF.Contracts.Dtos;

/// <summary>
/// Podsumowanie okresu przed eksportem dla księgowej. Kwoty zakupów obejmują wyłącznie faktury w PLN
/// (faktury w walutach obcych są liczone osobno - <see cref="PurchasesForeignCurrency"/>).
/// </summary>
public sealed record ExportSummaryDto(
    DateOnly From,
    DateOnly To,
    int SalesCount,
    decimal SalesNet,
    decimal SalesVat,
    decimal SalesGross,
    int PurchasesCount,
    int PurchasesForeignCurrency,
    decimal PurchasesNet,
    decimal PurchasesVat,
    decimal PurchasesGross,
    /// <summary>Faktury sprzedaży w walutach obcych - w zestawieniu, ale poza sumami w PLN (D43).</summary>
    int SalesForeignCurrency = 0);
