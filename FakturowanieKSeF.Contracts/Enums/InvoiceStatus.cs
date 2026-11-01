using System.Text.Json.Serialization;

namespace FakturowanieKSeF.Contracts;

/// <summary>
/// Cykl życia faktury sprzedażowej:
/// <code>
/// Draft ──wyślij──▶ Pending ──KSeF: 200──▶ Accepted (numer KSeF, UPO)
///   ▲                  │
///   └──── popraw ◀── Rejected (KSeF odrzucił, np. błąd walidacji)
/// </code>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<InvoiceStatus>))]
public enum InvoiceStatus
{
    /// <summary>Szkic - można edytować i usunąć.</summary>
    Draft,

    /// <summary>Wysłana do KSeF, oczekuje na przetworzenie (KSeF działa asynchronicznie). Edycja zablokowana.</summary>
    Pending,

    /// <summary>Przyjęta przez KSeF - ma numer KSeF. Dokument ostateczny, bez możliwości edycji i usunięcia.</summary>
    Accepted,

    /// <summary>Odrzucona przez KSeF - przyczyna w ostatniej próbie wysyłki; po poprawie można wysłać ponownie.</summary>
    Rejected
}
