namespace FakturowanieKSeF.Contracts.Dtos;

/// <summary>
/// Podmiot z wykazu podatników VAT (Biała lista MF) - do automatycznego uzupełnienia danych kontrahenta lub firmy.
/// </summary>
/// <param name="VatStatus">Status VAT z wykazu: „Czynny”, „Zwolniony” albo „Niezarejestrowany”.</param>
/// <param name="Address">Adres rozbity na pola; <c>null</c>, gdy adresu z wykazu nie dało się rozpoznać.</param>
/// <param name="RegistryAddress">Adres w postaci zwróconej przez wykaz (do porównania przez użytkownika).</param>
/// <param name="BankAccounts">Rachunki zgłoszone do urzędu skarbowego (26 cyfr, bez spacji).</param>
/// <param name="CheckedOn">Dzień, na który sprawdzono dane w wykazie.</param>
/// <param name="RequestId">Identyfikator zapytania nadany przez MF - potwierdzenie sprawdzenia w wykazie.</param>
public sealed record VatRegistryEntryDto(
    string Name,
    string Nip,
    string VatStatus,
    bool IsActiveVatPayer,
    string? Regon,
    string? Krs,
    AddressDto? Address,
    string? RegistryAddress,
    IReadOnlyList<string> BankAccounts,
    DateOnly CheckedOn,
    string? RequestId);
