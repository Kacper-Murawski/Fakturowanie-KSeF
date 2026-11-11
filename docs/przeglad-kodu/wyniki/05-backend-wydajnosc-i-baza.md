# Raport: Obszar 05 - backend: wydajność, baza danych, współbieżność
Status: ZAKOŃCZONY
Ostatnia aktualizacja: 2026-11-11 (koniec przeglądu)

## Postęp
- [x] AppDbContext.cs + snapshot (indeksy, typy kolumn) - PERF-10
- [x] Modele (`FakturowanieKSeF.Models/Models/*`)
- [x] Program.cs i konfiguracja (czasy życia usług, HttpClient, cache, logowanie) - PERF-15, PERF-17
- [x] Endpoints: InvoiceEndpoints.cs - PERF-3, 4, 7, 11, 12, 13, 16
- [x] Endpoints: pozostałe pliki - PERF-1, 5, 6, 9, 11
- [x] Services: usługi w tle i zapytania EF - PERF-8, 10, 14 (bez powtarzania KSEF-2, KSEF-4, KSEF-16, KSEF-19)
- [x] InvoicePdfRenderer.cs, AccountingExport.cs (pamięć) - PERF-1, PERF-2
- [x] Współbieżność (numeracja, unikalność, 409 vs 500, transakcje) - PERF-6, 7, 8, 13, 14
- [x] Pomiar zapytań EF (test tymczasowy na kopii repozytorium z danymi demonstracyjnymi i atrapami)
- [x] Uproszczenia - PERF-16
Następny krok: brak - przegląd zakończony.

## Podsumowanie

Typowe ekrany są pod względem liczby zapytań w dobrym stanie: pomiar na danych demonstracyjnych pokazał 2-6 zapytań na żądanie,
bez zapytań N+1 na listach i pulpicie, ze stronicowaniem i agregacją po stronie bazy. Najpoważniejsze ryzyka to (1) wyścigi
„sprawdź, potem zapisz” niechronione ani indeksem, ani wersją wiersza - potwierdzona testem nadpłata przy równoległych wpłatach
(PERF-6) i kilka faktur z jednej proformy (PERF-7), (2) eksport dla księgowej, który nawet dla samego podsumowania wczytuje
i parsuje wszystkie XML-e okresu i buduje paczkę w pamięci (PERF-1, PERF-2), (3) duże kolumny XML pobierane tam, gdzie nie są
potrzebne, w tym w iloczynie kartezjańskim pozycje × wysyłki (PERF-3, PERF-4). Naruszenia unikalności i konflikty wersji są
poprawnie zamieniane na 409 (`ApiErrors.ExceptionHandler`), wysyłka do KSeF zapisuje XML, stan i płatność jednym `SaveChanges`.
Znalezisk: 17 (krytyczne 0, wysokie 2, średnie 8, niskie 7). Nie powtarzano znalezisk obszarów 02 i 03 (ISO-1, ISO-2,
KSEF-2, KSEF-4, KSEF-8, KSEF-10, KSEF-14, KSEF-16, KSEF-19) - są przywołane tam, gdzie się stykają.

## Pomiar

Wykonany testem tymczasowym (usuniętym) na kopii repozytorium poza katalogiem roboczym: `ApiFactory` z atrapami KSeF/NBP/VAT/SMTP,
dane `DemoDataSeeder`, konto „Hurtownia Biuro-Max” (17 faktur, 13 otrzymanych, 12 wysyłek), przechwycone zdarzenia
`Microsoft.EntityFrameworkCore.Database.Command` (każde żądanie zawiera 1 zapytanie `CurrentCompany`).

| Żądanie | Zapytań | W tym z kolumnami XML | JOIN pozycje × wysyłki | Odpowiedź |
|---|---|---|---|---|
| `GET /api/dashboard` | 5 | 0 | - | 1,3 KB |
| `GET /api/payments/summary` | 3 | 0 | - | 0,2 KB |
| `GET /api/invoices` | 3 | 0 | - | 6,1 KB |
| `GET /api/invoices?q=fv&payment=Unpaid` | 3 | 0 | - | 0,8 KB |
| `GET /api/invoices/{id}` | 6 | 1 | - | 2,3 KB |
| `GET /api/invoices/{id}/pdf` | 2 | 1 | tak | 63 KB |
| `GET /api/invoices/{id}/xml` | 2 | 1 | tak | 2,6 KB |
| `GET /api/received-invoices` | 3 | 0 | - | 5,5 KB |
| `GET /api/received-invoices/{id}` | 2 | 1 (potrzebne) | - | 1,3 KB |
| `GET /api/customers`, `/api/products`, `/api/recurring-invoices` | 2 | 0 | - | < 1 KB |
| `GET /api/exports/summary` (rok) | 4 | 2 | - | 0,3 KB |
| `GET /api/exports/sales.csv` (rok) | 3 | 1 | - | 2,7 KB |
| `GET /api/exports/package.zip` (rok) | 4 | 2 | - | 29,6 KB |

Wnioski: liczba zapytań jest stała (brak N+1 na ekranach); problemem jest zawartość zapytań (PERF-1, 3, 4), a nie ich liczba.
Testy wyścigów: 8 równoległych wpłat → 8 × 200 (PERF-6); 8 równoległych konwersji proformy → 4 × 201 + 4 × 409 (PERF-7, PERF-13).
Aplikacji nie uruchamiano jako osobnego procesu (seeder przy fakturze w EUR sięgałby do NBP) - pomiar tylko przez `WebApplicationFactory`.

## Znaleziska

### PERF-1. Podsumowanie eksportu i pliki CSV wczytują i parsują wszystkie XML-e okresu (także UPO), choć potrzebują kilku liczb
- **Waga:** wysokie
- **Rodzaj:** wydajność
- **Pewność:** potwierdzone pomiarem (sekcja „Pomiar”: `/exports/summary` wykonuje 2 zapytania z kolumnami XML i zwraca 266 bajtów) i analizą kodu
- **Miejsce:** `FakturowanieKSeF.Api/Services/AccountingExport.cs:45-69`, `FakturowanieKSeF.Api/Endpoints/ExportEndpoints.cs:16-26` (`/exports/summary`), `:28-48` (`sales.csv`, `purchases.csv`)
- **Problem:** `SalesAsync` pobiera pełne encje faktur z `Include(KsefSubmissions)` - czyli **wszystkie** wysyłki każdej faktury (także odrzucone) z kolumnami `SentXml`, `UpoXml`, `StatusDetails` - a `PurchasesAsync` pełne `ReceivedInvoices` z `Xml`; każdy XML jest następnie parsowany (`Fa3InvoiceReader.Read`). Dzieje się to również dla `GET /exports/summary`, które zwraca wyłącznie liczbę faktur i sumy netto/VAT/brutto, oraz dla CSV, które nie potrzebują UPO ani odrzuconych wysyłek.
- **Scenariusz:** firma z 3000 faktur sprzedaży i 3000 zakupu w roku otwiera ekran eksportu z zakresem rocznym (limit 366 dni): serwer wczytuje ~6000 dokumentów XML + 3000 UPO (rzędu 100+ MB tekstu w UTF-16), parsuje je i zwraca 12 liczb. Każda zmiana zakresu dat powtarza operację; kilku użytkowników naraz = skok pamięci procesu (duże obiekty na LOH).
- **Propozycja poprawki:** (1) `summary` policzyć agregacją SQL z kolumn `TotalNet/TotalVat/TotalGross/Currency` (`Invoices` w statusie `Accepted`, `ReceivedInvoices`) - bez XML; (2) w `SalesAsync` projekcja tylko ostatniej wysyłki z numerem KSeF (`Select` z podzapytaniem `OrderByDescending(Id).First()`), `UpoXml` pobierać tylko dla paczki ZIP; (3) CSV sprzedaży budować z kolumn faktury tam, gdzie to możliwe.
- **Test, który to wykryje:** test integracyjny z licznikiem zapytań/interceptorem poleceń: `ExportSummary_DoesNotReadXmlColumns` - asercja, że SQL dla `/exports/summary` nie zawiera `"SentXml"`, `"UpoXml"`, `"Xml"`.

### PERF-6. Wyścig przy rejestracji wpłat: dwie równoległe wpłaty mijają kontrolę „pozostało do zapłaty” (nadpłata ponad kwotę należną)
- **Waga:** wysokie
- **Rodzaj:** błąd (współbieżność)
- **Pewność:** potwierdzone testem tymczasowym na kopii repozytorium (8 równoległych `POST /api/invoices/{id}/payments` na pełną kwotę 1897,50 zł → 8 × 200, suma wpłat 15 180,00 zł; test usunięty)
- **Miejsce:** `FakturowanieKSeF.Api/Endpoints/PaymentEndpoints.cs:29-42` (faktury wystawione), `:73-82` (otrzymane), `:148-160` (`Validate`)
- **Problem:** kontrola `request.Amount > remaining` opiera się na płatnościach odczytanych przed zapisem; dodanie `Payment` nie modyfikuje wiersza faktury, więc `Invoice.Version` (xmin) niczego nie wykrywa, a `ReceivedInvoice` nie ma tokenu wcale. Brak też ograniczenia w bazie.
- **Scenariusz:** faktura na 1000 zł; właściciel i pracownik (albo podwójne kliknięcie przy wolnym łączu) równocześnie rejestrują wpłatę 1000 zł → oba żądania widzą `remaining = 1000`, oba zapisują; rozliczenie pokazuje 2000 zł wpłat (nadpłata 1000), a faktura znika z należności. Usunięcie wymaga ręcznej korekty.
- **Propozycja poprawki:** w obu endpointach wymusić wersję wiersza nadrzędnego: dla faktury wystawionej oznaczyć encję jako zmienioną (`db.Entry(invoice).Property(i => i.UpdatedAt).IsModified = true` - UPDATE z warunkiem `xmin`) albo pobrać fakturę z `SELECT ... FOR UPDATE` w transakcji; dla `ReceivedInvoice` dodać `[Timestamp] uint Version` lub blokadę wiersza. `DbUpdateConcurrencyException` już zamienia się w 409.
- **Test, który to wykryje:** `Payments_ConcurrentPostsCannotOverpay` - dwa równoległe `POST /invoices/{id}/payments` na pełną kwotę (`Task.WhenAll`); oczekiwane: jeden 200, drugi 409, suma płatności ≤ kwota należna.

### PERF-2. Paczka ZIP i CSV budowane w całości w pamięci (kilka kopii każdego dokumentu)
- **Waga:** średnie
- **Rodzaj:** wydajność
- **Pewność:** potwierdzone (analiza kodu)
- **Miejsce:** `FakturowanieKSeF.Api/Services/AccountingExport.cs:143-174` (`Package`), `:258` (`CsvBuilder.ToBytes`), `FakturowanieKSeF.Api/Endpoints/ExportEndpoints.cs:50-60`
- **Problem:** eksport trzyma jednocześnie: listę encji z XML jako `string` (UTF-16), sparsowane `InvoiceDocumentDto`, tablice `byte[]` z `Encoding.GetBytes` dla każdego pliku, `MemoryStream` z archiwum oraz jego kopię z `stream.ToArray()`. Szczytowe zużycie pamięci to kilkukrotność rozmiaru danych okresu, na żądanie każdego użytkownika (w tym księgowej), bez limitu równoległości innego niż ogólny limiter API.
- **Scenariusz:** eksport roczny firmy z kilkoma tysiącami faktur → setki MB alokacji w jednym żądaniu; dwa-trzy równoległe eksporty na małym kontenerze (wdrożenie D48) kończą się `OutOfMemoryException`/zabiciem kontenera, co przerywa też usługi w tle (wysyłki KSeF).
- **Propozycja poprawki:** strumieniować archiwum wprost do odpowiedzi (`Results.Stream(async stream => ...)` z `ZipArchive` w trybie `Create` na `Response.Body`), dane czytać porcjami (`AsAsyncEnumerable()` z projekcją `Id, Number, SentXml, UpoXml`), CSV pisać `StreamWriter`-em do wpisu archiwum; rozważyć osobny limit równoległych eksportów (np. `ConcurrencyLimiter` 1-2 na firmę).
- **Test, który to wykryje:** test obciążeniowy/integracyjny `ExportPackage_StreamsWithoutBufferingWholeArchive` (np. 2000 faktur z atrapą XML ~50 KB, asercja na `GC.GetTotalAllocatedBytes`/szczyt pamięci albo na brak nagłówka `Content-Length` i poprawność archiwum).

### PERF-3. Dwa `Include` kolekcji w jednym zapytaniu (pozycje × wysyłki z XML) - iloczyn kartezjański z dużymi kolumnami
- **Waga:** średnie
- **Rodzaj:** wydajność
- **Pewność:** potwierdzone pomiarem (sekcja „Pomiar”: `/pdf` i `/xml` wykonują jedno zapytanie łączące `InvoiceItems` z `KsefSubmissions`); brak `AsSplitQuery()` i globalnego `UseQuerySplittingBehavior` w `Program.cs:21-22`
- **Miejsce:** `FakturowanieKSeF.Api/Endpoints/InvoiceEndpoints.cs:113-114` (`/xml`), `:153-154` (`/send`: Items × KsefSubmissions × Payments), `:228-229` (`/pdf`), `:345-346` (PUT)
- **Problem:** `Include(i => i.Items).Include(i => i.KsefSubmissions)` w jednym zapytaniu tworzy JOIN, w którym każdy wiersz pozycji jest powielony dla każdej wysyłki - a każdy wiersz niesie `SentXml`, `UpoXml` i `StatusDetails`. Dodatkowo pobierane są **wszystkie** wysyłki (także odrzucone), choć kod używa tylko ostatniej (`InvoiceFiles.Submitted`, `ToDto`), a PUT i `/send` nie potrzebują treści XML wcale.
- **Scenariusz:** faktura z 40 pozycjami, dwukrotnie odrzucona i przyjęta za trzecim razem (3 wysyłki po ~30 KB XML + UPO): pobranie PDF przesyła z bazy 40 × 3 = 120 wierszy, każdy z pełnym XML (kilka MB zamiast ~30 KB). W `/send` dochodzi mnożnik płatności. EF zgłasza dla takich zapytań ostrzeżenie `MultipleCollectionIncludeWarning`.
- **Propozycja poprawki:** dla `/xml` i `/pdf` pobierać ostatnią wysyłkę osobnym zapytaniem z projekcją (`SentXml`, `InvoiceHash`, `KsefNumber`) tylko gdy status to Pending/Accepted; w PUT i `/send` nie ładować `KsefSubmissions` (do DTO wystarczy projekcja ostatniej wysyłki bez XML) albo co najmniej dodać `AsSplitQuery()`.
- **Test, który to wykryje:** `InvoicePdf_QueryDoesNotMultiplyXmlByItems` - interceptor poleceń sprawdza, że zapytanie o fakturę nie łączy `InvoiceItems` z `KsefSubmissions` w jednym SELECT; alternatywnie włączyć `ConfigureWarnings(w => w.Throw(RelationalEventId.MultipleCollectionIncludeWarning))` w testach.

### PERF-4. Szczegóły faktury pobierają treść XML i UPO wszystkich wysyłek tylko po to, by pokazać status ostatniej
- **Waga:** średnie
- **Rodzaj:** wydajność
- **Pewność:** potwierdzone (analiza kodu: `Mapping.cs:107` używa wyłącznie ostatniej wysyłki, `Mapping.cs:122-125` z XML czyta tylko `UpoXml is not null`)
- **Miejsce:** `FakturowanieKSeF.Api/Endpoints/InvoiceEndpoints.cs:89-92`, `FakturowanieKSeF.Api/Infrastructure/Mapping.cs:107,122-125`; ten sam wzorzec: `Services/Email/EmailOutbox.cs` (załączniki) i `Services/AccountingExport.cs:47`
- **Problem:** `GET /invoices/{id}` (najczęściej otwierany ekran, odświeżany także podczas oczekiwania na KSeF) ładuje `Include(KsefSubmissions)` z kolumnami `SentXml` i `UpoXml` wszystkich wysyłek oraz pełne encje `Corrections` i `Proforma` (z kolumnami stron), choć DTO potrzebuje kilku pól skalarnych.
- **Scenariusz:** otwarcie faktury przyjętej przez KSeF = 6 zapytań (split query), z czego jedno przesyła dziesiątki KB XML + UPO, które serwer natychmiast odrzuca; ekran odpytuje endpoint cyklicznie, gdy faktura jest `Pending`.
- **Propozycja poprawki:** ostatnią wysyłkę pobierać projekcją pól `KsefSubmissionDto` (`HasUpo = s.UpoXml != null`), korekty i proformę projekcją do `CorrectionSummaryDto`/`LinkedDocumentDto` (sumę korekt policzyć z tej projekcji).
- **Test, który to wykryje:** `InvoiceDetails_DoesNotSelectXmlColumns` - interceptor poleceń: SQL żądania `GET /api/invoices/{id}` nie zawiera `"SentXml"` ani `"UpoXml"` poza wyrażeniem `IS NOT NULL`.

### PERF-5. Pulpit i podsumowanie płatności wczytują do pamięci wszystkie nierozliczone dokumenty i sumują je w aplikacji; to samo zapytanie wykonywane dwa razy
- **Waga:** średnie
- **Rodzaj:** wydajność
- **Pewność:** potwierdzone (analiza kodu; `Home.razor:228-230` wywołuje równolegle `/dashboard` i `/payments/summary`)
- **Miejsce:** `FakturowanieKSeF.Api/Endpoints/PaymentEndpoints.cs:111-139`, `FakturowanieKSeF.Api/Endpoints/DashboardEndpoints.cs:48-72`
- **Problem:** `/payments/summary` materializuje listę wszystkich nieopłaconych faktur wystawionych i **wszystkich nieopłaconych faktur otrzymanych** (bez ograniczenia okresu), żeby policzyć 8 liczb; `/dashboard` powtarza zapytanie o należności (z tymi samymi podzapytaniami `Sum` po `Payments` i `Corrections`) i grupuje je w aplikacji. Faktury otrzymane, których użytkownik nie oznacza jako zapłacone (typowe - funkcja jest opcjonalna), pozostają „otwarte” na zawsze, więc zbiór rośnie liniowo z historią.
- **Scenariusz:** firma po 2 latach ma 5000 faktur otrzymanych bez zarejestrowanych płatności: każde wejście na pulpit wykonuje dla każdej z nich podzapytanie `SUM(Payments)` i przesyła 5000 wierszy do aplikacji; analogicznie należności liczone są dwukrotnie w dwóch równoległych żądaniach.
- **Propozycja poprawki:** agregować w SQL (`GroupBy(x => x.Due < today)` → `Sum`, `Count`; kubełki wiekowania przez `GroupBy` po wyrażeniu `CASE`), a pulpit oprzeć na jednym endpoincie lub wspólnym zapytaniu; rozważyć utrwalaną kolumnę „zapłacono” albo indeks częściowy po dokumentach nierozliczonych, jeśli zbiór będzie duży.
- **Test, który to wykryje:** `PaymentsSummary_AggregatesInDatabase` - interceptor: zapytania endpointu zwracają stałą liczbę wierszy niezależnie od liczby faktur (np. seed 500 faktur → ≤ 4 wiersze wyniku).

### PERF-7. Zamiana proformy na fakturę VAT bez ochrony przed równoległym wywołaniem - dwie faktury z jednej proformy
- **Waga:** średnie
- **Rodzaj:** błąd (współbieżność)
- **Pewność:** potwierdzone testem tymczasowym na kopii repozytorium (8 równoległych `POST /api/invoices/{id}/convert` → 4 × 201 i 4 faktury VAT z jednej proformy; test usunięty); indeks `IX_Invoices_ProformaId` nie jest unikalny (snapshot `:528`)
- **Miejsce:** `FakturowanieKSeF.Api/Endpoints/InvoiceEndpoints.cs:256-257` i `:265-280`; `FakturowanieKSeF.Api/Data/AppDbContext.cs:171`
- **Problem:** „czy z tej proformy wystawiono już fakturę” to odczyt przed zapisem; ponawianie przy konflikcie numeru (`attempt < 3`) dodatkowo sprawia, że drugie żądanie po kolizji numeru dostaje kolejny numer i zapisuje się poprawnie.
- **Scenariusz:** podwójne kliknięcie „Wystaw fakturę” na proformie (lub dwie osoby w firmie) → dwa szkice FV/n i FV/n+1 z tym samym `ProformaId`; szczegóły proformy pokazują tylko jeden (`FirstOrDefault`, `InvoiceEndpoints.cs:97-98`), drugi może zostać wysłany do KSeF jako duplikat sprzedaży.
- **Propozycja poprawki:** unikalny indeks częściowy `(ProformaId) WHERE "ProformaId" IS NOT NULL AND "IsDeleted" = false` + komunikat w `ApiErrors.UniqueViolationMessages` (409 „Na podstawie tej proformy wystawiono już fakturę”).
- **Test, który to wykryje:** `ConvertProforma_ConcurrentCallsCreateSingleInvoice` - dwa równoległe `POST /invoices/{id}/convert`; oczekiwane 201 + 409 i dokładnie jedna faktura z danym `ProformaId`.

### PERF-8. Faktury cykliczne: szkic i przesunięcie harmonogramu zapisywane w dwóch osobnych `SaveChanges` - przerwanie między nimi tworzy duplikat szkicu
- **Waga:** średnie
- **Rodzaj:** błąd (spójność / transakcje)
- **Pewność:** potwierdzone analizą kodu
- **Miejsce:** `FakturowanieKSeF.Api/Services/RecurringInvoices.cs:58` → `:109` (zapis faktury) i `:69-76` (zapis harmonogramu); brak blokady przebiegu - `:42-46`
- **Problem:** `CreateDraftAsync` utrwala fakturę, a dopiero potem osobnym zapisem zwiększane są `IssuedCount`/`NextIssueDate`. Oba zapisy używają tokenu anulowania usługi, więc zatrzymanie aplikacji (wdrożenie nowej wersji), błąd połączenia z bazą albo wyjątek przy drugim zapisie zostawia harmonogram z nieprzesuniętą datą. To samo przy dwóch instancjach aplikacji (każda odczyta te same „należne” harmonogramy).
- **Scenariusz:** restart kontenera w chwili, gdy usługa przetwarza harmonogram: szkic FV/7/11/2026 zapisany, `NextIssueDate` nadal ≤ dziś → po starcie powstaje drugi szkic FV/8/11/2026 dla tego samego okresu; użytkownik dostaje powiadomienie o jednym, a na liście ma dwa.
- **Propozycja poprawki:** jedna jednostka pracy: zmienić pola harmonogramu przed `SaveChangesAsync` w `CreateDraftAsync` (z nawigacją `LastInvoice` zamiast samego `LastInvoiceId`) albo objąć oba zapisy transakcją; dodać `[Timestamp]` na `RecurringInvoice`, żeby druga instancja dostała konflikt zamiast duplikatu.
- **Test, który to wykryje:** `RecurringInvoices_FailureAfterDraftDoesNotDuplicate` - interceptor rzucający wyjątek przy drugim `SaveChanges`; po ponownym `RunAsync` istnieje dokładnie jedna faktura dla harmonogramu i okresu.

### PERF-9. Listy kontrahentów i katalogu obcinane po cichu do 500/1000 rekordów
- **Waga:** średnie
- **Rodzaj:** błąd / wydajność
- **Pewność:** potwierdzone analizą kodu (`Customers.razor:109` i `InvoiceEdit.razor:438` pobierają całą listę bez stronicowania)
- **Miejsce:** `FakturowanieKSeF.Api/Endpoints/CustomerEndpoints.cs:31`, `FakturowanieKSeF.Api/Endpoints/ProductEndpoints.cs:28`
- **Problem:** `Take(500)` / `Take(1000)` bez informacji o łącznej liczbie i bez stronicowania. Powyżej limitu część rekordów jest niewidoczna (sortowanie po nazwie - znikają końcowe litery alfabetu), a formularz faktury nie pozwala wybrać takiego kontrahenta z listy. Jednocześnie do limitu każdorazowe otwarcie formularza faktury przesyła pełne encje wszystkich kontrahentów.
- **Scenariusz:** firma z 650 kontrahentami: „Żabka sp. z o.o.” nie pojawia się na liście kontrahentów (licznik pokazuje 500) ani w polu wyboru nabywcy przy wystawianiu faktury; wyszukiwarka na liście go znajduje, ale formularz faktury nie ma wyszukiwania po stronie serwera.
- **Propozycja poprawki:** stronicowanie jak w fakturach (`PagedResult`, `total`) dla list; w formularzu faktury wyszukiwanie po stronie serwera (`q` + `Take(20)`) zamiast pobierania całej książki adresowej; co najmniej zwracać `total`, żeby UI mógł ostrzec o obcięciu.
- **Test, który to wykryje:** `Customers_ListBeyondLimitIsPagedNotTruncated` - seed 501 kontrahentów, asercja że `total == 501` i ostatni jest osiągalny przez `page=...`.

### PERF-10. Indeksy niedopasowane do zapytań: lista i eksport faktur otrzymanych, kolejka wysyłek KSeF, indeks zdublowany
- **Waga:** średnie
- **Rodzaj:** wydajność
- **Pewność:** do weryfikacji - niedopasowanie wynika wprost z porównania zapytań ze snapshotem; brakuje `EXPLAIN ANALYZE` na dużym zbiorze (dane demonstracyjne to kilkanaście wierszy na firmę, plan nie jest miarodajny)
- **Miejsce:** `FakturowanieKSeF.Api/Data/AppDbContext.cs:138-141, 194, 208-209` (snapshot `:524-536`, `:658-662`, `:837-839`); zapytania: `Endpoints/ReceivedInvoiceEndpoints.cs:53-55`, `Services/AccountingExport.cs:63-66`, `Services/KsefSubmissionProcessor.cs:40-47`, `Services/IssuedInvoiceImport.cs:119-122`
- **Problem:** (1) `ReceivedInvoices` ma indeks `(CompanyId, IssueDate)`, a lista sortuje po `KsefAcceptedAt DESC, Id DESC` i eksport filtruje po `KsefAcceptedAt` - każda strona listy sortuje wszystkie faktury otrzymane firmy. (2) Kolejka usługi wysyłek to `CompletedAt IS NULL OR (KsefNumber IS NOT NULL AND UpoXml IS NULL AND SessionReferenceNumber IS NOT NULL AND SentAt > …) ORDER BY Id LIMIT 50` - druga gałąź `OR` nie ma indeksu, więc w stanie spoczynku (brak pracy, czyli prawie zawsze) zapytanie wykonywane co 15 s przegląda całą tabelę `KsefSubmissions` wszystkich firm; indeks `IX_KsefSubmissions_CompletedAt` obejmuje wszystkie wiersze, choć potrzebne są tylko te z `NULL`. (3) `IX_Invoices_CompanyId_KsefNumber` dubluje unikalny `IX_Invoices_KsefNumber` (dopóki ten jest globalny - zob. ISO-1/KSEF-10; po ich poprawce to właśnie indeks firmowy powinien zostać, jako unikalny). (4) `KsefSubmissions.InvoiceHash` bez indeksu, a import sprawdza po nim każdą pozycję metadanych (`KnownAsync`).
- **Scenariusz:** 200 firm × 2000 wysyłek = 400 tys. wierszy `KsefSubmissions`: bezczynna usługa wykonuje pełny przegląd tabeli ~5800 razy na dobę; firma z 10 tys. faktur otrzymanych sortuje je w całości przy każdym przejściu między stronami listy.
- **Propozycja poprawki:** indeks `(CompanyId, KsefAcceptedAt DESC, Id DESC)` na `ReceivedInvoices`; dwa indeksy częściowe na `KsefSubmissions`: `(Id) WHERE "CompletedAt" IS NULL` oraz `(SentAt) WHERE "KsefNumber" IS NOT NULL AND "UpoXml" IS NULL AND "SessionReferenceNumber" IS NOT NULL` i rozbicie zapytania na dwa (wysyłki w toku / brakujące UPO), zamiast pełnego indeksu po `CompletedAt`; usunięcie zdublowanego indeksu po rozstrzygnięciu ISO-1.
- **Test, który to wykryje:** test modelu `DataModelTests`: asercja istnienia indeksów (`db.Model.FindEntityType(...).GetIndexes()`); jednorazowo `EXPLAIN (ANALYZE, BUFFERS)` trzech zapytań na bazie z ~100 tys. wierszy (skrypt w `tools/`).

### PERF-11. Wyszukiwanie `q`: `ILIKE '%…%'` po czterech kolumnach bez limitu długości i bez neutralizacji znaków `%`/`_`
- **Waga:** niskie
- **Rodzaj:** wydajność / błąd w przypadku brzegowym
- **Pewność:** potwierdzone analizą kodu
- **Miejsce:** `FakturowanieKSeF.Api/Endpoints/InvoiceEndpoints.cs:56-63`, `ReceivedInvoiceEndpoints.cs:43-48`, `CustomerEndpoints.cs:25-29`, `ProductEndpoints.cs:25-26`
- **Problem:** wzorzec z wiodącym `%` wyklucza użycie indeksu B-tree - koszt jest liniowy względem liczby dokumentów firmy (zawężenie po `CompanyId` działa), a `CountAsync` powtarza go drugi raz. Znaki `%` i `_` wpisane przez użytkownika działają jak symbole wieloznaczne, a długość `q` nie jest ograniczona (wzorzec typu `%a%a%a%…` wydłuża dopasowanie każdego wiersza).
- **Scenariusz:** wyszukanie „FV_1” zwraca także „FV/1…”, „FVA1…”; wyszukanie „100%” zwraca wszystko z „100”. Przy kilkudziesięciu tysiącach faktur jednej firmy każde naciśnięcie klawisza w polu wyszukiwania to dwa pełne przeglądy jej faktur.
- **Propozycja poprawki:** ograniczyć długość `q` (np. 100 znaków), neutralizować `\`, `%`, `_` (trzeci argument `ILike` z `ESCAPE`); indeksy trigramowe (`pg_trgm`, GIN) dopiero gdy pomiar pokaże potrzebę - przy obecnej skali zawężenie po firmie wystarcza.
- **Test, który to wykryje:** `InvoiceSearch_TreatsWildcardsLiterally` - faktury „FV/1/11/2026” i „FVX1”, zapytanie `q=FV_1` zwraca 0 wyników; `q` dłuższe niż limit → 400.

### PERF-12. Stan faktury korygowanej: wczytywane wszystkie korekty z pozycjami i dwukrotnie faktura pierwotna
- **Waga:** niskie
- **Rodzaj:** wydajność
- **Pewność:** potwierdzone analizą kodu
- **Miejsce:** `FakturowanieKSeF.Api/Endpoints/InvoiceEndpoints.cs:574-587` (`CurrentStateAsync`), `:593-597` (`CheckCorrectionStateAsync`), `:430-431`
- **Problem:** do ustalenia „stanu po ostatniej korekcie” pobierane są pełne encje wszystkich korekt przekazanych do KSeF wraz z pozycjami (przed i po), choć potrzebne są pozycje „po” jednej, ostatniej przyjętej korekty i flaga „czy jakaś jest w toku”. Przy wysyłce korekty faktura pierwotna z pozycjami jest czytana ponownie.
- **Scenariusz:** faktura z 30 pozycjami i 5 korektami: zapis szóstej korekty wczytuje 5 × 60 pozycji + 30 pozycji oryginału, wykorzystuje 30.
- **Propozycja poprawki:** dwa wąskie zapytania: `AnyAsync(Status == Pending)` oraz pozycje `!BeforeCorrection` korekty o najwyższym `Id` w statusie `Accepted` (`OrderByDescending(Id).Take(1)`).
- **Test, który to wykryje:** istniejące `CorrectionTests` jako testy regresji + licznik wierszy/zapytań dla zapisu n-tej korekty.

### PERF-13. Numeracja automatyczna: trzy próby nie wystarczają przy kilku równoległych zapisach - 409 „Faktura o tym numerze już istnieje” mimo pustego pola numeru
- **Waga:** niskie
- **Rodzaj:** błąd (współbieżność)
- **Pewność:** potwierdzone testem tymczasowym (8 równoległych `POST /invoices/{id}/convert`: 4 × 201, 4 × 409 po wyczerpaniu prób; test usunięty)
- **Miejsce:** `FakturowanieKSeF.Api/Endpoints/InvoiceEndpoints.cs:265-280, 318-336`, `Services/RecurringInvoices.cs:100-117`, `Services/InvoiceNumbering.cs:27-43`
- **Problem:** kolejny numer to „maksimum + 1” odczytane bez blokady; kolizję wykrywa indeks unikalny, a kod ponawia najwyżej 2 razy. Przy więcej niż 3 równoczesnych zapisach w jednej firmie (import, kilku pracowników, usługa faktur cyklicznych równolegle z użytkownikiem) część żądań kończy się 409 z komunikatem o duplikacie numeru, którego użytkownik nie podawał; w `RecurringInvoices` taki wyjątek przerywa cały przebieg (zob. KSEF-14). Dodatkowo każde wyznaczenie numeru pobiera do aplikacji wszystkie numery miesiąca.
- **Scenariusz:** jak w teście: 8 równoległych żądań utworzenia faktury z numeracją automatyczną → połowa odrzucona.
- **Propozycja poprawki:** serializować nadawanie numeru w obrębie firmy: `pg_advisory_xact_lock(companyId)` w transakcji zapisu albo tabela liczników `(CompanyId, Kind, Year, Month, Last)` z `UPDATE … RETURNING`; wtedy pętla ponowień staje się zbędna.
- **Test, który to wykryje:** `CreateInvoice_ParallelAutoNumbering_AllSucceedWithDistinctNumbers` - 10 równoległych `POST /api/invoices` bez numeru → 10 × 201 i 10 różnych numerów.

### PERF-14. Kolejka e-mail: limit dobowy sprawdzany przed zapisem, brak „zajęcia” wiadomości przez instancję, historia bez retencji
- **Waga:** niskie
- **Rodzaj:** błąd (współbieżność) / wydajność
- **Pewność:** potwierdzone analizą kodu
- **Miejsce:** `FakturowanieKSeF.Api/Services/Email/EmailOutbox.cs:59-63` (limit), `:79-106` (pobranie i wysyłka partii)
- **Problem:** (1) `CountAsync` + późniejszy `SaveChanges` w innym miejscu - równoległe żądania przekraczają `DailyLimitPerCompany`. (2) Partia jest pobierana zwykłym `SELECT` bez `FOR UPDATE SKIP LOCKED` ani znacznika zajęcia: dwie instancje aplikacji (lub nakładające się przebiegi po restarcie) wyślą tę samą wiadomość dwa razy - analogicznie do KSEF-2 dla wysyłek. (3) `EmailMessages` (z pełną treścią `Body`) i `KsefSubmissions.StatusDetails` rosną bez końca; lista historii per faktura nie ma limitu.
- **Scenariusz:** wdrożenie z dwiema replikami: kontrahent dostaje fakturę e-mailem dwukrotnie.
- **Propozycja poprawki:** pobieranie partii w transakcji z `FOR UPDATE SKIP LOCKED` (surowe SQL) lub atomowe `ExecuteUpdate` ustawiające `NextAttemptAt` „w przyszłość” przed wysyłką; limit dobowy egzekwować w tej samej transakcji co zapis; polityka retencji treści wysłanych wiadomości (np. czyszczenie `Body` po 90 dniach).
- **Test, który to wykryje:** `EmailOutbox_TwoProcessorsSendMessageOnce` - dwa równoległe `ProcessAsync` w osobnych zakresach DI, atrapa nadawcy z opóźnieniem; oczekiwana dokładnie jedna wysyłka.

### PERF-15. Drobne koszty stałe: `DbContext` bez puli, firma śledzona i czytana 1-3 zapytaniami w każdym żądaniu, pełna encja faktury tam, gdzie wystarczy kilka pól
- **Waga:** niskie
- **Rodzaj:** wydajność
- **Pewność:** potwierdzone pomiarem (sekcja „Pomiar”: każde żądanie zaczyna się od 1-2 zapytań `CurrentCompany`) i analizą kodu
- **Miejsce:** `FakturowanieKSeF.Api/Program.cs:21-22`; `Infrastructure/CurrentCompany.cs:32-67`; `Endpoints/EmailEndpoints.cs:22`, `NotificationEndpoints.cs:50`, `RecurringInvoiceEndpoints.cs:22-25, 45`; `Endpoints/InvoiceEndpoints.cs:68-78`
- **Problem:** (1) `AddDbContext` zamiast `AddDbContextPool` - nowy kontekst na każde żądanie i każdy przebieg usług w tle. (2) `CurrentCompany.FindAsync` zawsze ładuje **śledzoną** pełną encję firmy (z `KsefTokenEncrypted`), a przy wybranej firmie członka wykonuje dwa zapytania; filtr ról i endpoint współdzielą wynik (dobrze), ale odczyty nie potrzebują śledzenia. (3) Endpointy pomocnicze ładują całą encję faktury (ze stronami i adresami), by użyć 2-3 pól; lista harmonogramów robi `Include(TemplateInvoice)` dla 4 pól; lista faktur projektuje całą encję (`Invoice = i`).
- **Scenariusz:** brak błędnego wyniku; stały narzut alokacji i transferu na każde żądanie (w pomiarze `GET /api/invoices/{id}` alokuje ~7 MB przy 6 zapytaniach).
- **Propozycja poprawki:** `AddDbContextPool` (kontekst nie ma stanu per żądanie; do sprawdzenia testami, że ustawienia `CascadeDeleteTiming`/`DeleteOrphansTiming` z konstruktora zachowują się po zwrocie do puli); w `CurrentCompany` jedno zapytanie łączące firmę z członkostwem; projekcje `Select` w miejscach wskazanych wyżej.
- **Test, który to wykryje:** test licznika zapytań (interceptor) z progami na żądanie, np. `GET /api/invoices` ≤ 3 zapytania, `GET /api/invoices/{id}` ≤ 5.

### PERF-16. Uproszczenia: powtórzony kod endpointów, wyrażenie „kwota należna”, bliźniacze klasy usług w tle; propozycja podziału `InvoiceEndpoints.cs`
- **Waga:** niskie
- **Rodzaj:** uproszczenie
- **Pewność:** potwierdzone analizą kodu (`grep`: 57 wystąpień tego samego wiersza)
- **Miejsce:** wszystkie `FakturowanieKSeF.Api/Endpoints/*.cs` (57 × `if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();`); wyrażenie należności: `InvoiceEndpoints.cs:46-51, 75-76`, `PaymentEndpoints.cs:116-117`, `DashboardEndpoints.cs:54-56`, `Mapping.cs:49`; `InvoiceEndpoints.cs:126-142` i `:175-189` (generowanie + walidacja XSD); `KsefSubmissionWorker.cs:4-17` i `Email/EmailOutbox.cs:15-28` (identyczne klasy sygnału); cztery pętle `BackgroundService`; `MemberEndpoints.cs:149-153, 185-188` (ciasteczko firmy); `InvoiceEndpoints.cs:388-396` (komentarz `<summary>` metody `ApplyAsync` został nad `RecordPaymentFromInvoice`); `InvoiceNumbering.cs:24-25` (przeciążenie nieużywane w kodzie aplikacji)
- **Problem:** (1) sprawdzenie firmy powtórzone w każdym endpoincie - łatwo je pominąć w nowym. (2) Reguła „brutto + korekty przekazane do KSeF” zapisana 6 razy w LINQ i raz w C# - zmiana reguły (np. statusy) wymaga 7 zgodnych poprawek, a rozjazd oznacza różne kwoty na liście, pulpicie i w szczegółach. (3) `RecurringInvoices`, `DemoDataSeeder` i konwersja proformy wywołują statyczne `InvoiceEndpoints.ApplyAsync` - warstwa usług zależy od klasy endpointów. (4) `InvoiceEndpoints.cs` (616 wierszy) łączy mapowanie tras, logikę zapisu faktury, logikę korekt i płatność z faktury.
- **Scenariusz:** nie dotyczy (utrzymanie).
- **Propozycja poprawki:** filtr grupy `RequireCompany()` wstawiający firmę do `HttpContext.Items`/parametru; jedno `Expression<Func<Invoice, decimal>> TotalDue` używane we wszystkich zapytaniach; wydzielenie `InvoiceWriter` (obecne `ApplyAsync`, `CurrentStateAsync`, `CheckCorrectionStateAsync`, `RecordPaymentFromInvoice`) do `Services/` i pozostawienie w `InvoiceEndpoints.cs` samych tras (opcjonalnie podział na `InvoiceEndpoints` - CRUD i `InvoiceDocumentEndpoints` - xml/pdf/upo/send); jedna generyczna klasa sygnału i wspólna pętla usług w tle.
- **Test, który to wykryje:** istniejący zestaw testów integracyjnych jako regresja (refaktoryzacja bez zmiany zachowania); dodatkowo test spójności: kwota należna z listy = ze szczegółów = z podsumowania dla faktury z korektą.

### PERF-17. Produkcyjnie każde polecenie SQL trafia do logu (poziom `Information`), także z bezczynnych usług w tle
- **Waga:** niskie
- **Rodzaj:** wydajność (logowanie)
- **Pewność:** potwierdzone analizą konfiguracji (`appsettings.Development.json:6` wycisza kategorię, `appsettings.json` i pliki `deploy/` - nie)
- **Miejsce:** `FakturowanieKSeF.Api/appsettings.json:5-10`; źródła ruchu: `Services/KsefSubmissionWorker.cs:37-47`, `Services/Email/EmailOutbox.cs:147-154`
- **Problem:** `Logging:LogLevel:Default = Information` obejmuje kategorię `Microsoft.EntityFrameworkCore.Database.Command`, która na tym poziomie zapisuje pełny tekst każdego polecenia SQL. Wyciszenie (`Warning`) jest tylko w konfiguracji deweloperskiej - czyli odwrotnie niż potrzeba.
- **Scenariusz:** kontener produkcyjny bez żadnego użytkownika: usługa wysyłek (co 15 s w spoczynku) i usługa e-mail (co 15 s) dają ~11,5 tys. wpisów z kilkulinijkowym SQL na dobę; każde żądanie użytkownika dokłada 2-6 wpisów (np. `INSERT` faktury z 44 parametrami). Logi kontenera rosną i zaciemniają ostrzeżenia.
- **Propozycja poprawki:** w `appsettings.json` dodać `"Microsoft.EntityFrameworkCore.Database.Command": "Warning"` (w razie diagnozy włączać zmienną środowiskową `Logging__LogLevel__...`).
- **Test, który to wykryje:** `HostingTests`: w środowisku `Production` `ILoggerFactory.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command").IsEnabled(LogLevel.Information)` zwraca `false`.

## Sprawdzone bez uwag

- **Zamiana wyjątków bazy na 409:** `ApiErrors.ExceptionHandler` obsługuje `DbUpdateConcurrencyException` i każde naruszenie unikalności (z komunikatami dla numeru faktury, NIP kontrahenta, firmy właściciela, nazwy w katalogu); podwójne przyjęcie zaproszenia i podwójne założenie firmy kończą się 409 dzięki indeksom `(CompanyId, UserId)` i `OwnerId`.
- **Wysyłka do KSeF (`POST /invoices/{id}/send`):** XML, wpis `KsefSubmissions`, status `Pending` i płatność z faktury w jednym `SaveChanges`; podwójne kliknięcie chroni `Invoice.Version` (xmin) z wersją z żądania. Edycja faktury (PUT) również wymaga wersji.
- **Listy faktur i faktur otrzymanych:** `AsNoTracking`, filtrowanie i sortowanie w SQL, stabilne sortowanie (z `Id`), `pageSize` ograniczony do 100, sumy wpłat i korekt jako podzapytania (bez N+1); lista otrzymanych nie pobiera `Xml`.
- **Pulpit - sprzedaż, koszty, najwięksi kontrahenci:** agregacje `GROUP BY` w bazie z użyciem `(CompanyId, IssueDate)`.
- **Usługi w tle:** każda tworzy zakres DI (i `DbContext`) na przebieg, nie na czas życia usługi (zastrzeżenie o jednym kontekście dla wszystkich firm - KSEF-19).
- **`HttpClient`:** NBP i wykaz VAT przez `AddHttpClient` z limitem czasu 15 s; KSeF przez `AddKsefTestClient` (obszar 03).
- **`IMemoryCache`:** wpisy z czasem życia 12 h, klucze ograniczone dziennymi limitami zapytań (`VatRegistryLimiter` czyści liczniki przy zmianie dnia) i liczbą walut × dat; brak limitu rozmiaru nie jest tu ryzykiem.
- **`InvoicePdfRenderer`:** singleton bez stanu, licencja i ustawienia QuestPDF w konstruktorze statycznym; PDF jednej faktury w pamięci (63 KB) - adekwatne.
- **Wyrażenia regularne:** `AccountingExport.CsvBuilder` używa `GeneratedRegex`.
- **Typy kolumn:** kwoty `numeric(18,2)`, kurs `numeric(18,6)`, enumy jako tekst z ograniczeniem długości, `CK_Payments_OneInvoice`; indeksy unikalne częściowe uwzględniają miękkie usuwanie.
- **Miękkie usuwanie i kaskady:** `CascadeTiming.OnSaveChanges` + `ApplyAuditFields`; wymiana pozycji faktury przy edycji nie narusza `(InvoiceId, Position)` (pokryte istniejącymi testami edycji).
- **Kompresja odpowiedzi i limity Kestrel:** kompresja po stronie Caddy (`deploy/Caddyfile: encode zstd gzip`), domyślne limity Kestrel wystarczające (brak endpointów przyjmujących pliki).

## Poza obszarem

- `InvoiceEndpoints.cs:304` - `/invoices/next-number` używa `DateTime.Today` (czas serwera) zamiast `PolishDate.Today(time)`; przy serwerze w UTC propozycja numeru w nocy z ostatniego dnia miesiąca może dotyczyć poprzedniego miesiąca (obszar 04).

## Pytania do autora

1. Czy wdrożenie zakłada zawsze jedną instancję aplikacji (D48)? Jeśli tak, PERF-14 pkt 2 i wariant „dwie instancje” w PERF-8 są teoretyczne; wyścigi PERF-6, PERF-7 i PERF-13 występują także przy jednej instancji.
2. Czy nadpłata (suma wpłat > kwota należna) ma być w ogóle możliwa (np. po korekcie in minus)? Od tego zależy, czy PERF-6 zabezpieczać blokadą wiersza, czy także ograniczeniem w bazie.
3. Jaki jest oczekiwany rząd wielkości danych jednej firmy (faktur/rok)? Przy < 1000 PERF-1/PERF-2 są mało dotkliwe; limit 366 dni dopuszcza jednak eksport dowolnie dużej firmy.
