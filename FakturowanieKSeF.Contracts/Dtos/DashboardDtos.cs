namespace FakturowanieKSeF.Contracts.Dtos;

/// <summary>Dane wykresów pulpitu (D40). Kwoty w PLN.</summary>
public sealed record DashboardDto(
    IReadOnlyList<MonthlyTotalsDto> Months,
    IReadOnlyList<AgingBucketDto> ReceivablesAging,
    IReadOnlyList<TopCustomerDto> TopCustomers);

/// <summary>Sprzedaż (faktury przekazane do KSeF, z korektami) i koszty (faktury otrzymane) w miesiącu - netto.</summary>
public sealed record MonthlyTotalsDto(int Year, int Month, decimal SalesNet, decimal CostsNet);

/// <summary>Należności (pozostało do zapłaty) w przedziale dni po terminie.</summary>
public sealed record AgingBucketDto(string Label, decimal Amount, int Count);

/// <summary>Kontrahent z największą sprzedażą netto w okresie wykresu.</summary>
public sealed record TopCustomerDto(string Name, string? Nip, decimal SalesNet, int Count);
