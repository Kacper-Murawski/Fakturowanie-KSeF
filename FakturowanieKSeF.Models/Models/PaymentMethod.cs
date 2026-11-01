namespace FakturowanieKSeF.Models;

/// <summary>Forma płatności - wartości liczbowe zgodne z typem TFormaPlatnosci schemy FA(3).</summary>
public enum PaymentMethod
{
    Cash = 1,         // Gotówka
    Card = 2,         // Karta
    Voucher = 3,      // Bon
    Cheque = 4,       // Czek
    Credit = 5,       // Kredyt
    Transfer = 6,     // Przelew
    Mobile = 7        // Płatność mobilna
}
