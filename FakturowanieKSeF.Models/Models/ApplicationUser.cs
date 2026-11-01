using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace FakturowanieKSeF.Models;


public class ApplicationUser : IdentityUser
{
    // Opcjonalne: wbudowany endpoint /register (MapIdentityApi) przyjmuje tylko email i hasło,
    // więc imię i nazwisko uzupełniane są później w profilu użytkownika.
    [MaxLength(100)]
    public string? FirstName { get; set; }
    [MaxLength(100)]
    public string? LastName { get; set; }
    public DateTime RegistrationDate { get; set; } = DateTime.UtcNow;
}
