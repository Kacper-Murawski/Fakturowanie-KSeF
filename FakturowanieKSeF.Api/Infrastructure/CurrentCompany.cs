using System.Security.Claims;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Models;
using Microsoft.EntityFrameworkCore;

namespace FakturowanieKSeF.Api.Infrastructure;

/// <summary>
/// Ustala firmę, w której działa zalogowany użytkownik, i jego rolę (D46). To jedyne źródło <c>CompanyId</c>
/// używanego do filtrowania danych. Użytkownik może wybrać firmę (ciasteczko <see cref="CookieName"/> albo nagłówek
/// <see cref="HeaderName"/>), ale wybór jest tylko wskazaniem spośród firm, do których MA dostęp - własnej
/// (<see cref="Company.OwnerId"/>) lub z członkostwa (<see cref="CompanyMember"/>). Wskazanie cudzej firmy jest ignorowane,
/// więc podanie obcego identyfikatora nie daje dostępu do żadnych danych.
/// </summary>
public sealed class CurrentCompany(IHttpContextAccessor httpContextAccessor, AppDbContext db)
{
    public const string CookieName = "ksef.company";
    public const string HeaderName = "X-Company-Id";

    private Company? _company;
    private CompanyRole _role;
    private bool _loaded;

    public string UserId =>
        httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Brak zalogowanego użytkownika.");

    /// <summary>Rola w bieżącej firmie (ważna po <see cref="FindAsync"/>, gdy firma istnieje).</summary>
    public CompanyRole Role => _role;

    /// <summary>Bieżąca firma użytkownika lub null, jeśli nie założył firmy i nie należy do żadnej.</summary>
    public async Task<Company?> FindAsync(CancellationToken ct = default)
    {
        if (_loaded) return _company;
        _loaded = true;

        var userId = UserId;
        if (SelectedCompanyId() is { } selected && await AccessAsync(selected, userId, ct) is { } chosen)
        {
            (_company, _role) = chosen;
            return _company;
        }

        // Domyślnie: własna firma, a jeśli jej nie ma - pierwsza firma z członkostwa
        var owned = await db.Companies.SingleOrDefaultAsync(c => c.OwnerId == userId, ct);
        if (owned is not null)
        {
            (_company, _role) = (owned, CompanyRole.Owner);
            return _company;
        }

        var membership = await db.CompanyMembers.Include(m => m.Company)
            .Where(m => m.UserId == userId).OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).FirstOrDefaultAsync(ct);
        if (membership is not null) (_company, _role) = (membership.Company, membership.Role);
        return _company;
    }

    /// <summary>Firma i rola, jeśli użytkownik ma do niej dostęp; inaczej null.</summary>
    public async Task<(Company Company, CompanyRole Role)?> AccessAsync(int companyId, string userId, CancellationToken ct = default)
    {
        var company = await db.Companies.SingleOrDefaultAsync(c => c.Id == companyId, ct);
        if (company is null) return null;
        if (company.OwnerId == userId) return (company, CompanyRole.Owner);
        var member = await db.CompanyMembers.Where(m => m.CompanyId == companyId && m.UserId == userId).Select(m => (CompanyRole?)m.Role)
            .SingleOrDefaultAsync(ct);
        return member is { } role ? (company, role) : null;
    }

    private int? SelectedCompanyId()
    {
        var request = httpContextAccessor.HttpContext?.Request;
        var raw = request?.Headers[HeaderName].FirstOrDefault() ?? request?.Cookies[CookieName];
        return int.TryParse(raw, out var id) ? id : null;
    }

    internal void Set(Company company)
    {
        _company = company;
        _role = CompanyRole.Owner;
        _loaded = true;
    }
}

/// <summary>Uprawnienia ról (D46) - filtr endpointów sprawdzający rolę w bieżącej firmie.</summary>
public static class CompanyRoles
{
    public static readonly CompanyRole[] Issuers = [CompanyRole.Owner, CompanyRole.Employee];
    public static readonly CompanyRole[] All = [CompanyRole.Owner, CompanyRole.Employee, CompanyRole.Accountant];

    public static string Name(CompanyRole role) => role switch
    {
        CompanyRole.Owner => "właściciel",
        CompanyRole.Employee => "pracownik",
        _ => "księgowa"
    };

    /// <summary>Operacje zmieniające dane (POST, PUT, DELETE) dozwolone tylko dla podanych ról; odczyt - dla wszystkich.</summary>
    public static TBuilder RequireRoleForChanges<TBuilder>(this TBuilder builder, params CompanyRole[] roles)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var method = context.HttpContext.Request.Method;
            if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method)) return await next(context);
            return await CheckAsync(context, next, roles);
        });

    /// <summary>Endpoint dostępny tylko dla podanych ról (także odczyt).</summary>
    public static TBuilder RequireRole<TBuilder>(this TBuilder builder, params CompanyRole[] roles)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter((context, next) => CheckAsync(context, next, roles));

    private static async ValueTask<object?> CheckAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next, CompanyRole[] roles)
    {
        var current = context.HttpContext.RequestServices.GetRequiredService<CurrentCompany>();
        // Brak firmy - decyduje endpoint (np. utworzenie własnej firmy)
        if (await current.FindAsync(context.HttpContext.RequestAborted) is null || roles.Contains(current.Role))
            return await next(context);

        return Results.Problem(
            title: $"Twoja rola w firmie ({Name(current.Role)}) nie pozwala na tę operację.",
            statusCode: StatusCodes.Status403Forbidden);
    }
}
