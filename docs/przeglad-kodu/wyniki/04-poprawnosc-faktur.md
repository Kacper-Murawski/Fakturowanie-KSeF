# Raport: Obszar 04 - poprawność merytoryczna faktur
Status: W TOKU
Ostatnia aktualizacja: 2026-11-11 (start)

## Postęp
- [x] Kontekst: docs/fa3-xml.md, decyzje D16, D17, D21, D30-D33, D42, D43
- [x] Contracts: InvoiceCalculator.cs, Currencies.cs, VatRate.cs, Nip.cs, InvoiceDtos.cs
- [x] Api/Endpoints/InvoiceEndpoints.cs (ApplyAsync, korekty, proformy)
- [x] Api/Endpoints/PaymentEndpoints.cs
- [x] Api/Services: InvoiceNumbering.cs, InvoiceTemplates.cs, InvoiceFiles.cs, AccountingExport.cs
- [ ] Api/Services/InvoicePdfRenderer.cs
- [x] Api/Integrations/Nbp/NbpExchangeRates.cs (+ ExchangeRateLookup)
- [x] Ksef/Fa3: Fa3XmlGenerator.cs, Fa3InvoiceReader.cs, KsefQr.cs
- [ ] Ksef/Fa3/Fa3SchemaValidator.cs
- [x] Api/Endpoints/DashboardEndpoints.cs
- [ ] Testy obszaru (pokrycie)
Następny krok: dokończyć czytanie Fa3SchemaValidator, PaymentEndpoints, PDF, eksport, NBP, pulpit; potem potwierdzić testem FAK-1 (kolejność korekt) i FAK-2 (precyzja ilości).

## Znaleziska
(wersja robocza - pełny opis w trakcie; kandydaci zapisani, żeby nie przepadli przy przerwaniu)

- KANDYDAT FAK-A (wysokie/krytyczne, do potwierdzenia testem): `InvoiceEndpoints.cs:580-585` `CurrentStateAsync` wybiera „ostatnią przyjętą korektę” po `Id` malejąco, a nie po chwili przyjęcia. Szkic korekty K1 (Id mniejsze) zapisany ponownie i przyjęty PO korekcie K2 (Id większe) → stan „przed” kolejnej korekty brany z K2 zamiast z K1 → błędne różnice P_13/P_14/P_15.
- KANDYDAT FAK-B (średnie): brak ograniczenia liczby miejsc po przecinku w `SaveInvoiceItemRequest.Quantity` (>6) i `UnitNetPrice` (>8); sumy liczone z wartości niezaokrąglonych (`ApplyAsync`), a baza obcina do numeric(22,6)/(22,8); XML (`Fa3XmlGenerator.WriteFa`) liczy ponownie z wartości z bazy → `Invoice.TotalGross` ≠ P_15 (np. ilość 1,0000004 × 12500 → 12500,01 w bazie, 12500,00 w XML).
- KANDYDAT FAK-C (średnie): `POST /invoices/{id}/convert` - brak indeksu unikalnego na `ProformaId`; dwa równoległe żądania tworzą dwie faktury VAT z jednej proformy (`InvoiceEndpoints.cs:256-270`).
- KANDYDAT FAK-D (średnie): dwie różne korekty tej samej faktury wysłane równolegle - `CheckCorrectionStateAsync` (`:593`) nie blokuje (token współbieżności jest per korekta) → obie Pending z tym samym stanem „przed”.
- KANDYDAT FAK-E (średnie): stawki 0% WDT / eksport / np dostępne, ale nabywca tylko NIP albo BrakID (`Fa3XmlGenerator.WriteBuyer:123-126`) - nabywca zagraniczny trafia do KSeF jako BrakID=1 (konsument).
- KANDYDAT FAK-F (niskie): `next-number` i domyślna `IssueDate` używają `DateTime.Today` serwera (UTC), nie czasu polskiego (`InvoiceEndpoints.cs:304`, `InvoiceDtos.cs:153`).
- KANDYDAT FAK-G (niskie): `Fa3InvoiceReader` pomija P_9B/P_11A (faktury „od brutto”), wiersze zamówienia faktur zaliczkowych, rachunek faktora.

- KANDYDAT FAK-H (wysokie): kurs NBP dla daty sprzedaży PÓŹNIEJSZEJ niż data wystawienia / dzisiejsza (`InvoiceEndpoints.cs:541`, `NbpExchangeRates.cs:76-80`): art. 31a ust. 2 - faktura wystawiona przed powstaniem obowiązku → kurs sprzed dnia WYSTAWIENIA; kod pyta o przedział przed dniem sprzedaży (częściowo w przyszłości) → kurs zależny od chwili zapisu (ostatni opublikowany), buforowany 12 h, nieodświeżany przy wysyłce; przy dacie sprzedaży > 14 dni w przyszłości - błąd „Brak kursu” i faktury nie da się zapisać.
- KANDYDAT FAK-I (średnie): równoległa rejestracja dwóch wpłat (`PaymentEndpoints.cs:38-41`) - brak tokenu współbieżności → nadpłata mimo reguły D30.
- KANDYDAT FAK-J (niskie): `NbpClient` - odpowiedź nie-JSON (HTML) → `JsonException` nieobsłużony w `ApplyAsync` (łapie tylko `NbpException`) → 500.
- KANDYDAT FAK-K (niskie): eksport - kolumny per stawka pomijają grupy „ryczałt taxi”, „OSS”, „marża” (faktury zakupu), więc kolumny nie sumują się do „Razem netto”.
- DO SPRAWDZENIA: faktury importowane z KSeF (D44) w eksporcie sprzedaży / XML / PDF / korekty; PaymentEndpoints bez `RequireRoleForChanges` (poza obszarem?).
