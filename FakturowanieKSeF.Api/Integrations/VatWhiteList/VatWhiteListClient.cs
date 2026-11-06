using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace FakturowanieKSeF.Api.Integrations.VatWhiteList;

/// <summary>
/// Wykaz podatników VAT („Biała lista”) prowadzony przez Ministerstwo Finansów - publiczne API
/// <c>https://wl-api.mf.gov.pl</c> (bez uwierzytelniania). Zwraca dane rejestrowe podmiotu (nazwa, adres,
/// status VAT) i zgłoszone rachunki bankowe na wskazany dzień.
/// </summary>
public interface IVatWhiteListClient
{
    /// <summary>Dane podmiotu o podanym NIP według stanu na dzień <paramref name="date"/>; <c>null</c>, gdy nie ma go w wykazie.</summary>
    Task<WhiteListSubject?> FindByNipAsync(string nip, DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// Czy rachunek (NRB, 26 cyfr) jest przypisany do podmiotu o podanym NIP w wykazie na dzień <paramref name="date"/>.
    /// Metoda MF uwzględnia rachunki wirtualne (maski), których nie da się sprawdzić porównaniem z listą rachunków.
    /// </summary>
    Task<WhiteListAccountCheck> CheckBankAccountAsync(string nip, string account, DateOnly date, CancellationToken ct = default);
}

/// <summary>Wynik metody <c>check</c> wykazu: rachunek przypisany („TAK”) lub nie („NIE”).</summary>
public sealed record WhiteListAccountCheck(bool Assigned, string? RequestId);

/// <summary>Podmiot z wykazu podatników VAT (pola w postaci zwracanej przez API MF).</summary>
public sealed record WhiteListSubject(
    string Name,
    string Nip,
    string? StatusVat,
    string? Regon,
    string? Krs,
    string? WorkingAddress,
    string? ResidenceAddress,
    IReadOnlyList<string> AccountNumbers,
    string? RequestId);

/// <summary>Błąd komunikacji z wykazem lub błąd zgłoszony przez API MF (komunikat do wyświetlenia użytkownikowi).</summary>
public sealed class VatWhiteListException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Klient HTTP API wykazu podatników VAT (metody <c>search/nip</c> i <c>check/nip/.../bank-account</c>).</summary>
public sealed class VatWhiteListClient(HttpClient http) : IVatWhiteListClient
{
    /// <summary>Adres produkcyjnego API wykazu. Wykaz ma tylko jedną wersję - dane publiczne, wyłącznie do odczytu.</summary>
    public const string BaseAddress = "https://wl-api.mf.gov.pl/";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<WhiteListSubject?> FindByNipAsync(string nip, DateOnly date, CancellationToken ct = default)
    {
        var url = $"api/search/nip/{Uri.EscapeDataString(nip)}?date={date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        var body = await GetAsync<SearchResponse>(url, ct);
        var s = body?.Result?.Subject;
        if (s is null) return null;

        return new WhiteListSubject(
            s.Name?.Trim() ?? "",
            s.Nip ?? nip,
            s.StatusVat,
            s.Regon,
            s.Krs,
            s.WorkingAddress,
            s.ResidenceAddress,
            s.AccountNumbers ?? [],
            body!.Result!.RequestId);
    }

    public async Task<WhiteListAccountCheck> CheckBankAccountAsync(string nip, string account, DateOnly date, CancellationToken ct = default)
    {
        var url = $"api/check/nip/{Uri.EscapeDataString(nip)}/bank-account/{Uri.EscapeDataString(account)}" +
                  $"?date={date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        var body = await GetAsync<CheckResponse>(url, ct);
        return body?.Result?.AccountAssigned switch
        {
            "TAK" => new WhiteListAccountCheck(true, body.Result.RequestId),
            "NIE" => new WhiteListAccountCheck(false, body.Result.RequestId),
            _ => throw new VatWhiteListException("Nieoczekiwana odpowiedź wykazu podatników VAT.")
        };
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(url, ct);
        }
        catch (HttpRequestException e)
        {
            throw new VatWhiteListException("Brak połączenia z wykazem podatników VAT. Spróbuj ponownie później.", e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new VatWhiteListException("Wykaz podatników VAT nie odpowiada. Spróbuj ponownie później.", e);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var error = await TryReadAsync<ErrorResponse>(response, ct);
                throw new VatWhiteListException(error?.Message is { Length: > 0 } message
                    ? $"Wykaz podatników VAT zgłosił błąd: {message}"
                    : $"Wykaz podatników VAT zwrócił błąd ({(int)response.StatusCode}).");
            }

            // Strona HTML zamiast JSON: zabezpieczenie MF przed automatycznymi zapytaniami zablokowało adres serwera
            // (zaobserwowane dla adresów centrów danych w chmurze) - D43
            if (response.Content.Headers.ContentType?.MediaType == "text/html")
                throw new VatWhiteListException(
                    "Wykaz podatników VAT odrzucił zapytanie serwera (zabezpieczenie przed automatycznymi zapytaniami). Uzupełnij dane ręcznie.");

            return await TryReadAsync<T>(response, ct)
                   ?? throw new VatWhiteListException("Nieoczekiwana odpowiedź wykazu podatników VAT.");
        }
    }

    private static async Task<T?> TryReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, ct);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            return default;
        }
    }

    // Struktura odpowiedzi API MF (specyfikacja: https://wl-api.mf.gov.pl, model EntityResponse / Exception)
    private sealed record SearchResponse(SearchResult? Result);
    private sealed record SearchResult(SubjectPayload? Subject, string? RequestId);
    private sealed record SubjectPayload(
        string? Name, string? Nip, string? StatusVat, string? Regon, string? Krs,
        string? WorkingAddress, string? ResidenceAddress, List<string>? AccountNumbers);
    private sealed record CheckResponse(CheckResult? Result);
    private sealed record CheckResult(string? AccountAssigned, string? RequestId);
    private sealed record ErrorResponse(string? Code, string? Message);
}
