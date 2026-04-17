using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FakturowanieKSeF.Models;

public class InvoiceItem : BaseEntity
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    
    [Column(TypeName = "decimal(18,2)")]
    public decimal Quantity { get; set; }
    
    [Column(TypeName = "decimal(18,2)")]
    public decimal NetPrice { get; set; }
    
    public decimal VatRate { get; set; } // np. 0.23
    
    // Do jakiej faktury to należy
    public int InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
}