namespace FakturowanieKSeF.Contracts.Dtos;

/// <summary>Żądanie wysyłki faktury do KSeF - wersja rekordu z ostatniego odczytu (ochrona przed podwójną wysyłką).</summary>
public sealed class SendInvoiceRequest
{
    public uint Version { get; set; }
}
