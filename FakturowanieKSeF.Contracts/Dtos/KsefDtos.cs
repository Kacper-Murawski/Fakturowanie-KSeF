using System.ComponentModel.DataAnnotations;

namespace FakturowanieKSeF.Contracts.Dtos;

/// <summary>Stan integracji firmy z KSeF. Sam token nigdy nie jest zwracany do przeglądarki.</summary>
public sealed record KsefSettingsDto(bool HasToken, DateTime? TokenUpdatedAt, string Environment);

public sealed class SaveKsefTokenRequest
{
    [Required(ErrorMessage = "Wklej token KSeF.")]
    [MaxLength(1000)]
    public string Token { get; set; } = string.Empty;
}

/// <summary>Ostatnia próba wysyłki faktury do KSeF.</summary>
public sealed record KsefSubmissionDto(
    DateTime SentAt,
    int? StatusCode,
    string? StatusDescription,
    IReadOnlyList<string> StatusDetails,
    string? KsefNumber,
    DateTime? KsefAssignedAt,
    bool HasUpo,
    DateTime? CompletedAt,
    int FailedAttempts,
    string? LastError);
