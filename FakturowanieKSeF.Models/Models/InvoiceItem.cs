using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FakturowanieKSeF.Models;

/// <summary>
/// Pozycja faktury (FA(3): FaWiersz). Precyzja kolumn odpowiada typom schemy FA(3):
/// ilość - TIlosci (6 miejsc po przecinku), cena jednostkowa - TKwotowy2 (8), kwoty - TKwotowy (2).
/// Pozycje są częścią faktury - nie mają własnego miękkiego usuwania i są usuwane razem ze szkicem faktury.
/// </summary>
public class InvoiceItem
{
    public int Id { get; set; }

    /// <summary>Numer wiersza na fakturze (NrWierszaFa), od 1.</summary>
    public int Position { get; set; }

    /// <summary>Nazwa towaru lub usługi (P_7).</summary>
    [Required, MaxLength(512)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Jednostka miary (P_8A), np. "szt.", "godz.", "usł.".</summary>
    [MaxLength(50)]
    public string? Unit { get; set; }

    /// <summary>Ilość (P_8B).</summary>
    [Column(TypeName = "numeric(22,6)")]
    public decimal Quantity { get; set; }

    /// <summary>Cena jednostkowa netto (P_9A).</summary>
    [Column(TypeName = "numeric(22,8)")]
    public decimal UnitNetPrice { get; set; }

    /// <summary>Stawka VAT (P_12).</summary>
    public VatRate VatRate { get; set; } = VatRate.Rate23;

    /// <summary>Wartość netto pozycji (P_11).</summary>
    [Column(TypeName = "numeric(18,2)")]
    public decimal NetAmount { get; set; }

    [Column(TypeName = "numeric(18,2)")]
    public decimal VatAmount { get; set; }

    [Column(TypeName = "numeric(18,2)")]
    public decimal GrossAmount { get; set; }

    /// <summary>
    /// Wiersz stanu PRZED korektą (StanPrzed w FA(3)) - tylko na fakturze korygującej. Wiersze „przed” mają
    /// pozycje 1..n, a wiersze stanu po korekcie kolejne numery (osobna numeracja wierszy, jak wymaga FA(3)).
    /// </summary>
    public bool BeforeCorrection { get; set; }

    public int InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
}
