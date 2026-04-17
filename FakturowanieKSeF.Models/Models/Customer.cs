using System.ComponentModel.DataAnnotations;

namespace FakturowanieKSeF.Models;

public class Customer : BaseEntity
{
    [Required, MaxLength(250)]
    public string Name { get; set; } = string.Empty;
    
    // null dla B2C
    [MaxLength(15)]
    public string? Nip { get; set; } 
    
    // B2B czy B2C
    public bool IsCompany { get; set; } 
    
    public Address Address { get; set; } = new Address();
    
    [Required]
    public string OwnerId { get; set; } = string.Empty; 
}