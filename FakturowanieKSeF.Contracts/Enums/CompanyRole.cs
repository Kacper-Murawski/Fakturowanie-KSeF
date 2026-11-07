using System.Text.Json.Serialization;

namespace FakturowanieKSeF.Contracts;

/// <summary>Rola użytkownika w firmie (D46).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CompanyRole>))]
public enum CompanyRole
{
    /// <summary>Właściciel (założyciel) - pełny dostęp, w tym dane firmy, token KSeF i użytkownicy.</summary>
    Owner,

    /// <summary>Pracownik - wystawianie i wysyłka faktur, kontrahenci, katalog, e-maile, płatności.</summary>
    Employee,

    /// <summary>Księgowa - odczyt wszystkich danych, eksport i rejestrowanie płatności; bez wystawiania faktur.</summary>
    Accountant
}
