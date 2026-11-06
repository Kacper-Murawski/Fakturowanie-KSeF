using System.Security.Cryptography;
using System.Text;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Api.Services.Email;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FakturowanieKSeF.Api.Endpoints;

/// <summary>
/// Użytkownicy firmy i zaproszenia (D46). Zarządza nimi wyłącznie właściciel. Zaproszenie przyjmuje osoba,
/// która ma link z jednorazowym tokenem (wysłany na zapraszany adres) i jest zalogowana na konto z tym adresem.
/// </summary>
public static class MemberEndpoints
{
    public static readonly TimeSpan InvitationValidity = TimeSpan.FromDays(7);

    public static void MapMemberEndpoints(this RouteGroupBuilder api)
    {
        var members = api.MapGroup("/company/members").WithTags("Użytkownicy firmy").RequireAuthorization()
            .RequireRole(CompanyRole.Owner);

        members.MapGet("/", async (CurrentCompany current, AppDbContext db, EmailOutbox outbox, TimeProvider time, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();
                return Results.Ok(await ListAsync(company, db, outbox, time, ct));
            })
            .WithSummary("Użytkownicy firmy i oczekujące zaproszenia");

        members.MapPost("/invitations", async (InviteMemberRequest request, CurrentCompany current, AppDbContext db,
                UserManager<ApplicationUser> users, EmailOutbox outbox, EmailWorkSignal signal, IConfiguration configuration,
                HttpContext http, TimeProvider time, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();
                if (request.Role == CompanyRole.Owner)
                    return ApiErrors.Validation(nameof(request.Role), "Właściciel firmy jest jeden - zaproś pracownika lub księgową.");

                var email = request.Email.Trim();
                var normalized = users.NormalizeEmail(email);
                var owner = await db.Users.Where(u => u.Id == company.OwnerId).Select(u => u.NormalizedEmail).SingleAsync(ct);
                if (owner == normalized) return ApiErrors.Validation(nameof(request.Email), "To adres właściciela firmy.");
                if (await db.CompanyMembers.AnyAsync(m => m.CompanyId == company.Id && m.User.NormalizedEmail == normalized, ct))
                    return ApiErrors.Conflict("Ta osoba ma już dostęp do firmy.");

                // Nowe zaproszenie unieważnia poprzednie oczekujące na ten adres
                var now = time.GetUtcNow().UtcDateTime;
                await db.CompanyInvitations
                    .Where(i => i.CompanyId == company.Id && i.Email.ToUpper() == normalized && i.AcceptedAt == null && i.RevokedAt == null)
                    .ExecuteUpdateAsync(u => u.SetProperty(i => i.RevokedAt, now), ct);

                var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                var invitation = new CompanyInvitation
                {
                    CompanyId = company.Id, Email = email, Role = request.Role, TokenHash = Hash(token),
                    CreatedAt = now, ExpiresAt = now + InvitationValidity
                };
                db.CompanyInvitations.Add(invitation);

                // Adres aplikacji z konfiguracji (produkcyjnie), w developmencie - z żądania
                var appUrl = (configuration["App:PublicUrl"] ?? $"{http.Request.Scheme}://{http.Request.Host}").TrimEnd('/');
                var link = $"{appUrl}/zaproszenie?token={token}";

                var emailSent = false;
                if (outbox.IsConfigured)
                {
                    try
                    {
                        var body = new StringBuilder()
                            .AppendLine("Dzień dobry,").AppendLine()
                            .AppendLine($"{company.Name} (NIP {company.Nip}) zaprasza Cię do aplikacji Fakturowanie KSeF")
                            .AppendLine($"z rolą: {CompanyRoles.Name(request.Role)}.").AppendLine()
                            .AppendLine("Aby przyjąć zaproszenie, zaloguj się (lub załóż konto) na ten adres e-mail i otwórz link:")
                            .AppendLine(link).AppendLine()
                            .AppendLine($"Link jest ważny do {now.Add(InvitationValidity):dd.MM.yyyy}. Jeśli nie spodziewasz się zaproszenia, zignoruj tę wiadomość.")
                            .AppendLine().AppendLine("--").AppendLine(EmailTemplates.TestEnvironmentNotice).ToString();
                        await outbox.EnqueueAsync(company.Id, EmailKind.Invitation, email, $"Zaproszenie do firmy {company.Name}", body, ct: ct);
                        emailSent = true;
                    }
                    catch (EmailLimitException ex)
                    {
                        return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status429TooManyRequests);
                    }
                }

                await db.SaveChangesAsync(ct);
                if (emailSent) signal.Pulse();
                return Results.Ok(new InvitationCreatedDto(ToDto(invitation), link, emailSent));
            })
            .WithSummary("Zaproszenie pracownika lub księgowej (e-mail z jednorazowym linkiem, ważnym 7 dni)");

        members.MapDelete("/invitations/{id:int}", async (int id, CurrentCompany current, AppDbContext db, TimeProvider time, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();
                var invitation = await db.CompanyInvitations.SingleOrDefaultAsync(i => i.Id == id && i.CompanyId == company.Id && i.AcceptedAt == null, ct);
                if (invitation is null) return ApiErrors.NotFound("zaproszenie");
                invitation.RevokedAt = time.GetUtcNow().UtcDateTime;
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .WithSummary("Odwołanie zaproszenia");

        members.MapPut("/{memberId:int}", async (int memberId, ChangeMemberRoleRequest request, CurrentCompany current, AppDbContext db,
                CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();
                if (request.Role == CompanyRole.Owner) return ApiErrors.Validation(nameof(request.Role), "Właściciel firmy jest jeden.");
                var member = await db.CompanyMembers.SingleOrDefaultAsync(m => m.Id == memberId && m.CompanyId == company.Id, ct);
                if (member is null) return ApiErrors.NotFound("użytkownik firmy");
                member.Role = request.Role;
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .WithSummary("Zmiana roli użytkownika firmy");

        members.MapDelete("/{memberId:int}", async (int memberId, CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();
                var member = await db.CompanyMembers.SingleOrDefaultAsync(m => m.Id == memberId && m.CompanyId == company.Id, ct);
                if (member is null) return ApiErrors.NotFound("użytkownik firmy");
                db.CompanyMembers.Remove(member); // dostęp kończy się od następnego żądania tej osoby
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .WithSummary("Odebranie dostępu do firmy");

        // ---------------- konto: firmy użytkownika i przyjmowanie zaproszeń ----------------
        var account = api.MapGroup("/account").WithTags("Konto").RequireAuthorization();

        account.MapGet("/companies", async (CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                var active = await current.FindAsync(ct);
                var userId = current.UserId;
                var owned = await db.Companies.Where(c => c.OwnerId == userId)
                    .Select(c => new { c.Id, c.Name, c.Nip, Role = CompanyRole.Owner }).ToListAsync(ct);
                var memberships = await db.CompanyMembers.Where(m => m.UserId == userId)
                    .Select(m => new { m.Company.Id, m.Company.Name, m.Company.Nip, m.Role }).ToListAsync(ct);
                return Results.Ok(owned.Concat(memberships)
                    .Select(c => new AccessibleCompanyDto(c.Id, c.Name, c.Nip, c.Role, c.Id == active?.Id)).ToList());
            })
            .WithSummary("Firmy dostępne dla użytkownika (własna i z zaproszeń) z rolą");

        account.MapPost("/active-company", async (SelectCompanyRequest request, CurrentCompany current, HttpContext http,
                IWebHostEnvironment env, CancellationToken ct) =>
            {
                if (await current.AccessAsync(request.CompanyId, current.UserId, ct) is null) return ApiErrors.NotFound("firma");
                http.Response.Cookies.Append(CurrentCompany.CookieName, request.CompanyId.ToString(), new CookieOptions
                {
                    HttpOnly = true, SameSite = SameSiteMode.Strict, IsEssential = true, MaxAge = TimeSpan.FromDays(30),
                    Secure = !env.IsDevelopment()
                });
                return Results.NoContent();
            })
            .WithSummary("Wybór firmy, w której pracuje użytkownik (tylko spośród dostępnych)");

        account.MapGet("/invitations/{token}", async (string token, CurrentCompany current, AppDbContext db, TimeProvider time, CancellationToken ct) =>
            {
                var invitation = await FindValidAsync(db, token, time, ct);
                return invitation is null
                    ? ApiErrors.NotFound("zaproszenie (wygasło, zostało odwołane lub już przyjęte)")
                    : Results.Ok(new InvitationPreviewDto(invitation.Company.Name, invitation.Company.Nip, invitation.Role, invitation.Email, invitation.ExpiresAt));
            })
            .WithSummary("Podgląd zaproszenia");

        account.MapPost("/invitations/accept", async (AcceptInvitationRequest request, CurrentCompany current, AppDbContext db,
                UserManager<ApplicationUser> users, HttpContext http, IWebHostEnvironment env, TimeProvider time, CancellationToken ct) =>
            {
                var invitation = await FindValidAsync(db, request.Token, time, ct);
                if (invitation is null) return ApiErrors.NotFound("zaproszenie (wygasło, zostało odwołane lub już przyjęte)");

                var user = await users.FindByIdAsync(current.UserId);
                if (user is null || users.NormalizeEmail(invitation.Email) != user.NormalizedEmail)
                    return Results.Problem(title: $"Zaproszenie jest dla adresu {invitation.Email}. Zaloguj się na konto z tym adresem.",
                        statusCode: StatusCodes.Status403Forbidden);
                if (invitation.Company.OwnerId == user.Id || await db.CompanyMembers.AnyAsync(m => m.CompanyId == invitation.CompanyId && m.UserId == user.Id, ct))
                    return ApiErrors.Conflict("Masz już dostęp do tej firmy.");

                var now = time.GetUtcNow().UtcDateTime;
                invitation.AcceptedAt = now;
                db.CompanyMembers.Add(new CompanyMember { CompanyId = invitation.CompanyId, UserId = user.Id, Role = invitation.Role, CreatedAt = now });
                await db.SaveChangesAsync(ct);

                // Od razu praca w nowej firmie
                http.Response.Cookies.Append(CurrentCompany.CookieName, invitation.CompanyId.ToString(), new CookieOptions
                {
                    HttpOnly = true, SameSite = SameSiteMode.Strict, IsEssential = true, MaxAge = TimeSpan.FromDays(30), Secure = !env.IsDevelopment()
                });
                return Results.Ok(new AccessibleCompanyDto(invitation.CompanyId, invitation.Company.Name, invitation.Company.Nip, invitation.Role, true));
            })
            .WithSummary("Przyjęcie zaproszenia (token z wiadomości + konto z zapraszanym adresem e-mail)");
    }

    private static async Task<CompanyMembersDto> ListAsync(Company company, AppDbContext db, EmailOutbox outbox, TimeProvider time, CancellationToken ct)
    {
        var owner = await db.Users.Where(u => u.Id == company.OwnerId).Select(u => u.Email).SingleAsync(ct);
        var members = await db.CompanyMembers.Where(m => m.CompanyId == company.Id).OrderBy(m => m.CreatedAt)
            .Select(m => new CompanyMemberDto(m.Id, m.User.Email!, m.Role, m.CreatedAt)).ToListAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        var invitations = await db.CompanyInvitations
            .Where(i => i.CompanyId == company.Id && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
            .OrderBy(i => i.CreatedAt).ToListAsync(ct);
        return new CompanyMembersDto(
            [new CompanyMemberDto(null, owner ?? "", CompanyRole.Owner, null), .. members],
            invitations.Select(ToDto).ToList(), outbox.IsConfigured);
    }

    private static async Task<CompanyInvitation?> FindValidAsync(AppDbContext db, string token, TimeProvider time, CancellationToken ct)
    {
        var hash = Hash(token);
        var now = time.GetUtcNow().UtcDateTime;
        return await db.CompanyInvitations.Include(i => i.Company)
            .SingleOrDefaultAsync(i => i.TokenHash == hash && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now, ct);
    }

    /// <summary>Skrót tokenu (hex) - w bazie nie ma tokenu, więc wyciek bazy nie pozwala przyjąć zaproszenia.</summary>
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim())));

    private static CompanyInvitationDto ToDto(CompanyInvitation i) => new(i.Id, i.Email, i.Role, i.CreatedAt, i.ExpiresAt);
}
