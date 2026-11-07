using System.ComponentModel.DataAnnotations;

namespace FakturowanieKSeF.Contracts.Dtos;

/// <summary>Firma dostępna dla użytkownika (własna lub z członkostwa) - D46.</summary>
public sealed record AccessibleCompanyDto(int Id, string Name, string Nip, CompanyRole Role, bool Active);

/// <summary>Użytkownik firmy (właściciel ma <c>MemberId = null</c> - nie można go usunąć ani zmienić jego roli).</summary>
public sealed record CompanyMemberDto(int? MemberId, string Email, CompanyRole Role, DateTime? Since);

/// <summary>Zaproszenie oczekujące na przyjęcie.</summary>
public sealed record CompanyInvitationDto(int Id, string Email, CompanyRole Role, DateTime CreatedAt, DateTime ExpiresAt);

public sealed record CompanyMembersDto(IReadOnlyList<CompanyMemberDto> Members, IReadOnlyList<CompanyInvitationDto> Invitations, bool EmailConfigured);

/// <summary>Utworzone zaproszenie z linkiem (pokazywanym właścicielowi raz - np. gdy poczta nie jest skonfigurowana).</summary>
public sealed record InvitationCreatedDto(CompanyInvitationDto Invitation, string Link, bool EmailSent);

/// <summary>Podgląd zaproszenia przed przyjęciem.</summary>
public sealed record InvitationPreviewDto(string CompanyName, string CompanyNip, CompanyRole Role, string Email, DateTime ExpiresAt);

public sealed class InviteMemberRequest
{
    [Required(ErrorMessage = "Podaj adres e-mail.")]
    [EmailAddress(ErrorMessage = "Nieprawidłowy adres e-mail.")]
    [MaxLength(254)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Pracownik albo księgowa (właściciel jest jeden - założyciel firmy).</summary>
    public CompanyRole Role { get; set; } = CompanyRole.Accountant;
}

public sealed class ChangeMemberRoleRequest
{
    public CompanyRole Role { get; set; }
}

public sealed class AcceptInvitationRequest
{
    [Required]
    [MaxLength(100)]
    public string Token { get; set; } = string.Empty;
}

public sealed class SelectCompanyRequest
{
    public int CompanyId { get; set; }
}
