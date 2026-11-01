using System.Security.Claims;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Models;
using Microsoft.EntityFrameworkCore;

namespace FakturowanieKSeF.Api.Infrastructure;

/// <summary>
/// Ustala firmę zalogowanego użytkownika. To jedyne źródło <c>CompanyId</c> używanego do filtrowania danych:
/// identyfikator firmy NIGDY nie pochodzi z żądania klienta, tylko z tożsamości zalogowanego użytkownika.
/// Dzięki temu użytkownik nie może odczytać ani zmienić danych innej firmy, podając cudzy identyfikator.
/// </summary>
public sealed class CurrentCompany(IHttpContextAccessor httpContextAccessor, AppDbContext db)
{
    private Company? _company;
    private bool _loaded;

    public string UserId =>
        httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Brak zalogowanego użytkownika.");

    /// <summary>Firma użytkownika lub null, jeśli jeszcze jej nie założył.</summary>
    public async Task<Company?> FindAsync(CancellationToken ct = default)
    {
        if (!_loaded)
        {
            _company = await db.Companies.SingleOrDefaultAsync(c => c.OwnerId == UserId, ct);
            _loaded = true;
        }
        return _company;
    }

    internal void Set(Company company)
    {
        _company = company;
        _loaded = true;
    }
}
