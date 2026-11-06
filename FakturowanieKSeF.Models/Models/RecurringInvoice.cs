using System.ComponentModel.DataAnnotations;

namespace FakturowanieKSeF.Models;

/// <summary>
/// Harmonogram faktury cyklicznej (D39): co <see cref="IntervalMonths"/> miesięcy usługa w tle tworzy SZKIC faktury
/// na wzór faktury <see cref="TemplateInvoiceId"/> (nabywca, pozycje, warunki płatności). Szkic nie jest wysyłany
/// do KSeF automatycznie - wysyła go użytkownik po sprawdzeniu.
/// </summary>
public class RecurringInvoice : BaseEntity
{
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>Nazwa robocza, np. „Abonament serwisowy - Kontrahent S.A.”.</summary>
    [MaxLength(200)]
    public string? Name { get; set; }

    /// <summary>Faktura-wzór (faktura VAT tej samej firmy).</summary>
    public int TemplateInvoiceId { get; set; }
    public Invoice TemplateInvoice { get; set; } = null!;

    /// <summary>Co ile miesięcy: 1, 3, 6 albo 12.</summary>
    public int IntervalMonths { get; set; } = 1;

    /// <summary>Dzień miesiąca wystawienia (1-31); w krótszych miesiącach - ostatni dzień miesiąca.</summary>
    public int DayOfMonth { get; set; }

    /// <summary>Data najbliższego wystawienia.</summary>
    public DateOnly NextIssueDate { get; set; }

    /// <summary>Ostatni dzień, w którym może powstać faktura (puste - bez końca).</summary>
    public DateOnly? EndDate { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Powiadomienie e-mail użytkownika o przygotowanym szkicu.</summary>
    public bool NotifyByEmail { get; set; } = true;

    public int IssuedCount { get; set; }

    public DateOnly? LastIssuedOn { get; set; }

    public int? LastInvoiceId { get; set; }

    /// <summary>Błąd ostatniego przebiegu (np. usunięty wzór) - harmonogram jest wtedy wstrzymywany.</summary>
    [MaxLength(500)]
    public string? LastError { get; set; }
}
