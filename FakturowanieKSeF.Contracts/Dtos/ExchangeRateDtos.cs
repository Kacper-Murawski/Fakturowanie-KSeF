namespace FakturowanieKSeF.Contracts.Dtos;

/// <summary>Kurs średni NBP użyty do przeliczenia faktury w walucie obcej (D43).</summary>
public sealed record ExchangeRateDto(string Currency, decimal Rate, DateOnly EffectiveDate, string TableNo);
