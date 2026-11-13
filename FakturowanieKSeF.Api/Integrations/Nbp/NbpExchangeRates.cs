using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FakturowanieKSeF.Contracts.Dtos;
using Microsoft.Extensions.Caching.Memory;

namespace FakturowanieKSeF.Api.Integrations.Nbp;

/// <summary>Kurs średni NBP (tabela A) użyty do przeliczenia faktury.</summary>
public sealed record NbpRate(string Currency, decimal Mid, DateOnly EffectiveDate, string TableNo);

/// <summary>Błąd pobierania kursu (brak waluty w tabeli A, brak notowań, awaria API NBP).</summary>
public sealed class NbpException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Kursy średnie walut NBP - publiczne API <c>https://api.nbp.pl</c> (bez uwierzytelniania), tabela A (D43).
/// </summary>
public interface INbpClient
{
    /// <summary>Notowania kursu średniego waluty w przedziale dat (puste - brak notowań, np. dni wolne).</summary>
    Task<IReadOnlyList<NbpRate>> GetRatesAsync(string currency, DateOnly from, DateOnly to, CancellationToken ct = default);
}

public sealed class NbpClient(HttpClient http) : INbpClient
{
    public const string BaseAddress = "https://api.nbp.pl/";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<NbpRate>> GetRatesAsync(string currency, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var code = currency.ToLowerInvariant();
        var url = $"api/exchangerates/rates/a/{Uri.EscapeDataString(code)}/{D(from)}/{D(to)}/?format=json";
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(url, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new NbpException("Serwis kursów NBP jest niedostępny. Spróbuj ponownie za chwilę.", ex);
        }

        // 404 - brak notowań w przedziale lub waluta spoza tabeli A
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        if (!response.IsSuccessStatusCode)
            throw new NbpException($"Serwis kursów NBP zwrócił błąd ({(int)response.StatusCode}).");

        var body = await response.Content.ReadFromJsonAsync<Series>(Json, ct);
        return (body?.Rates ?? [])
            .Select(r => new NbpRate(body!.Code ?? currency.ToUpperInvariant(), r.Mid, DateOnly.ParseExact(r.EffectiveDate, "yyyy-MM-dd", CultureInfo.InvariantCulture), r.No))
            .ToList();
    }

    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record Series(string? Code, List<Rate>? Rates);
    private sealed record Rate(string No, string EffectiveDate, decimal Mid);
}

/// <summary>
/// Kurs do przeliczenia faktury w walucie obcej (art. 31a ustawy o VAT): kurs średni NBP z ostatniego dnia roboczego
/// poprzedzającego dzień powstania obowiązku podatkowego - w aplikacji dzień sprzedaży (lub wystawienia, gdy jest ten sam).
/// Wyniki w pamięci podręcznej (opublikowane kursy się nie zmieniają).
/// </summary>
public sealed class ExchangeRateLookup(INbpClient client, IMemoryCache cache)
{
    /// <summary>Pierwszy dzień notowań tabeli A w API NBP - wcześniejszych kursów API nie udostępnia (SEC-6).</summary>
    public static readonly DateOnly FirstTableDate = new(2002, 1, 2);

    /// <summary>Limit wpisów pamięci podręcznej kursów (osobna pamięć podręczna - nie rośnie bez końca, SEC-6).</summary>
    public const int CacheSizeLimit = 10_000;

    /// <summary>Brak kursu (np. data, dla której tabela nie jest jeszcze opublikowana) - zapamiętywany krótko (SEC-6).</summary>
    private sealed record Missing(string Message);

    public async Task<NbpRate> ForTaxPointAsync(string currency, DateOnly taxPoint, CancellationToken ct = default)
    {
        var code = currency.ToUpperInvariant();
        if (taxPoint <= FirstTableDate)
            throw new NbpException($"Brak kursu średniego NBP waluty {code} przed dniem {taxPoint:dd.MM.yyyy} - kursy są dostępne od {FirstTableDate:dd.MM.yyyy}.");

        var key = $"nbp:{code}:{taxPoint:yyyy-MM-dd}";
        if (cache.TryGetValue(key, out object? cached))
            return cached is Missing missing ? throw new NbpException(missing.Message) : (NbpRate)cached!;

        // Ostatni dzień roboczy przed datą: przedział 14 dni wystarcza na święta i długie weekendy
        var rates = await client.GetRatesAsync(code, taxPoint.AddDays(-14), taxPoint.AddDays(-1), ct);
        var rate = rates.Where(r => r.EffectiveDate < taxPoint).MaxBy(r => r.EffectiveDate);
        if (rate is null)
        {
            var message = $"Brak kursu średniego NBP waluty {code} przed dniem {taxPoint:dd.MM.yyyy}.";
            cache.Set(key, new Missing(message), new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15), Size = 1 });
            throw new NbpException(message);
        }

        cache.Set(key, rate, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12), Size = 1 });
        return rate;
    }

    public static ExchangeRateDto ToDto(NbpRate r) => new(r.Currency, r.Mid, r.EffectiveDate, r.TableNo);
}
