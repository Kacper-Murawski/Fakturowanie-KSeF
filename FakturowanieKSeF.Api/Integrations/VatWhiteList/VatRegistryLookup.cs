using FakturowanieKSeF.Contracts.Dtos;
using Microsoft.Extensions.Caching.Memory;

namespace FakturowanieKSeF.Api.Integrations.VatWhiteList;

/// <summary>Przekroczono dzienny limit zapytań do wykazu (całej aplikacji albo jednego użytkownika).</summary>
public sealed class VatRegistryLimitException(string message) : Exception(message);

/// <summary>
/// Wyszukiwanie podmiotów w wykazie podatników VAT na potrzeby formularzy. API MF ogranicza liczbę zapytań
/// wyszukujących na dobę dla adresu IP - w modelu SaaS jest to adres serwera, wspólny dla wszystkich użytkowników.
/// Dlatego wyniki są przechowywane w pamięci podręcznej do końca dnia, a liczba zapytań ograniczona
/// (całkowita i na użytkownika), żeby jeden użytkownik nie wyczerpał limitu pozostałym (decyzja D28).
/// </summary>
public sealed class VatRegistryLookup(
    IVatWhiteListClient client,
    VatRegistryLimiter limiter,
    IMemoryCache cache,
    TimeProvider time)
{
    public async Task<VatRegistryEntryDto?> FindAsync(string nip, string userId, CancellationToken ct = default)
    {
        var today = PolishDate.Today(time);
        var key = $"vat-registry:{nip}:{today:yyyy-MM-dd}";
        if (cache.TryGetValue(key, out VatRegistryEntryDto? cached)) return cached;

        limiter.Acquire(VatRegistryLimiter.Search, userId, today);
        var subject = await client.FindByNipAsync(nip, today, ct);
        var entry = subject is null ? null : ToDto(subject, today);

        // Także brak podmiotu w wykazie - ponowne pytanie tego samego dnia dałoby ten sam wynik
        cache.Set(key, entry, TimeSpan.FromHours(12));
        return entry;
    }

    /// <summary>
    /// Sprawdzenie rachunku w wykazie na dziś. Rachunek musi być polskim numerem NRB (26 cyfr, opcjonalnie z „PL”);
    /// dla innych zwraca <c>null</c> - wykaz obejmuje wyłącznie rachunki prowadzone w Polsce.
    /// </summary>
    public async Task<BankAccountCheckDto?> CheckAccountAsync(string nip, string account, string userId, CancellationToken ct = default)
    {
        if (ToNrb(account) is not { } nrb) return null;

        var today = PolishDate.Today(time);
        var key = $"vat-registry-check:{nip}:{nrb}:{today:yyyy-MM-dd}";
        if (cache.TryGetValue(key, out BankAccountCheckDto? cached)) return cached;

        limiter.Acquire(VatRegistryLimiter.Check, userId, today);
        var check = await client.CheckBankAccountAsync(nip, nrb, today, ct);
        var result = new BankAccountCheckDto(nrb, check.Assigned, today, check.RequestId);
        cache.Set(key, result, TimeSpan.FromHours(12));
        return result;
    }

    /// <summary>„PL61 1090 1014 …” → „611090…” (26 cyfr) albo <c>null</c>, gdy to nie jest polski numer NRB.</summary>
    public static string? ToNrb(string account)
    {
        var s = new string(account.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        if (s.StartsWith("PL")) s = s[2..];
        return s.Length == 26 && s.All(char.IsAsciiDigit) ? s : null;
    }

    public static VatRegistryEntryDto ToDto(WhiteListSubject s, DateOnly checkedOn)
    {
        // Adres siedziby (firmy) albo zamieszkania (osoby fizyczne bez stałego miejsca prowadzenia działalności)
        var registryAddress = s.WorkingAddress ?? s.ResidenceAddress;
        var status = s.StatusVat ?? "Niezarejestrowany";

        return new VatRegistryEntryDto(
            s.Name, s.Nip, status, status == "Czynny", s.Regon, s.Krs,
            WhiteListAddress.Parse(registryAddress), registryAddress,
            s.AccountNumbers, checkedOn, s.RequestId);
    }
}

/// <summary>
/// Dzienne limity zapytań do wykazu (w pamięci jednej instancji aplikacji), osobno dla wyszukiwania podmiotów
/// i sprawdzania rachunków - MF stosuje dla tych metod różne limity.
/// </summary>
public sealed class VatRegistryLimiter(IConfiguration config)
{
    public const string Search = "Search";
    public const string Check = "Check";

    private readonly Lock _lock = new();
    private readonly Dictionary<(string Kind, string UserId), int> _perUser = [];
    private readonly Dictionary<string, int> _total = [];
    private DateOnly _day;

    private (int Total, int PerUser) Limits(string kind) => kind == Search
        ? (config.GetValue("VatWhiteList:DailyLimit", 90), config.GetValue("VatWhiteList:DailyLimitPerUser", 20))
        : (config.GetValue("VatWhiteList:DailyCheckLimit", 2000), config.GetValue("VatWhiteList:DailyCheckLimitPerUser", 200));

    public void Acquire(string kind, string userId, DateOnly today)
    {
        var (totalLimit, userLimit) = Limits(kind);
        lock (_lock)
        {
            if (_day != today)
            {
                _day = today;
                _total.Clear();
                _perUser.Clear();
            }

            var used = _perUser.GetValueOrDefault((kind, userId));
            if (used >= userLimit)
                throw new VatRegistryLimitException(
                    "Wykorzystano dzienny limit sprawdzeń w wykazie podatników VAT. Uzupełnij dane ręcznie lub spróbuj jutro.");
            if (_total.GetValueOrDefault(kind) >= totalLimit)
                throw new VatRegistryLimitException(
                    "Dzienny limit zapytań aplikacji do wykazu podatników VAT został wyczerpany. Spróbuj jutro.");

            _perUser[(kind, userId)] = used + 1;
            _total[kind] = _total.GetValueOrDefault(kind) + 1;
        }
    }
}

/// <summary>Data w strefie czasowej Polski - wykaz MF i KSeF operują na dniach kalendarzowych czasu polskiego.</summary>
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
