namespace FakturowanieKSeF.Models;

public enum InvoiceStatus
{
    Draft,      // Robocza
    Sent,       // Wysłana do KSeF
    Accepted,   // Zaakceptowana
    Rejected    // Odrzucona przez KSeF
}