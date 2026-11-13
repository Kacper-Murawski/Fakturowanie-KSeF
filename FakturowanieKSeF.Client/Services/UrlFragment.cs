namespace FakturowanieKSeF.Client.Services;

/// <summary>
/// Parametry z fragmentu adresu (<c>#token=…&amp;code=…</c>). Linki z wiadomości e-mail przenoszą kody we fragmencie,
/// bo przeglądarka nie wysyła go do serwera - kody nie trafiają do logów serwera i proxy (SEC-9, D53).
/// Dla zgodności ze starszymi linkami czytany jest też zwykły parametr zapytania (<c>?token=…</c>).
/// </summary>
public static class UrlFragment
{
    public static string? Get(string uri, string name)
    {
        var parsed = new Uri(uri);
        foreach (var part in new[] { parsed.Fragment.TrimStart('#'), parsed.Query.TrimStart('?') })
        {
            foreach (var pair in part.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq > 0 && Uri.UnescapeDataString(pair[..eq]) == name)
                    return Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            }
        }
        return null;
    }
}
