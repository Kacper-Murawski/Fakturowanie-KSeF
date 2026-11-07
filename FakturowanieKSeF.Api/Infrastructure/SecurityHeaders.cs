namespace FakturowanieKSeF.Api.Infrastructure;

/// <summary>
/// Nagłówki bezpieczeństwa dla każdej odpowiedzi (D47):
/// <list type="bullet">
/// <item>Content-Security-Policy - przeglądarka uruchamia wyłącznie skrypty i WebAssembly z serwera aplikacji
///   (bez skryptów wstawionych w stronę i z obcych domen) - druga linia obrony przed XSS; zakaz osadzania w ramkach,</item>
/// <item>Referrer-Policy: no-referrer - adres strony (np. link zaproszenia z tokenem) nie wycieka do innych serwisów,</item>
/// <item>X-Content-Type-Options, X-Frame-Options, Permissions-Policy, Cross-Origin-Opener-Policy.</item>
/// </list>
/// Swagger (tylko w środowisku deweloperskim) używa skryptów w stronie - nie dostaje CSP.
/// </summary>
public static class SecurityHeaders
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' 'wasm-unsafe-eval'; " +   // Blazor WebAssembly: kompilacja modułu .wasm
        "style-src 'self' 'unsafe-inline'; " +        // atrybuty style w komponentach (np. szerokość pasków wykresów)
        "img-src 'self' data:; " +                    // kod QR 2FA jako obraz data:
        "font-src 'self'; connect-src 'self'; worker-src 'self'; manifest-src 'self'; " +
        "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                if (!context.Request.Path.StartsWithSegments("/swagger"))
                    headers.ContentSecurityPolicy = ContentSecurityPolicy;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                return Task.CompletedTask;
            });
            await next();
        });
}
