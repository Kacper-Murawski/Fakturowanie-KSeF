using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace FakturowanieKSeF.Models;

[Owned]
public class Address
{
    [Required, MaxLength(100)]
    public string Street { get; set; } = string.Empty;
    
    [Required, MaxLength(50)]
    public string City { get; set; } = string.Empty;
    
    [Required, MaxLength(20)]
    public string PostalCode { get; set; } = string.Empty;
    
    [Required, MaxLength(50)]
    public string Country { get; set; } = "Polska";
}