# Obszar 04 - poprawność merytoryczna faktur (kwoty, FA(3), korekty, waluty, płatności)

Raport zapisz w `docs/przeglad-kodu/wyniki/04-poprawnosc-faktur.md`. ID znalezisk: `FAK-1`, `FAK-2`, …
Najpierw przeczytaj `00-zasady.md`, potem `docs/fa3-xml.md` i decyzje D16, D17, D21, D30-D33, D42, D43.

## Zakres plików

- `FakturowanieKSeF.Contracts/Calculation/InvoiceCalculator.cs`, `Currencies.cs`, `Enums/VatRate.cs`,
  `Validation/Nip.cs`, `Dtos/InvoiceDtos.cs` (walidacja `IValidatableObject`)
- `FakturowanieKSeF.Api/Endpoints/InvoiceEndpoints.cs` (`ApplyAsync`, korekty, proformy), `PaymentEndpoints.cs`
- `FakturowanieKSeF.Api/Services/InvoiceNumbering.cs`, `InvoiceTemplates.cs`, `InvoicePdfRenderer.cs`,
  `InvoiceFiles.cs`, `AccountingExport.cs`
- `FakturowanieKSeF.Api/Integrations/Nbp/NbpExchangeRates.cs`
- `FakturowanieKSeF.Ksef/Fa3/Fa3XmlGenerator.cs`, `Fa3InvoiceReader.cs`, `Fa3SchemaValidator.cs`, `KsefQr.cs`
- `FakturowanieKSeF.Api/Endpoints/DashboardEndpoints.cs` (sumy na pulpicie)
- testy: `Fa3XmlTests.cs`, `CorrectionTests.cs`, `CurrencyTests.cs`, `VatRateTests.cs`, `PaymentTests.cs`,
  `ProformaTests.cs`, `ExportTests.cs`, `ContractsTests.cs`

## Pytania kontrolne

**Kwoty i podatek**
- Zaokrąglenia: czy metoda (od sumy według stawek vs od pozycji, D16) jest stosowana spójnie w kalkulatorze,
  XML (P_13_x, P_14_x, P_15), PDF, eksporcie i rozliczeniach? Szukaj różnic o 0,01 zł.
- `MidpointRounding` i typ `decimal` wszędzie (żadnego `double` w kwotach), ilości ułamkowe, ceny z 8 miejscami,
  bardzo duże kwoty, kwoty ujemne (korekty), zero.
- Stawki: `zw`, `np`, `0%` krajowe/WDT/eksport, stawki historyczne 22%/7% - czy trafiają do właściwych pól FA(3)?
- Podatek w PLN dla faktur walutowych (`P_14_xW`): kurs, zaokrąglenie, korekty walutowe (kurs faktury korygowanej).

**FA(3) i KSeF**
- Czy każde pole obowiązkowe jest wypełniane zawsze, gdy powinno (nabywca bez NIP, osoba prywatna, nabywca
  zagraniczny, `Zaplacono` + `DataZaplaty`, termin płatności, rachunek, podstawa zwolnienia)?
- Czy walidacja XSD jest wykonywana przed **każdym** wydaniem XML (wysyłka, pobranie XML, PDF, e-mail, eksport ZIP)?
- Odczyt faktur otrzymanych (`Fa3InvoiceReader`): faktury innych wystawców mogą wykorzystywać pola, których nasz
  generator nie używa - czy odczyt jest odporny (brakujące elementy, wiele stawek, korekty, waluty, zaliczki)?
- Kod QR weryfikacji (`KsefQr`): format adresu, skrót dokumentu, kodowanie.

**Korekty, proformy, numeracja**
- Korekta „przed i po”: stan przed korektą po kolejnej korekcie, korekta do korekty, korekta do zera, różnice
  znaków, wpływ na rozliczenia (pozostało do zapłaty, nadpłata).
- Numeracja: unikalność przy równoległym zapisie, przełom miesiąca/roku, numery ręczne kolidujące z automatycznymi,
  osobne serie FV/KOR/PRO, strefa czasowa daty wystawienia.
- Proforma → faktura VAT: jednokrotność zamiany (wyścig), kopiowanie danych, waluta.

**Płatności i rozliczenia**
- Status płatności (opłacona / częściowo / po terminie) przy korektach, nadpłatach, płatności „Zapłacono przy
  wystawieniu”, walutach (kwoty w różnych walutach nie mogą być sumowane - sprawdź pulpit, eksport, przypomnienia).
- Granice dat: „po terminie” liczone w strefie Europe/Warsaw, dzień terminu włącznie/wyłącznie.

**Kurs NBP**
- „Ostatni dzień roboczy przed dniem sprzedaży”: weekendy, święta, brak tabeli, pamięć podręczna, strefa czasowa,
  zachowanie przy niedostępności NBP.

**Uproszczenia i wydajność**
- Powtarzane obliczenia tych samych sum w różnych miejscach (kalkulator vs XML vs PDF vs eksport) - czy da się
  mieć jedno źródło prawdy? Zbędne generowanie XML (np. kilkakrotnie dla jednego żądania).
