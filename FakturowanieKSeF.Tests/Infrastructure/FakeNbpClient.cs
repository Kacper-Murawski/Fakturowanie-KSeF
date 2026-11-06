using FakturowanieKSeF.Api.Integrations.Nbp;

namespace FakturowanieKSeF.Tests.Infrastructure;

/// <summary>
/// Atrapa API NBP: notowania w dni robocze (pon.-pt., z wyjątkiem podanych świąt), stały kurs na walutę.
/// Licznik zapytań pozwala sprawdzić pamięć podręczną.
/// </summary>
public sealed class FakeNbpClient : INbpClient
{
    public Dictionary<string, decimal> Mid { get; } = new() { ["EUR"] = 4.3916m, ["USD"] = 3.9871m, ["GBP"] = 5.1234m };

    public HashSet<DateOnly> Holidays { get; } = [new(2026, 11, 11), new(2026, 12, 24), new(2026, 12, 25), new(2026, 12, 26)];

    public Exception? Failure { get; set; }

    public int Calls { get; private set; }

    public Task<IReadOnlyList<NbpRate>> GetRatesAsync(string currency, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        Calls++;
        if (Failure is not null) throw Failure;
        if (!Mid.TryGetValue(currency.ToUpperInvariant(), out var mid)) return Task.FromResult<IReadOnlyList<NbpRate>>([]);

        var list = new List<NbpRate>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || Holidays.Contains(d)) continue;
            list.Add(new NbpRate(currency.ToUpperInvariant(), mid, d, $"{d.DayOfYear}/A/NBP/{d.Year}"));
        }
        return Task.FromResult<IReadOnlyList<NbpRate>>(list);
    }
}
