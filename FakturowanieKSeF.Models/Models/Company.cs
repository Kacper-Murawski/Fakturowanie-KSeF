using System.ComponentModel.DataAnnotations;

namespace FakturowanieKSeF.Models;

public class Company : BaseEntity
{
    [Required, MaxLength(250)]
    public string Name { get; set; } = string.Empty;
    
    [Required, MaxLength(15)]
    public string Nip { get; set; } = string.Empty;
    
    public Address Address { get; set; } = new Address();
    
    [Required]
    public string OwnerId { get; set; } = string.Empty;
}