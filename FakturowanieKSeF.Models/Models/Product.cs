using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FakturowanieKSeF.Models;

/// <summary>
/// Towar lub usługa z katalogu firmy - podpowiedź przy wystawianiu faktury (D35). Dane są KOPIOWANE do pozycji
/// faktury (<see cref="InvoiceItem"/>), więc zmiana ceny w katalogu nie zmienia wystawionych faktur.
/// </summary>
public class Product : BaseEntity
{
    /// <summary>Nazwa (FA(3): P_7) - unikalna w katalogu firmy.</summary>
    [Required, MaxLength(512)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Jednostka miary (P_8A), np. „szt.”, „godz.”.</summary>
    [MaxLength(50)]
    public string? Unit { get; set; }

    /// <summary>Domyślna cena jednostkowa netto (P_9A) - na fakturze można ją zmienić.</summary>
    [Column(TypeName = "numeric(22,8)")]
    public decimal UnitNetPrice { get; set; }

    public VatRate VatRate { get; set; } = VatRate.Rate23;

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
}
