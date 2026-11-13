using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Api.Integrations.Nbp;
using FakturowanieKSeF.Contracts.Calculation;

namespace FakturowanieKSeF.Api.Endpoints;

/// <summary>Podgląd kursu NBP w formularzu faktury (D43); ten sam kurs serwer ustala przy zapisie faktury.</summary>
public static class ExchangeRateEndpoints
{
    public static void MapExchangeRateEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/exchange-rates/{currency}", async (string currency, DateOnly? date, ExchangeRateLookup rates, TimeProvider time,
                CancellationToken ct) =>
            {
                var code = currency.ToUpperInvariant();
                if (!Currencies.Supported.Contains(code) || !Currencies.IsForeign(code))
                    return ApiErrors.Validation(nameof(currency), "Nieobsługiwana waluta.");

                // Zakres dat: od pierwszej tabeli NBP w API do jutra - inne daty to zbędne zapytania do NBP (SEC-6)
                var today = PolishDate.Today(time);
                var day = date ?? today;
                if (day < ExchangeRateLookup.FirstTableDate || day > today.AddDays(1))
                    return ApiErrors.Validation(nameof(date),
                        $"Kurs NBP można sprawdzić dla dat od {ExchangeRateLookup.FirstTableDate:dd.MM.yyyy} do {today.AddDays(1):dd.MM.yyyy}.");
                try
                {
                    return Results.Ok(ExchangeRateLookup.ToDto(await rates.ForTaxPointAsync(code, day, ct)));
                }
                catch (NbpException ex)
                {
                    return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .WithTags("Kursy walut").RequireAuthorization()
            .WithSummary("Kurs średni NBP z ostatniego dnia roboczego przed podaną datą (dniem sprzedaży)");
    }
}
