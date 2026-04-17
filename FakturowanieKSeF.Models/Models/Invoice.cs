using System.ComponentModel.DataAnnotations;

namespace FakturowanieKSeF.Models;

public class Invoice : BaseEntity
{
    [Required, MaxLength(50)]
    public string Number { get; set; } = string.Empty; // np. FV 1/2026
    
    public DateTime IssueDate { get; set; }
    
    public DateTime SaleDate { get; set; }
    
    public string Currency { get; set; } = "PLN";
    
    public DateTime DueDate { get; set; } // Termin płatności (np. 14 dni od wystawienia)
    
    [MaxLength(30)]
    public string BankAccountNumber { get; set; } = string.Empty;
    
    [MaxLength(100)]
    public string? KsefReferenceNumber { get; set; } // Znak zapytania, bo na początku go nie ma

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    
    // Klucze obce i relacje
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    
    public int ContractorId { get; set; }
    public Customer Contractor { get; set; } = null!;
    
    // Lista pozycji na fakturze
    public List<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();
}