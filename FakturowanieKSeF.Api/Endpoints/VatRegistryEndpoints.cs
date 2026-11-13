using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Api.Integrations.VatWhiteList;
using FakturowanieKSeF.Contracts.Validation;

namespace FakturowanieKSeF.Api.Endpoints;

/// <summary>
/// Wyszukiwanie podmiotów w wykazie podatników VAT (Biała lista MF) - podpowiadanie danych kontrahenta
/// i firmy na podstawie NIP. Dane wykazu są publiczne, więc endpoint nie dotyczy danych żadnej firmy
/// w aplikacji; wymaga jednak zalogowania, bo zużywa wspólny limit zapytań do API MF.
/// </summary>
public static class VatRegistryEndpoints
{
    public static void MapVatRegistryEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/vat-registry").WithTags("Wykaz podatników VAT").RequireAuthorization();

        group.MapGet("/{nip}", async (string nip, CurrentCompany current, VatRegistryLookup lookup, HttpContext http, CancellationToken ct) =>
            {
                if (!NipValidator.IsValid(nip)) return ApiErrors.Validation("Nip", "Nieprawidłowy numer NIP.");

                try
                {
                    var entry = await lookup.FindAsync(NipValidator.Normalize(nip), current.UserId, RateLimits.ClientIp(http), ct);
                    return entry is null
                        ? Results.Problem(
                            title: "Podmiotu o tym numerze NIP nie ma w wykazie podatników VAT.",
                            detail: "Wykaz obejmuje podatników VAT czynnych i zwolnionych. Wpisz dane ręcznie.",
                            statusCode: StatusCodes.Status404NotFound)
                        : Results.Ok(entry);
                }
                catch (VatRegistryLimitException e)
                {
                    return Results.Problem(title: e.Message, statusCode: StatusCodes.Status429TooManyRequests);
                }
                catch (VatWhiteListException e)
                {
                    return Results.Problem(title: e.Message, statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .WithSummary("Dane podmiotu z wykazu podatników VAT (Biała lista MF) według NIP");
    }
}
