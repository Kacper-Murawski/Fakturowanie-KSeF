namespace FakturowanieKSeF.Api.Infrastructure;

/// <summary>
/// Data w strefie czasowej Polski - wykaz MF, KSeF i terminy płatności operują na dniach kalendarzowych
/// czasu polskiego (serwer może działać w UTC).
/// </summary>
public static class PolishDate
{
    private static readonly TimeZoneInfo Zone = FindZone();

    public static DateOnly Today(TimeProvider time) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), Zone).DateTime);

    private static TimeZoneInfo FindZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc; // brak bazy stref czasowych w systemie - dzień według UTC
        }
    }
}
