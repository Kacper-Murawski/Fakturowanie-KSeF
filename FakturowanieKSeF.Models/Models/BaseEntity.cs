namespace FakturowanieKSeF.Models;

/// <summary>
/// Wspólne pola encji: identyfikator, znaczniki czasu (UTC, ustawiane automatycznie przy zapisie)
/// oraz miękkie usuwanie (rekord oznaczony jako usunięty jest ukrywany filtrem globalnym, ale pozostaje w bazie).
/// </summary>
public abstract class BaseEntity
{
    public int Id { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }
}
