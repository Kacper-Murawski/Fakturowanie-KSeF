using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace FakturowanieKSeF.Api.Infrastructure;

/// <summary>
/// Limity liczby żądań (D47). Uzupełniają blokadę konta po 5 nieudanych logowaniach (D3), która chroni pojedyncze konto,
/// o ochronę przed atakami z jednego adresu na wiele kont (credential stuffing), masową rejestracją i zgadywaniem
/// tokenów zaproszeń:
/// <list type="bullet">
/// <item>„auth” - logowanie, rejestracja, odświeżanie tokenu, przyjmowanie zaproszeń: limit na adres IP na minutę,</item>
/// <item>globalny - wszystkie żądania API: limit na użytkownika (zalogowany) lub adres IP na minutę.</item>
/// </list>
/// Za odwrotnym proxy adres IP klienta pochodzi z nagłówka X-Forwarded-For (UseForwardedHeaders).
/// </summary>
public static class RateLimits
{
    public const string Auth = "auth";

    /// <summary>Ustawienia czytane przy żądaniu (a nie przy starcie) - testy mogą je nadpisać.</summary>
    private static (bool Enabled, int Auth, int Api) Settings(HttpContext http)
    {
        var c = http.RequestServices.GetRequiredService<IConfiguration>();
        return (c.GetValue("RateLimiting:Enabled", true), c.GetValue("RateLimiting:AuthPerMinute", 10), c.GetValue("RateLimiting:ApiPerMinute", 600));
    }

    public static IServiceCollection AddApiRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                await Results.Problem(title: "Zbyt wiele żądań. Spróbuj ponownie za chwilę.", statusCode: StatusCodes.Status429TooManyRequests)
                    .ExecuteAsync(context.HttpContext);
            };

            options.AddPolicy(Auth, http => Settings(http) is { Enabled: true } s
                ? RateLimitPartition.GetFixedWindowLimiter($"auth:{ClientIp(http)}", _ => Window(s.Auth))
                : RateLimitPartition.GetNoLimiter("off"));

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                Settings(http) is not { Enabled: true } s || !http.Request.Path.StartsWithSegments("/api")
                    ? RateLimitPartition.GetNoLimiter("off")
                    : RateLimitPartition.GetFixedWindowLimiter(
                        http.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } user ? $"user:{user}" : $"ip:{ClientIp(http)}",
                        _ => Window(s.Api)));
        });

    private static FixedWindowRateLimiterOptions Window(int permits) => new()
    {
        PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
    };

    private static string ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "nieznany";
}
