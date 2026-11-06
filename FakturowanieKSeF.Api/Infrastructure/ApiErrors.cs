using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FakturowanieKSeF.Api.Infrastructure;

/// <summary>Spójne odpowiedzi błędów API w formacie ProblemDetails (RFC 9457), z komunikatami po polsku.</summary>
public static class ApiErrors
{
    public static IResult NotFound(string what) =>
        Results.Problem(title: $"Nie znaleziono: {what}.", statusCode: StatusCodes.Status404NotFound);

    public static IResult Conflict(string message) =>
        Results.Problem(title: message, statusCode: StatusCodes.Status409Conflict);

    public static IResult CompanyRequired() =>
        Results.Problem(
            title: "Najpierw uzupełnij dane swojej firmy.",
            detail: "Kontrahenci i faktury są przypisani do firmy. Utwórz ją przez PUT /api/company.",
            statusCode: StatusCodes.Status409Conflict);

    public static IResult Validation(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] },
            title: "Dane zawierają błędy.");

    /// <summary>Komunikaty dla naruszeń unikalnych indeksów bazy.</summary>
    private static readonly Dictionary<string, string> UniqueViolationMessages = new()
    {
        ["IX_Invoices_CompanyId_Number"] = "Faktura o tym numerze już istnieje.",
        ["IX_Customers_CompanyId_Nip"] = "Kontrahent o tym numerze NIP już istnieje.",
        ["IX_Companies_OwnerId"] = "To konto ma już przypisaną firmę.",
        ["IX_Products_CompanyId_Name"] = "Pozycja o tej nazwie już jest w katalogu.",
    };

    /// <summary>
    /// Zamienia wyjątki „biznesowe” z warstwy danych na odpowiedzi 409 zamiast ogólnego błędu 500.
    /// </summary>
    public sealed class ExceptionHandler : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
        {
            IResult? result = exception switch
            {
                DbUpdateConcurrencyException =>
                    Conflict("Dane zostały w międzyczasie zmienione. Odśwież i spróbuj ponownie."),
                DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg } =>
                    Conflict(UniqueViolationMessages.GetValueOrDefault(pg.ConstraintName ?? "", "Taki rekord już istnieje.")),
                _ => null
            };

            if (result is null) return false;

            await result.ExecuteAsync(context);
            return true;
        }
    }
}
