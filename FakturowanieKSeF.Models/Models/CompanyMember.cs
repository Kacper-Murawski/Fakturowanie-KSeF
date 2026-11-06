using System.ComponentModel.DataAnnotations;

namespace FakturowanieKSeF.Models;

/// <summary>
/// Dostęp użytkownika do cudzej firmy (pracownik, księgowa) - D46. Właściciel firmy nie ma wpisu
/// (<see cref="Company.OwnerId"/>). Jedno konto może należeć do wielu firm (np. biuro rachunkowe).
/// </summary>
public class CompanyMember
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public CompanyRole Role { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Zaproszenie do firmy wysłane na adres e-mail (D46). Przyjęcie wymaga jednorazowego tokenu z wiadomości (dowód
/// dostępu do skrzynki) i konta z tym samym adresem - w bazie przechowywany jest tylko skrót SHA-256 tokenu.
/// </summary>
public class CompanyInvitation
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    [Required, MaxLength(254)]
    public string Email { get; set; } = string.Empty;

    public CompanyRole Role { get; set; }

    /// <summary>Skrót SHA-256 (hex) tokenu z linku - sam token nie jest przechowywany.</summary>
    [Required, MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? AcceptedAt { get; set; }

    public DateTime? RevokedAt { get; set; }
}
