namespace FakturowanieKSeF.Api.Infrastructure;

/// <summary>
/// Nagłówki pamięci podręcznej dla plików klienta. Pliki bez odcisku w nazwie (index.html, css/app.css, js/*.js,
/// manifest) dostają <c>no-cache</c>: przeglądarka może je trzymać, ale przed użyciem sprawdza ETag (odpowiedź 304
/// bez treści, gdy plik się nie zmienił). Bez tego po aktualizacji aplikacji przeglądarka używała starego index.html
/// i arkusza stylów razem z nowym kodem Blazora (D50).
/// </summary>
public static class StaticFileCaching
{
    private static readonly string[] Revalidated = [".html", ".css", ".js", ".webmanifest", ".json"];

    public static void Apply(HttpContext context, string fileName)
    {
        // Pliki frameworka Blazora (_framework) mają własne sumy kontrolne i obsługę wersji
        if (context.Request.Path.StartsWithSegments("/_framework")) return;
        if (Revalidated.Any(ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            context.Response.Headers.CacheControl = "no-cache";
    }
}
