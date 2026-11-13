using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Api.Services;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Ksef;
using FakturowanieKSeF.Models;

namespace FakturowanieKSeF.Api.Endpoints;

/// <summary>
/// Konfiguracja integracji firmy z KSeF (token). Token jest szyfrowany przed zapisem i nigdy nie wraca do klienta.
/// </summary>
public static class KsefSettingsEndpoints
{
    public static void MapKsefSettingsEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/company/ksef").WithTags("KSeF").RequireAuthorization()
            .RequireRoleForChanges(CompanyRole.Owner); // token KSeF - tylko właściciel (D46)

        group.MapGet("/", async (CurrentCompany current, CancellationToken ct) =>
                await current.FindAsync(ct) is { } company
                    ? Results.Ok(ToDto(company))
                    : ApiErrors.CompanyRequired())
            .WithSummary("Stan integracji z KSeF (czy token jest skonfigurowany)");

        group.MapPut("/token", async (SaveKsefTokenRequest request, CurrentCompany current, AppDbContext db,
                IKsefGateway gateway, KsefTokenProtector protector, KsefTokenLimiter limiter, TimeProvider time, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();
                if (limiter.TryAcquire(company.Id) is { } tooMany) return tooMany;

                var token = request.Token.Trim();
                try
                {
                    // Token jest sprawdzany w KSeF przed zapisem - użytkownik od razu wie, czy jest poprawny
                    await gateway.VerifyTokenAsync(company.Nip, token, ct);
                }
                catch (KsefAuthenticationException ex)
                {
                    return ApiErrors.Validation(nameof(request.Token), ex.Message);
                }

                Store(company, token, protector, time);
                await db.SaveChangesAsync(ct);
                return Results.Ok(ToDto(company));
            })
            .WithSummary("Zapis tokenu KSeF (np. wygenerowanego w Aplikacji Podatnika) po weryfikacji w KSeF");

        group.MapPost("/test-token", async (CurrentCompany current, AppDbContext db, IKsefGateway gateway,
                KsefTokenProtector protector, KsefTokenLimiter limiter, ILoggerFactory loggers, TimeProvider time, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();
                if (limiter.TryAcquire(company.Id) is { } tooMany) return tooMany;

                string token;
                try
                {
                    // Możliwe wyłącznie na środowisku testowym KSeF (samopodpisany certyfikat) - D8, D24
                    token = await gateway.GenerateTestTokenAsync(company.Nip, ct);
                }
                catch (Exception ex) when (ex is KsefAuthenticationException or HttpRequestException)
                {
                    // Szczegóły (adresy, komunikaty sieci) tylko w logu serwera - użytkownik dostaje ogólny opis (SEC-8)
                    loggers.CreateLogger("KsefSettings").LogWarning(ex, "Nie udało się wygenerować tokenu testowego KSeF dla firmy {CompanyId}", company.Id);
                    return Results.Problem(title: "Nie udało się wygenerować tokenu w testowym KSeF.",
                        detail: ex is HttpRequestException
                            ? "Brak połączenia z testowym KSeF. Spróbuj ponownie za chwilę."
                            : "Testowy KSeF odrzucił prośbę o token. Spróbuj ponownie za chwilę.",
                        statusCode: StatusCodes.Status502BadGateway);
                }

                Store(company, token, protector, time);
                await db.SaveChangesAsync(ct);
                return Results.Ok(ToDto(company));
            })
            .WithSummary("Wygenerowanie tokenu w testowym KSeF dla NIP firmy (tylko środowisko testowe)");

        group.MapDelete("/token", async (CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                company.KsefTokenEncrypted = null;
                company.KsefTokenUpdatedAt = null;
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .WithSummary("Usunięcie tokenu KSeF");
    }

    private static void Store(Company company, string token, KsefTokenProtector protector, TimeProvider time)
    {
        company.KsefTokenEncrypted = protector.Protect(token);
        company.KsefTokenUpdatedAt = time.GetUtcNow().UtcDateTime;
    }

    private static KsefSettingsDto ToDto(Company company) =>
        new(company.KsefTokenEncrypted is not null, company.KsefTokenUpdatedAt, "Testowe (api-test.ksef.mf.gov.pl)");
}
