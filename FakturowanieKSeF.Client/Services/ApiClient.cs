using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace FakturowanieKSeF.Client.Services;

/// <summary>Błąd zwrócony przez API (ProblemDetails / ValidationProblemDetails).</summary>
public sealed class ApiProblem
{
    public int Status { get; init; }
    public string Title { get; init; } = "Wystąpił błąd.";
    public string? Detail { get; init; }

    /// <summary>Błędy walidacji per pole, np. "Address.PostalCode", "Items[1].Quantity".</summary>
    public Dictionary<string, string[]> Errors { get; init; } = [];

    public static ApiProblem Network() => new() { Status = 0, Title = "Brak połączenia z serwerem. Spróbuj ponownie." };
}

/// <summary>Wynik wywołania API: wartość albo opis błędu.</summary>
public sealed class ApiResult<T>
{
    public T? Value { get; init; }
    public ApiProblem? Problem { get; init; }
    public bool Ok => Problem is null;
}

/// <summary>
/// Klient REST API aplikacji. Uwierzytelnianie odbywa się ciasteczkiem sesji (ta sama domena),
/// więc przeglądarka dołącza je automatycznie - kod klienta nie ma dostępu do żadnego tokenu.
/// </summary>
public sealed class ApiClient(HttpClient http, NavigationManager navigation)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<ApiResult<T>> GetAsync<T>(string url) => SendAsync<T>(() => http.GetAsync(url));

    public Task<ApiResult<T>> PostAsync<T>(string url, object body) => SendAsync<T>(() => http.PostAsJsonAsync(url, body, Json));

    public Task<ApiResult<T>> PutAsync<T>(string url, object body) => SendAsync<T>(() => http.PutAsJsonAsync(url, body, Json));

    public Task<ApiResult<bool>> DeleteAsync(string url) => SendAsync<bool>(() => http.DeleteAsync(url));

    private async Task<ApiResult<T>> SendAsync<T>(Func<Task<HttpResponseMessage>> send)
    {
        HttpResponseMessage response;
        try
        {
            response = await send();
        }
        catch (HttpRequestException)
        {
            return new ApiResult<T> { Problem = ApiProblem.Network() };
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Sesja wygasła - przejście do logowania z powrotem na bieżącą stronę
            var returnUrl = Uri.EscapeDataString(navigation.ToBaseRelativePath(navigation.Uri));
            navigation.NavigateTo($"login?returnUrl={returnUrl}", forceLoad: false);
            return new ApiResult<T> { Problem = new ApiProblem { Status = 401, Title = "Sesja wygasła. Zaloguj się ponownie." } };
        }

        if (!response.IsSuccessStatusCode)
            return new ApiResult<T> { Problem = await ReadProblemAsync(response) };

        if (typeof(T) == typeof(bool) || response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            return new ApiResult<T> { Value = default };

        return new ApiResult<T> { Value = await response.Content.ReadFromJsonAsync<T>(Json) };
    }

    public static async Task<ApiProblem> ReadProblemAsync(HttpResponseMessage response)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>(Json);
            return new ApiProblem
            {
                Status = (int)response.StatusCode,
                Title = problem?.Title ?? "Wystąpił błąd.",
                Detail = problem?.Detail,
                Errors = problem?.Errors ?? []
            };
        }
        catch (JsonException)
        {
            return new ApiProblem { Status = (int)response.StatusCode, Title = $"Błąd serwera ({(int)response.StatusCode})." };
        }
    }

    private sealed record ProblemPayload(string? Title, string? Detail, Dictionary<string, string[]>? Errors);
}
