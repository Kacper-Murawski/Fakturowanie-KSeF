using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace FakturowanieKSeF.Models;

/// <summary>
/// Adres podmiotu (typ własny - przechowywany w kolumnach tabeli właściciela).
/// Mapowanie na FA(3): KodKraju = <see cref="CountryCode"/>, AdresL1 = <see cref="Street"/>,
/// AdresL2 = "<see cref="PostalCode"/> <see cref="City"/>".
/// </summary>
[Owned]
public class Address
{
    /// <summary>Ulica z numerem budynku i lokalu, np. "ul. Kwiatowa 1/2".</summary>
    [Required, MaxLength(200)]
    public string Street { get; set; } = string.Empty;

    [Required, MaxLength(10)]
    public string PostalCode { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string City { get; set; } = string.Empty;

    /// <summary>Kod kraju ISO 3166-1 alfa-2 (KSeF wymaga kodu, nie nazwy kraju).</summary>
    [Required, MaxLength(2)]
    public string CountryCode { get; set; } = "PL";
}
