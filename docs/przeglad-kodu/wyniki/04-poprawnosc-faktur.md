# Raport: Obszar 04 - poprawność merytoryczna faktur
Status: ZAKOŃCZONY
Ostatnia aktualizacja: 2026-11-12 (wznowiony i dokończony przez drugiego wykonawcę)

## Postęp
- [x] Kontekst: docs/fa3-xml.md, decyzje D16, D17, D21, D30-D33, D42, D43 (+ D44 w zakresie korekt faktur importowanych)
- [x] Contracts: InvoiceCalculator.cs, Currencies.cs, VatRate.cs, Nip.cs, InvoiceDtos.cs
- [x] Api/Endpoints/InvoiceEndpoints.cs (ApplyAsync, korekty, proformy)
- [x] Api/Endpoints/PaymentEndpoints.cs
- [x] Api/Services: InvoiceNumbering.cs, InvoiceTemplates.cs, InvoiceFiles.cs, AccountingExport.cs
- [x] Api/Services/InvoicePdfRenderer.cs
- [x] Api/Integrations/Nbp/NbpExchangeRates.cs (+ ExchangeRateLookup)
- [x] Ksef/Fa3: Fa3XmlGenerator.cs, Fa3InvoiceReader.cs, Fa3SchemaValidator.cs, KsefQr.cs
- [x] Api/Endpoints/DashboardEndpoints.cs
- [x] Faktury importowane z KSeF (IssuedInvoiceImport.cs) - tylko wpływ na kwoty, korekty i rozliczenia
- [x] Testy obszaru (pokrycie) - luki wskazane przy znaleziskach („Test, który to wykryje”)
- [x] Potwierdzenie testem tymczasowym (`Tmp04Probe.cs`, usunięty): FAK-1, FAK-2, FAK-4, FAK-5, FAK-8
Następny krok: brak - przegląd zakończony.

Kandydaci z wersji roboczej (litery A-O) otrzymali numery końcowe: L→FAK-1, A→FAK-2, H→FAK-3, B→FAK-4, M→FAK-5,
D→FAK-6, E→FAK-7, N→FAK-8, F→FAK-9, O→FAK-10, G→FAK-11, J→FAK-12, K→FAK-13; C = PERF-7, I = PERF-6 (już zgłoszone
w obszarze 05 - nie powtarzam).

## Podsumowanie

Rdzeń obliczeń jest poprawny i spójny: wszędzie `decimal`, jedno zaokrąglenie „połówkowe od zera”, podatek od sumy netto
w stawce (D16), ten sam kalkulator w API, formularzu, XML i podglądzie; stawki trafiają do właściwych pól P_13_x/P_14_x,
XSD jest sprawdzane przed wysyłką i pobraniem XML, a e-mail i paczka ZIP wydają dokładnie wysłany dokument.
Najpoważniejsze ryzyka leżą na styku **korekt** z danymi, których kalkulator „nie zna”: korekta faktury zaimportowanej
z KSeF (rabat, inna metoda liczenia, korekty różnicowe) wysyła do KSeF błędne kwoty różnicy (FAK-1), a kolejność korekt
wyznaczana po `Id` zamiast po chwili przyjęcia daje zły stan „przed” (FAK-2) - oba potwierdzone testem.
Trzecie istotne ryzyko to kurs NBP dla faktur walutowych z datą sprzedaży późniejszą niż data wystawienia (FAK-3,
niezgodność z art. 31a ust. 2 i kurs zależny od chwili zapisu). Pozostałe znaleziska to rozbieżności o 0,01
(precyzja ilości - FAK-4, VAT w PLN korekt - FAK-5) i braki w przypadkach brzegowych.

Liczba znalezisk: krytyczne 1, wysokie 2, średnie 5, niskie 7 (razem 15). Bezpieczeństwo: brak nowych znalezisk
w tym obszarze (XXE w czytniku i walidatorze zablokowane; wstrzyknięcie formuł w CSV zabezpieczone).

## Znaleziska

### FAK-1. Korekta faktury zaimportowanej z KSeF wysyła błędne kwoty różnicy (stan „przed” liczony ponownie z ilość × cena)
- **Waga:** krytyczne
- **Rodzaj:** błąd
- **Pewność:** potwierdzone (test tymczasowy przez API: import faktury z rabatem `P_10`, korekta, odczyt XML)
- **Miejsce:** `FakturowanieKSeF.Api/Endpoints/InvoiceEndpoints.cs:557-561` (`CalculateCorrection` z `Quantity × UnitNetPrice` stanu przed),
  `FakturowanieKSeF.Ksef/Fa3/Fa3XmlGenerator.cs:161-164,191` (to samo w XML, `P_11` wiersza StanPrzed liczone ponownie),
  `FakturowanieKSeF.Api/Services/IssuedInvoiceImport.cs:141-151` (pozycje importu), `InvoiceEndpoints.cs:583-585` (`CurrentStateAsync`),
  `FakturowanieKSeF.Ksef/Fa3/Fa3InvoiceReader.cs:35-43` (czytnik nie zna `P_10`, `P_9B`, `P_11A`)
- **Problem:** D44 pozwala korygować w aplikacji faktury wystawione w innym programie. Stan „przed korektą” nie jest brany
  z kwot dokumentu (`P_11`, `P_13_x`, `P_14_x`), tylko przeliczany kalkulatorem aplikacji z ilości i ceny. Gdy faktura
  źródłowa nie spełnia równania `netto = ilość × cena` i „VAT od sumy netto”, różnica na korekcie jest błędna:
  (a) rabat w wierszu (`P_10`) - czytnik go pomija; (b) ceny brutto (`P_9B`/`P_11A`, bez `P_9A`/`P_11`) - import zapisuje cenę 0 i netto 0;
  (c) VAT liczony przez inny program od pozycji / od brutto - różnice groszowe; (d) korekta zaimportowana wystawiona metodą
  różnicową (wiersze z ujemną ilością, bez `StanPrzed`) - `CurrentStateAsync` traktuje jej wiersze różnicy jako „stan po korekcie”.
- **Scenariusz (potwierdzony):** faktura z innego programu: 2 szt. × 100 zł, rabat `P_10` = 20 → `P_11` = 180,00, `P_15` = 221,40.
  Po imporcie: `TotalNet` = 180, ale pozycja ma ilość 2, cenę 100. Korekta w aplikacji do 1 szt. × 90 zł (prawidłowa różnica:
  -90,00 netto / -20,70 VAT / -110,70 brutto) daje `P_13_1` = -110,00, `P_14_1` = -25,30, `P_15` = -135,30, a wiersz StanPrzed
  ma `P_11` = 200,00 (na fakturze pierwotnej było 180,00). Do KSeF trafia korekta z zawyżonym zmniejszeniem podatku o 4,60 zł;
  należność faktury w rozliczeniach = 221,40 - 135,30 = 86,10 zamiast 110,70.
- **Propozycja poprawki:** stan „przed” opierać na kwotach zapisanych, nie przeliczanych: netto wiersza z `InvoiceItem.NetAmount`,
  a sumy per stawka z dokumentu (dla importu - z `P_13_x`/`P_14_x`). Do czasu poprawki: blokować korektę faktury importowanej,
  gdy `Σ round(ilość × cena) ≠ TotalNet`, gdy przeliczony VAT ≠ `TotalVat`, gdy którakolwiek pozycja nie ma `P_9A`/`P_11`
  albo gdy istnieje zaimportowana korekta bez wierszy `StanPrzed` (komunikat: „skoryguj w programie, w którym wystawiono fakturę”).
- **Test, który to wykryje:** `IssuedImportTests.KorektaFakturyImportowanejZRabatem_RoznicaZgodnaZDokumentem` - import XML z `P_10`,
  korekta przez API, asercje `P_13_1`/`P_14_1`/`P_15` i `P_11` wiersza StanPrzed; drugi przypadek: import korekty różnicowej, potem korekta w aplikacji → 409/422.

### FAK-2. „Ostatnia przyjęta korekta” wybierana po `Id`, a nie po chwili przyjęcia - zły stan „przed” kolejnej korekty
- **Waga:** wysokie (skutek klasy krytycznej - błędna kwota podatku w KSeF; wymaga dwóch szkiców korekt jednocześnie)
- **Rodzaj:** błąd
- **Pewność:** potwierdzone (test tymczasowy przez API)
- **Miejsce:** `FakturowanieKSeF.Api/Endpoints/InvoiceEndpoints.cs:577-586` (`OrderByDescending(c => c.Id)` + `FirstOrDefault(Accepted)`);
  korzystają z tego `ApplyAsync:441` i `CheckCorrectionStateAsync:597`
- **Problem:** `Id` odzwierciedla kolejność utworzenia szkicu, nie kolejność przyjęcia przez KSeF. Szkic starszej korekty zapisany
  ponownie i przyjęty później niż korekta o większym `Id` nie jest uznawany za stan bieżący.
- **Scenariusz (potwierdzony):** faktura 10 × 150 zł. Szkic K1 (cena 120, Id=4), szkic K2 (cena 100, Id=5). Wysyłka K2 → przyjęta.
  Wysyłka K1 → 409 (prawidłowo), ponowny zapis K1 (stan przed = 100, po = 120, +246 zł), wysyłka → przyjęta. Stan faktury: cena 120.
  Nowa korekta K3 (cena 90): stan przed = **100** (z K2) zamiast 120, różnica **-123,00** zamiast -369,00. K3 trafia do KSeF
  z błędnymi wierszami StanPrzed i kwotami; suma dokumentów w KSeF nie daje stanu końcowego, należność w rozliczeniach zawyżona o 246 zł.
  Kontrola przed wysyłką (`CheckCorrectionStateAsync`) używa tej samej funkcji, więc niczego nie wykrywa.
- **Propozycja poprawki:** sortować po chwili przyjęcia (`KsefAcceptedAt`, w razie remisu `KsefNumber`/`Id`), a dla korekt `Pending`
  utrzymać blokadę. Alternatywnie zapisywać w korekcie wskaźnik „poprzednia korekta” i iść po łańcuchu.
- **Test, który to wykryje:** `CorrectionTests.KolejnaKorekta_StanPrzedZKorektyPrzyjetejJakoOstatnia_NieZNajwiekszymId` (scenariusz powyżej, asercja `ItemsBefore[0].UnitNetPrice == 120`).

### FAK-3. Kurs NBP dla faktury walutowej z datą sprzedaży późniejszą niż data wystawienia: zła data kursu, kurs zależny od chwili zapisu, błąd przy dacie odległej
- **Waga:** wysokie
- **Rodzaj:** błąd
- **Pewność:** potwierdzone analizą kodu i przepisu (art. 31a ust. 2 ustawy o VAT); zachowanie pamięci podręcznej - do weryfikacji testem z `FakeNbpClient`
- **Miejsce:** `FakturowanieKSeF.Api/Endpoints/InvoiceEndpoints.cs:541` (`SaleDate ?? IssueDate`),
  `FakturowanieKSeF.Api/Integrations/Nbp/NbpExchangeRates.cs:69-81` (przedział `taxPoint-14 … taxPoint-1`, pamięć 12 h); to samo w podglądzie kursu w formularzu
- **Problem:** gdy fakturę wystawia się przed powstaniem obowiązku podatkowego (data sprzedaży > data wystawienia - typowe dla
  faktur „z góry”), art. 31a ust. 2 każe użyć kursu z ostatniego dnia roboczego przed **dniem wystawienia**. Kod zawsze bierze dzień
  sprzedaży. Dla daty sprzedaży w przyszłości przedział zapytania sięga w przyszłość, więc wynikiem jest „ostatni opublikowany kurs
  w chwili zapisu”: (1) ten sam szkic zapisany w poniedziałek i w środę dostaje różne kursy; (2) wynik jest buforowany pod kluczem
  daty na 12 h - po publikacji nowej tabeli (ok. 12:00) nadal zwracany jest stary; (3) kurs nie jest odświeżany przy wysyłce;
  (4) data sprzedaży > 14 dni naprzód → pusty wynik → „Brak kursu średniego NBP…” i faktury nie da się zapisać.
- **Scenariusz:** 12.11 (czwartek) faktura w EUR, wystawienie 12.11, sprzedaż 30.11. Prawidłowo: kurs z 10.11 (11.11 - święto).
  Aplikacja: zapytanie 16-29.11 → 404/pusto → błąd walidacji „Nie udało się pobrać kursu NBP”. Dla sprzedaży 20.11: zapytanie 6-19.11 →
  kurs z 12.11, jeżeli tabela już opublikowana, inaczej z 10.11 - `P_14_xW` zależy od godziny zapisu i jest niezgodne z przepisem.
- **Propozycja poprawki:** dzień odniesienia = `min(SaleDate ?? IssueDate, IssueDate)`; odrzucać/nie buforować wyników dla dat
  odniesienia późniejszych niż dzisiaj (czas polski); uzupełnić D43 o ust. 2.
- **Test, który to wykryje:** `CurrencyTests.FakturaWystawionaPrzedSprzedaza_KursSprzedDniaWystawienia` oraz
  `…DataSprzedazyZaMiesiac_ZapisDziala_IKursNieZalezyOdChwiliZapisu` (FakeNbpClient z notowaniami po dacie wystawienia).

### FAK-4. Ilość i cena z większą liczbą miejsc niż w bazie: `TotalGross` w bazie ≠ `P_15` w XML (różnica 0,01)
- **Waga:** średnie
- **Rodzaj:** błąd
- **Pewność:** potwierdzone (test tymczasowy przez API)
- **Miejsce:** `FakturowanieKSeF.Contracts/Dtos/InvoiceDtos.cs:114-120` (brak ograniczenia liczby miejsc po przecinku),
  `FakturowanieKSeF.Api/Endpoints/InvoiceEndpoints.cs:503-504,565-567` (sumy z wartości z żądania), `Models/InvoiceItem.cs:27,31`
  (`numeric(22,6)` / `numeric(22,8)` - PostgreSQL zaokrągla), `Fa3XmlGenerator.cs:153,168` (XML liczony z wartości z bazy);
  analogicznie `AddPaymentRequest.Amount` (`PaymentDtos.cs:27`, `numeric(18,2)`)
- **Problem:** sumy faktury są liczone z niezaokrąglonych wartości żądania i zapisywane, a ilość/cena zapisują się zaokrąglone;
  XML, podgląd per stawka i PDF liczą ponownie z wartości zaokrąglonych. Dwa źródła prawdy rozjeżdżają się o grosz.
- **Scenariusz (potwierdzony):** pozycja ilość 1,0000004 × 12 500 zł (23%) + 3 × 0,123456789 zł (8%). Baza/DTO/lista/rozliczenia:
  netto pozycji 12 500,01, brutto faktury **15 375,41**; XML: `P_8B` = 1, `P_11` = 12500.00, `P_15` = **15375.40**; podsumowanie
  per stawka w tym samym DTO pokazuje 12 500,00 / 2 875,00 przy sumie 12 500,38 / 2 875,03. Po wpłacie kwoty z faktury (15 375,40)
  dokument zostaje „częściowo opłacony” (0,01 zł), może trafić do przypomnień o zaległości.
- **Propozycja poprawki:** walidacja skali w DTO (ilość ≤ 6, cena ≤ 8, kwota wpłaty ≤ 2 miejsc; wspólny atrybut w Contracts, działa
  też w formularzu) albo zaokrąglenie wartości wejściowych przed obliczeniami w `ApplyAsync`.
- **Test, który to wykryje:** `InvoiceApiTests.IloscZSiedmiomaMiejscami_JestOdrzucana` (400) oraz test spójności
  `TotalGross == P_15` z XML dla wartości granicznych skali.

### FAK-5. Podatek w PLN (`P_14_xW`) korekty walutowej liczony z różnicy, nie jako różnica stanów - 0,01 zł po serii korekt
- **Waga:** średnie
- **Rodzaj:** błąd
- **Pewność:** potwierdzone (test jednostkowy kalkulatora)
- **Miejsce:** `FakturowanieKSeF.Contracts/Calculation/InvoiceCalculator.cs:51-54` (`VatInPln`), wywołania: `Fa3XmlGenerator.cs:219,230`,
  `Api/Infrastructure/Mapping.cs:118`
- **Problem:** kwoty w walucie korekty to „po - przed” (każdy stan zaokrąglany osobno, więc korekty sumują się do zera), ale
  podatek w PLN to `round(różnica × kurs)`. Suma `P_14_xW` faktury i jej korekt nie odpowiada wtedy stanowi końcowemu.
- **Scenariusz (potwierdzony):** EUR, kurs 4,2345. Faktura netto 43,52 (VAT 10,01 → 42,39 zł). K1: do netto 21,74 (VAT 5,00) →
  `P_14_1W` = -21,21. K2: do zera → -21,17. Suma w ewidencji: 42,39 - 21,21 - 21,17 = **0,01 zł** VAT po korekcie do zera.
- **Propozycja poprawki:** `P_14_xW` korekty = `round(VAT_po × kurs) - round(VAT_przed × kurs)` (osobna funkcja dla korekt przyjmująca oba stany).
- **Test, który to wykryje:** `CurrencyTests.KorektyWalutowe_PodatekWPlnSumujeSieDoZeraPoKorekcieDoZera`.

### FAK-6. Dwie różne korekty tej samej faktury wysłane równolegle przechodzą kontrolę stanu
- **Waga:** średnie
- **Rodzaj:** błąd (wyścig)
- **Pewność:** potwierdzone analizą kodu; do weryfikacji testem współbieżnym
- **Miejsce:** `FakturowanieKSeF.Api/Endpoints/InvoiceEndpoints.cs:166-200` (`/send`), `:593-607` (`CheckCorrectionStateAsync`)
- **Problem:** token współbieżności (`Version`) chroni pojedynczą korektę. Dwa żądania `/send` dla dwóch szkiców korekt tej samej
  faktury wykonują kontrolę „brak korekty Pending” zanim którekolwiek zapisze status - oba przechodzą.
- **Scenariusz:** dwa szkice korekt z tym samym stanem „przed” (150 zł): K1 → 120, K2 → 100; dwie osoby (właściciel i pracownik, D46)
  klikają „Wyślij” jednocześnie → obie `Pending`, obie przyjęte; w KSeF dwie korekty „z 150”, łączna różnica -80 zamiast stanu końcowego jednej z nich.
- **Propozycja poprawki:** przy wysyłce korekty blokować wiersz faktury korygowanej (`SELECT … FOR UPDATE` w transakcji) albo podbijać
  jej `Version` (aktualizacja z tokenem współbieżności) - drugie żądanie dostanie 409.
- **Test, który to wykryje:** `CorrectionTests.DwieKorektyWyslaneRownolegle_JednaDostaje409` (`Task.WhenAll` na dwóch `/send`).

### FAK-7. Stawki 0% WDT, 0% eksport, „np I/II” dostępne, ale nabywca zagraniczny trafia do XML jako `BrakID` (konsument)
- **Waga:** średnie
- **Rodzaj:** błąd (niepełna obsługa udostępnionej funkcji)
- **Pewność:** potwierdzone analizą kodu (`Fa3XmlTests.OsobaPrywatnaBezNip_MaZnacznikBrakID` utrwala to zachowanie); reakcja KSeF - do weryfikacji
- **Miejsce:** `FakturowanieKSeF.Ksef/Fa3/Fa3XmlGenerator.cs:122-127` (`NIP` albo `BrakID`), `Contracts/Dtos/InvoiceDtos.cs:257` (`[Nip]` - tylko polski NIP),
  `Contracts/Enums/VatRate.cs:39-41` (`Selectable` zawiera WDT/eksport/np)
- **Problem:** FA(3) ma dla nabywcy `KodUE` + `NrVatUE` (kontrahent z UE) i `KodKraju` + `NrID` (spoza UE). Aplikacja pozwala wybrać
  kraj adresu i stawki transakcji zagranicznych, lecz nie pozwala podać identyfikatora - faktura WDT / eksport usług trafia do KSeF
  z `BrakID = 1`, czyli jako sprzedaż osobie bez identyfikatora. Dla WDT numer VAT-UE nabywcy jest obowiązkowym elementem faktury
  (art. 106e ust. 1 pkt 24), brak też prefiksu `PL` sprzedawcy (`PrefiksPodatnika`).
- **Scenariusz:** nabywca „Muster GmbH”, kraj DE, pozycja 0% WDT → XML przechodzi XSD, `P_13_6_2` wypełnione, `Podmiot2/BrakID` = 1.
- **Propozycja poprawki:** dodać do nabywcy pola identyfikatora zagranicznego (VAT-UE / inny) i generować `KodUE`+`NrVatUE` lub `KodKraju`+`NrID`;
  do tego czasu blokować w walidacji stawki `0 WDT` bez numeru VAT-UE (albo usunąć je z `Selectable`).
- **Test, który to wykryje:** `Fa3XmlTests.NabywcaZUE_MaNrVatUE_AWdtBezNumeruJestOdrzucane`.

### FAK-8. Import faktury oznaczonej „Zapłacono” nie tworzy płatności - opłacona faktura widnieje jako należność
- **Waga:** średnie
- **Rodzaj:** błąd
- **Pewność:** potwierdzone (test tymczasowy: import z `Zaplacono`/`DataZaplaty` → `PaidOn` ustawione, `PaymentStatus = Unpaid`)
- **Miejsce:** `FakturowanieKSeF.Api/Services/IssuedInvoiceImport.cs:169` (tylko `PaidOn`), por. `InvoiceEndpoints.cs:397-415` (`RecordPaymentFromInvoice` wołane tylko w `/send` i seedzie)
- **Problem:** D33 zakłada, że zapłata oznaczona w XML jest płatnością w rozliczeniu; import (D44) tego nie robi.
- **Scenariusz:** faktura gotówkowa z innego programu (`Zaplacono = 1`) po imporcie: lista „nieopłacona”, wchodzi do sumy należności
  na pulpicie i w `/payments/summary`; jeżeli ma termin płatności - po jego upływie „po terminie” i automatyczne przypomnienie do nabywcy, który zapłacił.
- **Propozycja poprawki:** w imporcie faktury VAT z `PaidOn` dodać płatność `FromInvoice = true` na kwotę `P_15` (to samo co `RecordPaymentFromInvoice`).
- **Test, który to wykryje:** `IssuedImportTests.FakturaZaplaconaPrzyWystawieniu_PoImporcieJestOplacona`.

### FAK-9. Daty „dzisiaj” według zegara serwera zamiast czasu polskiego
- **Waga:** niskie
- **Rodzaj:** błąd (przypadek brzegowy)
- **Pewność:** potwierdzone analizą kodu (uwaga przejęta z obszaru 05, sekcja „Poza obszarem”)
- **Miejsce:** `FakturowanieKSeF.Api/Endpoints/InvoiceEndpoints.cs:304` (`/invoices/next-number`), `FakturowanieKSeF.Contracts/Dtos/InvoiceDtos.cs:150` (domyślna `IssueDate`)
- **Problem:** reszta kodu używa `PolishDate.Today(time)`. Przy serwerze w UTC między 00:00 a 01:00/02:00 czasu polskiego „dzisiaj” to dzień poprzedni.
- **Scenariusz:** 1.12 o 00:30 czasu polskiego `GET /invoices/next-number` (bez daty) proponuje numer z serii `/11/2026`; żądanie API bez
  pola `issueDate` dostaje datę wystawienia 30.11 i numer listopadowy.
- **Propozycja poprawki:** `PolishDate.Today(time)` w endpoincie; w DTO brak wartości domyślnej zależnej od zegara (pole wymagane albo uzupełniane w `ApplyAsync`).
- **Test, który to wykryje:** `InvoiceApiTests.NastepnyNumer_NaPrzelomieMiesiaca_WedlugCzasuPolskiego` (FakeTimeProvider 30.11 23:30 UTC).

### FAK-10. PDF: cena jednostkowa zawsze z dwoma miejscami, choć `P_9A` dopuszcza osiem
- **Waga:** niskie
- **Rodzaj:** błąd (wizualizacja niezgodna z dokumentem)
- **Pewność:** potwierdzone analizą kodu
- **Miejsce:** `FakturowanieKSeF.Api/Services/InvoicePdfRenderer.cs:209` (`p.ToString("N2", Pl)`)
- **Problem:** cena 0,12345679 zł jest drukowana jako 0,12; wiersz „1000 × 0,12 = 123,46” wygląda na błąd rachunkowy.
- **Scenariusz:** pozycja 1000 kg × 0,12345679 zł → PDF: ilość 1000, cena 0,12, wartość 123,46.
- **Propozycja poprawki:** format `"#,##0.00######"` (min. 2, maks. 8 miejsc), jak dla ilości (`0.######`).
- **Test, który to wykryje:** `PdfTests.CenaZWielomaMiejscami_JestDrukowanaBezObciecia`.

### FAK-11. Czytnik FA pomija elementy używane przez innych wystawców (PDF i eksport faktur otrzymanych/importowanych)
- **Waga:** niskie (skutki dla korekt opisane osobno w FAK-1)
- **Rodzaj:** błąd (niepełny odczyt)
- **Pewność:** potwierdzone analizą kodu
- **Miejsce:** `FakturowanieKSeF.Ksef/Fa3/Fa3InvoiceReader.cs:35-72`
- **Problem:** nieodczytywane: `P_9B`/`P_11A` (faktury „od brutto” - puste kolumny ceny i wartości), `P_10` (rabat - wiersz „nie zgadza się”),
  `P_11Vat`, kolejne `TerminPlatnosci` (tylko pierwszy) i `TerminOpis`, płatności częściowe (`ZaplataCzesciowa`), `KursWalutyZ` (kurs faktury
  zaliczkowej - brak kursu w eksporcie), `RachunekBankowyFaktora`, wiersze `Zamowienie` faktur zaliczkowych (pusta tabela pozycji),
  `P_13_5` bez `P_14_5` jest pokazywane jako VAT 0,00.
- **Scenariusz:** faktura zakupu wystawiona „od brutto” → PDF/ podgląd z pustymi kolumnami „Cena netto” i „Wartość netto”; faktura zaliczkowa → PDF bez pozycji.
- **Propozycja poprawki:** uzupełnić czytnik o `P_9B`, `P_10`, `P_11A`, `Zamowienie/ZamowienieWiersz`, `KursWalutyZ`, listę terminów; w PDF kolumna „Rabat” i „Cena brutto”, gdy występują.
- **Test, który to wykryje:** `ReceivedInvoicesTests.Odczyt_FakturaOdBrutto_ZRabatem_IZaliczkowa` na oficjalnych przykładach MF (FA_3_Przykład_*).

### FAK-12. Odpowiedź NBP niebędąca poprawnym JSON-em kończy się błędem 500 zamiast komunikatu walidacji
- **Waga:** niskie
- **Rodzaj:** błąd
- **Pewność:** do weryfikacji (brakuje testu z atrapą `HttpMessageHandler` zwracającą 200 + HTML); kod: brak `catch` dla `JsonException`/`FormatException`
- **Miejsce:** `FakturowanieKSeF.Api/Integrations/Nbp/NbpExchangeRates.cs:50-53`, `InvoiceEndpoints.cs:544` (łapany tylko `NbpException`)
- **Problem:** `ReadFromJsonAsync` i `DateOnly.ParseExact` rzucają wyjątki inne niż `NbpException` (strona błędu/ochrony przed botami z kodem 200,
  ucięta odpowiedź) → zapis faktury walutowej zwraca 500. D43 opisuje taki przypadek dla Białej listy, a klient NBP go nie obsługuje. (Błąd 500 dla skrajnej daty - zob. SEC-6.)
- **Propozycja poprawki:** opakować odczyt w `catch (Exception ex) when (ex is JsonException or FormatException or NotSupportedException)` → `NbpException`.
- **Test, który to wykryje:** `CurrencyTests.KlientNbp_OdpowiedzHtml_DajeNbpException`.

### FAK-13. Eksport CSV: kolumny per stawka nie sumują się do „Razem”, a podsumowanie pomija VAT w PLN faktur walutowych
- **Waga:** niskie
- **Rodzaj:** błąd (zestawienie dla księgowej)
- **Pewność:** potwierdzone analizą kodu
- **Miejsce:** `FakturowanieKSeF.Api/Services/AccountingExport.cs:30-39` (`RateColumns` bez grup „ryczałt taxi”, „procedura OSS”, „marża”),
  `:72-83` (`Summary` - tylko dokumenty w PLN)
- **Problem:** (1) faktura zakupu z `P_13_4`, `P_13_5` lub `P_13_11` ma „Razem netto” większe niż suma kolumn stawek - brak kolumny „inne”;
  (2) `Summary` liczy VAT sprzedaży/zakupów tylko z faktur w PLN, choć dla faktur walutowych znana jest kwota podatku w złotych (`P_14_xW`,
  kolumna „VAT w PLN” w CSV) - „VAT sprzedaży” na ekranie eksportu jest zaniżony względem ewidencji.
- **Propozycja poprawki:** dodać kolumnę „Netto inne / VAT inne” (taxi, OSS, marża); w podsumowaniu pokazać osobno „VAT w PLN z faktur walutowych” albo doliczyć go do VAT.
- **Test, który to wykryje:** `ExportTests.KolumnyStawekSumujaSieDoRazem_DlaFakturyMarza` i `…PodsumowanieZawieraVatWPlnFakturWalutowych`.

### FAK-14. Brak górnych granic kwot i dat: przepełnienie → 500, daty spoza zakresu FA(3) wykrywane dopiero przy wysyłce
- **Waga:** niskie
- **Rodzaj:** błąd (przypadek brzegowy)
- **Pewność:** do weryfikacji testem (granice wynikają z kodu: `Range` do 999999999999 × 99999999999999, kolumny `numeric(18,2)`)
- **Miejsce:** `FakturowanieKSeF.Contracts/Dtos/InvoiceDtos.cs:114-120,150,155`, `Models/Invoice.cs:76-82`
- **Problem:** ilość 10^12 × cena 10^14 mieści się w walidacji, ale nie w `numeric(18,2)` (ani w `TKwotowy` FA(3)) → `DbUpdateException` → 500;
  przy 1000 takich pozycji `OverflowException` typu `decimal`. `IssueDate`/`SaleDate` nie mają żadnych granic (np. rok 0001 lub 9999; data
  wystawienia w przyszłości) - XSD/KSeF odrzuci je dopiero przy wysyłce.
- **Propozycja poprawki:** walidacja wartości pozycji i sumy faktury (np. < 10^13) oraz zakresu dat (≥ 2006-01-01 jak w XSD, data wystawienia ≤ dziś + rozsądny margines) w `IValidatableObject`.
- **Test, który to wykryje:** `InvoiceApiTests.KwotaPozaZakresem_400_ZamiastBledu500`, `…DataWystawieniaPozaZakresem_400`.

### FAK-15. Sumy faktury liczone ponownie w czterech miejscach - brak jednego źródła prawdy
- **Waga:** niskie
- **Rodzaj:** uproszczenie
- **Pewność:** potwierdzone analizą kodu
- **Miejsce:** `InvoiceEndpoints.cs:503-568` (zapis), `Infrastructure/Mapping.cs:80-99,118` (każdy odczyt DTO), `Fa3XmlGenerator.cs:157-171,191` (XML),
  `IssuedInvoiceImport.cs:144-151` (import); zapisane `InvoiceItem.NetAmount/VatAmount/GrossAmount` i `Invoice.Total*` nie są używane przez XML ani podsumowanie per stawka
- **Problem:** podsumowanie per stawka i kwoty korekty są każdorazowo przeliczane z ilości i cen, a sumy faktury czytane z kolumn. Dopóki
  wszystkie dane pochodzą z kalkulatora, wyniki są równe; każde odstępstwo (FAK-1 - import, FAK-4 - skala) daje dwie różne „prawdy”.
  Dodatkowo `ToDto` liczy pełne podsumowanie przy każdej odpowiedzi, a PDF szkicu generuje XML i od razu go parsuje (`InvoiceFiles.cs:36`).
- **Propozycja poprawki:** jedna funkcja `InvoiceAmounts.For(Invoice)` zwracająca podsumowanie per stawka z zapisanych `NetAmount` wierszy
  (dla importu - z dokumentu), używana przez XML, DTO i PDF; test-strażnik `Invoice.TotalGross == P_15` wykonywany przy wysyłce (odmowa 422 przy rozbieżności).
  Uzupełnia PERF-12 i PERF-16.
- **Test, który to wykryje:** test własności (losowe pozycje): `TotalNet/TotalVat/TotalGross` z bazy == sumy `P_13_x`/`P_14_x`/`P_15` z XML == `VatSummary` z DTO.

## Sprawdzone bez uwag

- **Kalkulator** (`InvoiceCalculator`): `decimal` wszędzie, `MidpointRounding.AwayFromZero`, podatek od sumy netto w stawce, korekta jako
  różnica stanów (sumuje się do zera przy korekcie do zera), stawki 22%/7% w grupach P_13_1/P_13_2; brak `double` w kwotach w całym zakresie.
- **Stawki → pola FA(3)**: 23/22 → P_13_1/P_14_1, 8/7 → _2, 5 → _3, 0 KR/WDT/EX → P_13_6_1/2/3, zw → P_13_7 + P_19/P_19A, np I/II → P_13_8/9,
  oo → P_13_10 + P_18 = 1; 4%/3% odrzucane w walidacji i generatorze. Kody P_12 zgodne ze schematem (test `VatRateTests`).
- **Pola warunkowe XML**: P_6 tylko gdy różna od P_1; `Zaplacono`+`DataZaplaty` przed `TerminPlatnosci`; brak `Platnosc` na korekcie „do zwrotu”;
  `P_14_xW` i `KursWaluty` tylko dla walut obcych; korekta z kursem faktury korygowanej; UTF-8 bez BOM.
- **Walidacja XSD przed wydaniem**: `/send` i `/xml` walidują; e-mail i ZIP wydają wyłącznie `SentXml` (zwalidowany przy wysyłce lub pochodzący z KSeF);
  PDF szkicu powstaje z niezwalidowanego XML, ale XML nie jest wydawany, a wydruk jest oznaczony jako szkic.
- **`Fa3SchemaValidator`**: DTD zabronione, `XmlResolver = null`, importy schematów tylko z zasobów osadzonych, zbiór kompilowany raz.
- **`KsefQr`**: adres `qr-test…/invoice/{NIP}/{DD-MM-RRRR}/{SHA-256 Base64URL}` zgodny ze specyfikacją KODU I; skrót liczony z dokładnie wysłanych bajtów.
- **Numeracja**: osobne serie FV/KOR/PRO, przełom miesiąca/roku (sufiks z daty wystawienia), numery ręczne w serii podbijają licznik, indeks unikalny
  pomija rekordy usunięte i importowane; współbieżność - zob. PERF-13 (nie powtarzam).
- **Proforma → faktura VAT**: kopiowanie waluty, pozycji, podstawy zwolnienia, termin i kurs ustalane na nowo, numer z serii FV; wyścig - zob. PERF-7.
- **Rozliczenia**: status z kwoty po korektach (`TotalDue`), nadpłata po korekcie, „po terminie” dopiero dzień po terminie i według czasu polskiego,
  płatność „Zapłacono” niedublowana po ponownej wysyłce; wyścig wpłat - zob. PERF-6. Kwoty w różnych walutach nie są sumowane (pulpit, podsumowanie,
  przypomnienia per faktura z walutą); wykres sprzedaży przelicza kursem z faktury (D43).
- **Eksport**: kwoty z wysłanego XML, korekty ujemne, ochrona przed formułami w CSV, zakres dat zakupów w czasie polskim.
- **Bezpieczeństwo** w obszarze: brak nowych znalezisk. **Wydajność**: poza FAK-15 brak nowych (PERF-1, PERF-5, PERF-12 pokrywają resztę).

## Poza obszarem

Brak nowych. Uwagi powiązane, już zgłoszone: PERF-6 (wyścig wpłat), PERF-7 (wyścig zamiany proformy), PERF-13 (ponowienia numeracji),
ISO-6 (zapytania korekt bez `CompanyId`), SEC-6 (podgląd kursu NBP), KSEF-13 (NBP a faktury cykliczne).

## Pytania do autora

1. **Korekty faktur importowanych (FAK-1):** czy mają pozostać dostępne (wtedy stan „przed” musi pochodzić z kwot dokumentu), czy lepiej je
   wyłączyć dla dokumentów, których aplikacja nie potrafi odtworzyć?
2. **Data wystawienia a chwila wysyłki:** szkic z `IssueDate` sprzed kilku dni jest wysyłany z tą datą w `P_1`, a faktura jest wystawiona
   w dniu przesłania do KSeF (art. 106na ust. 1). Czy przy wysyłce data ma być aktualizowana / ma pojawić się ostrzeżenie?
3. **`NrWierszaFa` w korektach:** wiersze „przed” i „po” są numerowane kolejno (1…n, n+1…m). Broszura FA(3) zaleca powiązanie wiersza korygowanego
   i korygującego tym samym numerem - czy obecna numeracja jest świadoma (KSeF testowy ją przyjmuje)?
4. **Numer nadawany szkicowi:** usunięcie szkicu ze środka serii zostawia lukę albo numer jest użyty ponownie po usunięciu ostatniego - czy
   numer nie powinien być nadawany dopiero przy wysyłce?
5. **Kurs korekty walutowej (D43):** zawsze kurs faktury korygowanej - poprawne dla korekt „in minus” i błędów pierwotnych; czy korekty z nowej
   przyczyny (np. podwyżka ceny po sprzedaży) są świadomie poza zakresem?
