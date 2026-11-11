# Raport: Obszar 05 - backend: wydajność, baza danych, współbieżność
Status: W TOKU
Ostatnia aktualizacja: 2026-11-11 (start)

## Postęp
- [x] AppDbContext.cs (indeksy do porównania ze snapshotem - w toku)
- [x] Modele
- [x] Program.cs (przeczytany; uwagi do spisania: brak puli DbContext, IMemoryCache bez limitu - do weryfikacji)
- [x] Endpoints: InvoiceEndpoints.cs (do spisania: wyścig convert proformy, CurrentStateAsync)
- [x] Endpoints: wszystkie pliki przeczytane
- [x] Services: usługi w tle i zapytania EF (bez powtarzania KSEF-2, KSEF-4, KSEF-16, KSEF-19)
- [ ] InvoicePdfRenderer.cs, AccountingExport.cs (pamięć)
- [x] Współbieżność (PERF-6, PERF-7, PERF-8; numeracja i unikalność - bez uwag)
- [ ] Pomiar zapytań EF (jeśli możliwy)
- [ ] Uproszczenia
Następny krok: spisać uwagi o indeksach (PERF-10+), CurrentStateAsync, kolejka e-mail, pula DbContext; pomiar zapytań na kopii repo w scratchpad (port 5199, baza ksef_review05); uproszczenia.

## Znaleziska

### PERF-1. Podsumowanie eksportu i pliki CSV wczytują i parsują wszystkie XML-e okresu (także UPO), choć potrzebują kilku liczb
- **Waga:** wysokie
- **Rodzaj:** wydajność
- **Pewność:** potwierdzone (analiza kodu: `ExportEndpoints.cs:16-25` → `AccountingExport.SalesAsync`/`PurchasesAsync`)
- **Miejsce:** `FakturowanieKSeF.Api/Services/AccountingExport.cs:45-69`, `FakturowanieKSeF.Api/Endpoints/ExportEndpoints.cs:16-26` (`/exports/summary`), `:28-48` (`sales.csv`, `purchases.csv`)
- **Problem:** `SalesAsync` pobiera pełne encje faktur z `Include(KsefSubmissions)` - czyli **wszystkie** wysyłki każdej faktury (także odrzucone) z kolumnami `SentXml`, `UpoXml`, `StatusDetails` - a `PurchasesAsync` pełne `ReceivedInvoices` z `Xml`; każdy XML jest następnie parsowany (`Fa3InvoiceReader.Read`). Dzieje się to również dla `GET /exports/summary`, które zwraca wyłącznie liczbę faktur i sumy netto/VAT/brutto, oraz dla CSV, które nie potrzebują UPO ani odrzuconych wysyłek.
- **Scenariusz:** firma z 3000 faktur sprzedaży i 3000 zakupu w roku otwiera ekran eksportu z zakresem rocznym (limit 366 dni): serwer wczytuje ~6000 dokumentów XML + 3000 UPO (rzędu 100+ MB tekstu w UTF-16), parsuje je i zwraca 12 liczb. Każda zmiana zakresu dat powtarza operację; kilku użytkowników naraz = skok pamięci procesu (duże obiekty na LOH).
- **Propozycja poprawki:** (1) `summary` policzyć agregacją SQL z kolumn `TotalNet/TotalVat/TotalGross/Currency` (`Invoices` w statusie `Accepted`, `ReceivedInvoices`) - bez XML; (2) w `SalesAsync` projekcja tylko ostatniej wysyłki z numerem KSeF (`Select` z podzapytaniem `OrderByDescending(Id).First()`), `UpoXml` pobierać tylko dla paczki ZIP; (3) CSV sprzedaży budować z kolumn faktury tam, gdzie to możliwe.
- **Test, który to wykryje:** test integracyjny z licznikiem zapytań/interceptorem poleceń: `ExportSummary_DoesNotReadXmlColumns` - asercja, że SQL dla `/exports/summary` nie zawiera `"SentXml"`, `"UpoXml"`, `"Xml"`.

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
- **Pewność:** potwierdzone (analiza kodu; brak `AsSplitQuery()` i brak globalnego `UseQuerySplittingBehavior` w `Program.cs:21-22`)
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

### PERF-6. Wyścig przy rejestracji wpłat: dwie równoległe wpłaty mijają kontrolę „pozostało do zapłaty” (nadpłata ponad kwotę należną)
- **Waga:** średnie
- **Rodzaj:** błąd (współbieżność)
- **Pewność:** potwierdzone analizą kodu (brak blokady i brak tokenu współbieżności na ścieżce); test równoległy - patrz sekcja „Pomiar”, jeśli wykonany
- **Miejsce:** `FakturowanieKSeF.Api/Endpoints/PaymentEndpoints.cs:29-42` (faktury wystawione), `:73-82` (otrzymane), `:148-160` (`Validate`)
- **Problem:** kontrola `request.Amount > remaining` opiera się na płatnościach odczytanych przed zapisem; dodanie `Payment` nie modyfikuje wiersza faktury, więc `Invoice.Version` (xmin) niczego nie wykrywa, a `ReceivedInvoice` nie ma tokenu wcale. Brak też ograniczenia w bazie.
- **Scenariusz:** faktura na 1000 zł; właściciel i pracownik (albo podwójne kliknięcie przy wolnym łączu) równocześnie rejestrują wpłatę 1000 zł → oba żądania widzą `remaining = 1000`, oba zapisują; rozliczenie pokazuje 2000 zł wpłat (nadpłata 1000), a faktura znika z należności. Usunięcie wymaga ręcznej korekty.
- **Propozycja poprawki:** w obu endpointach wymusić wersję wiersza nadrzędnego: dla faktury wystawionej oznaczyć encję jako zmienioną (`db.Entry(invoice).Property(i => i.UpdatedAt).IsModified = true` - UPDATE z warunkiem `xmin`) albo pobrać fakturę z `SELECT ... FOR UPDATE` w transakcji; dla `ReceivedInvoice` dodać `[Timestamp] uint Version` lub blokadę wiersza. `DbUpdateConcurrencyException` już zamienia się w 409.
- **Test, który to wykryje:** `Payments_ConcurrentPostsCannotOverpay` - dwa równoległe `POST /invoices/{id}/payments` na pełną kwotę (`Task.WhenAll`); oczekiwane: jeden 200, drugi 409, suma płatności ≤ kwota należna.

### PERF-7. Zamiana proformy na fakturę VAT bez ochrony przed równoległym wywołaniem - dwie faktury z jednej proformy
- **Waga:** średnie
- **Rodzaj:** błąd (współbieżność)
- **Pewność:** potwierdzone analizą kodu (sprawdzenie `ProformaId` zwykłym zapytaniem, indeks `IX_Invoices_ProformaId` nie jest unikalny - snapshot `:528`)
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
