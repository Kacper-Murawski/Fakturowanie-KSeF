using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace FakturowanieKSeF.Models;

/// <summary>
/// Kopia danych strony faktury (sprzedawcy lub nabywcy) z chwili wystawienia faktury.
/// Faktura jest dokumentem niezmiennym: zmiana danych firmy lub kontrahenta po wystawieniu
/// nie może zmieniać treści wcześniej wystawionych faktur.
/// Mapowanie na FA(3): Podmiot1 (sprzedawca) / Podmiot2 (nabywca).
/// </summary>
[Owned]
public class InvoiceParty
{
    [Required, MaxLength(250)]
    public string Name { get; set; } = string.Empty;

    /// <summary>NIP - wymagany dla sprzedawcy; dla nabywcy pusty w przypadku osoby prywatnej.</summary>
    [MaxLength(10)]
    public string? Nip { get; set; }

    public Address Address { get; set; } = new();
}
