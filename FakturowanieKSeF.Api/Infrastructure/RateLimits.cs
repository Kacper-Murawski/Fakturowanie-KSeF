using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace FakturowanieKSeF.Api.Infrastructure;

/// <summary>
/// Limity liczby żądań (D47, D52). Uzupełniają blokadę konta po 5 nieudanych próbach (D3), która chroni pojedyncze konto,
/// o ochronę przed atakami z jednego adresu na wiele kont (credential stuffing), masową rejestracją i zgadywaniem
/// tokenów zaproszeń oraz kodów 2FA:
/// <list type="bullet">
/// <item>„auth” - wrażliwe operacje bez sesji (logowanie, rejestracja, odświeżanie tokenu, reset hasła, zaproszenia,
///   zmiana hasła): limit na adres IP na minutę. Sprawdzenie sesji (<c>GET manage/info</c>) i wylogowanie NIE zużywają
///   tej puli - inaczej osoby za wspólnym adresem (biuro, NAT) blokowałyby sobie logowanie odświeżaniem strony (SEC-3),</item>
/// <item>logowanie - dodatkowo limit na parę „adres IP + e-mail” (<see cref="LoginLimiter"/>),</item>
/// <item>„2fa” - operacje wymagające kodu 2FA na zalogowanym koncie: limit na użytkownika (SEC-1),</item>
/// <item>globalny - wszystkie żądania API: limit na użytkownika (zalogowany) lub adres IP na minutę.</item>
/// </list>
/// Za odwrotnym proxy adres IP klienta pochodzi z nagłówka X-Forwarded-For (UseForwardedHeaders).
/// </summary>
public static class RateLimits
{
    public const string Auth = "auth";
    public const string TwoFactor = "2fa";

    /// <summary>Ścieżki pod <c>/api/auth</c>, które NIE podlegają limitowi „auth” (zużywa je zwykłe używanie aplikacji).</summary>
    private static bool IsSessionCheck(HttpRequest request) =>
        request.Path.StartsWithSegments("/api/auth/logout")
        || (HttpMethods.IsGet(request.Method) && request.Path.StartsWithSegments("/api/auth/manage/info"));

    /// <summary>Ustawienia czytane przy żądaniu (a nie przy starcie) - testy mogą je nadpisać.</summary>
    private static (bool Enabled, int Auth, int Api, int TwoFactor) Settings(HttpContext http)
    {
        var c = http.RequestServices.GetRequiredService<IConfiguration>();
        return (c.GetValue("RateLimiting:Enabled", true), c.GetValue("RateLimiting:AuthPerMinute", 30),
            c.GetValue("RateLimiting:ApiPerMinute", 600), c.GetValue("RateLimiting:TwoFactorPerMinute", 5));
    }

    public static IServiceCollection AddApiRateLimits(this IServiceCollection services)
    {
        services.AddSingleton<LoginLimiter>();
        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                await TooManyRequests().ExecuteAsync(context.HttpContext);
            };

            options.AddPolicy(Auth, http => Settings(http) is { Enabled: true } s && !IsSessionCheck(http.Request)
                ? RateLimitPartition.GetFixedWindowLimiter($"auth:{ClientIp(http)}", _ => Window(s.Auth))
                : RateLimitPartition.GetNoLimiter("off"));

            options.AddPolicy(TwoFactor, http => Settings(http) is { Enabled: true } s
                ? RateLimitPartition.GetFixedWindowLimiter(
                    $"2fa:{http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ClientIp(http)}", _ => Window(s.TwoFactor))
                : RateLimitPartition.GetNoLimiter("off"));

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                Settings(http) is not { Enabled: true } s || !http.Request.Path.StartsWithSegments("/api")
                    ? RateLimitPartition.GetNoLimiter("off")
                    : RateLimitPartition.GetFixedWindowLimiter(
                        http.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } user ? $"user:{user}" : $"ip:{ClientIp(http)}",
                        _ => Window(s.Api)));
        });
    }

    /// <summary>Limit logowań na parę „adres IP + e-mail” - działa przed wbudowanym <c>/api/auth/login</c>.</summary>
    public static IApplicationBuilder UseLoginRateLimit(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path.StartsWithSegments("/api/auth/login")
                && Settings(context).Enabled)
            {
                var email = await ReadEmailAsync(context.Request);
                var limiter = context.RequestServices.GetRequiredService<LoginLimiter>();
                using var lease = limiter.Acquire($"{ClientIp(context)}|{email}");
                if (!lease.IsAcquired)
                {
                    if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                        context.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                    await TooManyRequests().ExecuteAsync(context);
                    return;
                }
            }
            await next();
        });

    /// <summary>E-mail z treści żądania logowania (treść jest buforowana - endpoint Identity odczyta ją ponownie).</summary>
    private static async Task<string> ReadEmailAsync(HttpRequest request)
    {
        request.EnableBuffering();
        try
        {
            using var json = await JsonDocument.ParseAsync(request.Body);
            return json.RootElement.ValueKind == JsonValueKind.Object
                   && json.RootElement.TryGetProperty("email", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString()!.Trim().ToLowerInvariant()
                : "";
        }
        catch (JsonException)
        {
            return "";
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    private static IResult TooManyRequests() =>
        Results.Problem(title: "Zbyt wiele żądań. Spróbuj ponownie za chwilę.", statusCode: StatusCodes.Status429TooManyRequests);

    private static FixedWindowRateLimiterOptions Window(int permits) => new()
    {
        PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
    };

    public static string ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "nieznany";
}

/// <summary>
/// Liczniki logowań na parę „adres IP + e-mail” (domyślnie 10 na minutę, <c>RateLimiting:LoginPerMinute</c>).
/// Ogranicza zgadywanie haseł jednego konta z jednego adresu niezależnie od wyższego limitu na sam adres IP.
/// </summary>
public sealed class LoginLimiter(IConfiguration configuration) : IDisposable
{
    private readonly ConcurrentDictionary<int, PartitionedRateLimiter<string>> _limiters = new();

    public RateLimitLease Acquire(string key)
    {
        // Limit czytany przy żądaniu (testy mogą go zmienić); osobny limiter dla każdej wartości limitu
        var permits = configuration.GetValue("RateLimiting:LoginPerMinute", 10);
        var limiter = _limiters.GetOrAdd(permits, p => PartitionedRateLimiter.Create<string, string>(k =>
            RateLimitPartition.GetFixedWindowLimiter(k, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = p, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
            })));
        return limiter.AttemptAcquire(key);
    }

    public void Dispose()
    {
        foreach (var limiter in _limiters.Values) limiter.Dispose();
    }
}
