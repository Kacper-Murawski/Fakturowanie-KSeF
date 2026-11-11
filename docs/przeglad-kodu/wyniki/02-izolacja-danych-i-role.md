# Raport: Obszar 02 - izolacja danych między firmami i autoryzacja ról
Status: W TOKU
Ostatnia aktualizacja: 2026-11-11 (start)

## Postęp
- [x] `Infrastructure/CurrentCompany.cs` + pytania kontrolne o wybór firmy (przeczytane; uwagi robocze niżej)
- [x] `Data/AppDbContext.cs` (filtry globalne, miękkie usuwanie)
- [x] `Program.cs` (mapowanie grup, uwierzytelnienie, filtry ról)
- [x] `Endpoints/*.cs` - wszystkie 16 plików przeczytane (tabela do spisania)
- [x] `Services/AccountingExport`, `InvoiceTemplates`, `RecurringInvoices`, `InvoiceNumbering`, `Email/*`
- [x] usługi w tle: `KsefSubmissionProcessor`, `ReceivedInvoiceSync`, `IssuedInvoiceImport`, `RecurringInvoices`, `PaymentReminders`, `EmailOutbox` (przeczytane)
- [x] potwierdzenie testem: kolizja globalnie unikalnego `Invoices.KsefNumber` (ISO-1), import drugiej firmy (ISO-1), harmonogram-trucizna (ISO-2) - test tymczasowy `Tmp02IsolationTests.cs` uruchomiony i USUNIĘTY
- [x] potwierdzenie: PUT /company dla konta-członka bez własnej firmy = 403 (istniejący test `CompanyRoleTests.Ksiegowa_...` linia z `PutAsJsonAsync("/api/company")`) -> ISO-3
- [ ] `VatRegistryLookup`, `InvoiceFiles`, `Mapping.ToDto` (co wraca w DTO firmy)
- [ ] `DemoDataSeeder`
- [ ] `docs/api.md` - zgodność ról z opisem (sync faktur otrzymanych przez księgową, bank-account-check)
- [ ] pokrycie testami izolacji i ról (`CompanyRoleTests`, `*ApiTests`)
- [ ] spisanie tabeli endpointów, sekcji końcowych
Następny krok: `VatRegistryLookup`, `InvoiceFiles`, `Mapping`, `DemoDataSeeder`, `docs/api.md`, pokrycie testami; potem tabela endpointów.

## Notatki robocze (kandydaci na znaleziska - do spisania w pełnym formacie)
- K1: `IX_Invoices_KsefNumber` unikalny GLOBALNIE (AppDbContext.cs:140), `Company.Nip` nieunikalny; `IssuedInvoiceImport.KnownAsync` sprawdza tylko własną firmę -> druga firma o tym samym NIP: naruszenie unikalności; w `KsefSubmissionProcessor.ProcessAsync` `SaveChangesAsync` (linia 60) jest poza try -> wyjątek przerywa przebieg, a wysyłka-trucizna (OrderBy Id, Take 50) blokuje wysyłki WSZYSTKICH firm. W `IssuedInvoiceImport.SyncAsync` końcowy SaveChanges (linia 111) rzuca ponownie (encja Added zostaje w trackerze).
- K2: `PUT /api/company` - `RequireRoleForChanges(Owner)`: konto, które jest tylko członkiem cudzej firmy (bez własnej), dostaje 403 i nie może założyć własnej firmy (CompanyEndpoints.cs:14-32, CurrentCompany.cs:52-55).
- K3: `RecurringInvoices.RunAsync` / `PaymentReminders.RunAutomaticAsync` / `ReceivedInvoicesWorker` - brak izolacji wyjątków per firma (wyjątek jednej firmy przerywa przebieg dla pozostałych). Wyzwalacz w RecurringInvoices: 3 kolizje numeru (np. ręczny numer FV/2147483647/MM/RRRR -> przepełnienie int w `InvoiceNumbering`).
- K4: grupa `/received-invoices` bez filtra ról: księgowa może POST `/sync` i `/bank-account-check` (do porównania z api.md).
- K5: filtr globalny `Payment` nie uwzględnia `Invoice.IsDeleted` - płatność `FromInvoice` po odrzuconej i usuniętej fakturze (sprawdzić, czy gdziekolwiek jest liczona).
- K6: GET `/company/notifications`, `/company/ksef` dostępne dla każdej roli (ujawnia e-mail właściciela) - niskie.
- K7: `CurrentStateAsync`/`CheckCorrectionStateAsync` (InvoiceEndpoints.cs:577, 595) bez warunku `CompanyId` - bezpieczne pośrednio, obrona w głąb.

## Znaleziska

### ISO-1. Globalnie unikalny numer KSeF: jedna firma blokuje wysyłkę do KSeF wszystkich firm i import faktur drugiej firmy
- **Waga:** krytyczne
- **Rodzaj:** bezpieczeństwo (naruszenie izolacji firm - odmowa usługi między najemcami) / błąd
- **Pewność:** potwierdzone (tymczasowy test integracyjny `Tmp02IsolationTests.T1/T2` na atrapie KSeF - opis niżej; test usunięty)
- **Miejsce:** `FakturowanieKSeF.Api/Data/AppDbContext.cs:140` (indeks unikalny `IX_Invoices_KsefNumber` na samym `KsefNumber`, bez `CompanyId`);
  `FakturowanieKSeF.Api/Services/IssuedInvoiceImport.cs:119-122` (`KnownAsync` sprawdza tylko własną firmę) i `:79`, `:111` (zapis);
  `FakturowanieKSeF.Api/Services/KsefSubmissionProcessor.cs:40-47, 60` (`OrderBy(Id).Take(50)`, `SaveChangesAsync` poza `try`);
  brak unikalności `Company.Nip` (`AppDbContext.cs:39-50`, `CompanyEndpoints.cs:23-34`).
- **Problem:** NIP firmy nie jest unikalny (dwa konta mogą założyć firmę o tym samym NIP; w testowym KSeF każdy może wygenerować token
  dla dowolnego NIP przyciskiem „test-token”), a numer KSeF faktury jest unikalny w całej tabeli `Invoices`, a nie w obrębie firmy.
  Faktura zaimportowana przez firmę B zajmuje numer KSeF, którego potem nie może zapisać firma A. W `KsefSubmissionProcessor`
  zapis jest poza blokiem `try`, więc naruszenie indeksu przerywa cały przebieg; ta sama wysyłka wraca jako pierwsza w każdym
  kolejnym przebiegu (sortowanie po `Id`), więc **wszystkie późniejsze wysyłki wszystkich firm nigdy nie są przetwarzane**.
- **Scenariusz (potwierdzony):**
  1. Firma A (NIP N) wysyła fakturę; usługa w tle wykonuje krok 1 (faktura w KSeF, numer referencyjny zapisany).
  2. Atakujący zakłada konto z firmą B o tym samym NIP N, generuje token testowy i wywołuje `POST /api/invoices/import-from-ksef`
     zanim A pozna numer KSeF (okno można dowolnie wydłużyć na własnej parze kont: A usuwa token `DELETE /api/company/ksef/token`,
     B importuje, A przywraca token). Import B zapisuje fakturę z `KsefNumber = K` (200, `downloaded: 1`).
  3. Niezwiązana firma C wysyła swoją fakturę.
  4. Każdy przebieg `KsefSubmissionProcessor.ProcessAsync` kończy się `DbUpdateException: 23505 duplicate key ... "IX_Invoices_KsefNumber"`;
     po 4 przebiegach faktura A i faktura C mają nadal status `Pending` (C nigdy nie została wysłana do KSeF).
  Wariant bez wyścigu (T1): A i B mają ten sam NIP, faktura wystawiona poza aplikacją; A importuje (200), B dostaje
  **409 „Taki rekord już istnieje”** przy każdym imporcie, 0 faktur, punkt kontrolny B nie przesuwa się (import B trwale zablokowany
  na tej fakturze), a odpowiedź 409 ujawnia B, że inna firma w aplikacji ma już tę fakturę. W `ReceivedInvoicesWorker` ten sam wyjątek
  z końcowego `SaveChangesAsync` (linia 111 - nieudana encja zostaje w trackerze) przerywa pętlę po firmach w danym cyklu.
- **Propozycja poprawki:** (a) indeks unikalny `(CompanyId, KsefNumber)` zamiast globalnego (migracja); (b) w `KsefSubmissionProcessor`
  objąć `SaveChangesAsync` izolacją błędu per wysyłka (po błędzie `ChangeTracker.Clear()`/osobny zakres i przejście do następnej,
  licznik `FailedAttempts` zapisany osobnym poleceniem), żeby jedna wysyłka nie mogła zatrzymać kolejki; (c) w `IssuedInvoiceImport`
  po nieudanym zapisie odłączyć encję przed końcowym `SaveChangesAsync`; (d) rozważyć decyzję, czy dwie firmy o tym samym NIP są
  dozwolone (jeśli nie - indeks unikalny na `Companies.Nip` z filtrem `IsDeleted = false`; jeśli tak - opisać w decyzjach).
- **Test, który to wykryje:** `KsefSendingTests.NumerKsefZajetyPrzezInnaFirme_NieBlokujeWysylekPozostalychFirm` (scenariusz 1-4, asercja:
  faktura C `Accepted`); `IssuedImportTests.DwieFirmyOTymSamymNip_ImportujaTeSameFakturyNiezaleznie` (obie firmy `downloaded = 1`).

### ISO-2. Usługi w tle bez izolacji błędów per firma: harmonogram jednej firmy zatrzymuje faktury cykliczne pozostałych
- **Waga:** wysokie
- **Rodzaj:** bezpieczeństwo (odmowa usługi między firmami) / błąd
- **Pewność:** potwierdzone dla `RecurringInvoices` (tymczasowy test `Tmp02IsolationTests.T3`, usunięty); dla pozostałych usług - z analizy kodu
- **Miejsce:** `FakturowanieKSeF.Api/Services/RecurringInvoices.cs:42-83` (pętla po harmonogramach wszystkich firm bez `try/catch`) i `:100-117`
  (trzecia kolizja numeru rzuca wyjątek); `FakturowanieKSeF.Api/Services/InvoiceNumbering.cs:256-261` (`max + 1` przepełnia `int`);
  analogiczny wzorzec: `Services/Email/PaymentReminders.cs:54-85` (pętla po firmach, łapany tylko `EmailLimitException`),
  `Services/ReceivedInvoiceSync.cs:188-194` (pętla `ReceivedInvoicesWorker` po firmach - wyjątek z `SyncAsync` jednej firmy,
  np. `CryptographicException` z `Unprotect` albo ISO-1, pomija pozostałe firmy w cyklu).
- **Problem:** przebieg obsługuje wszystkie firmy w jednej pętli i jednym `DbContext`; wyjątek przy jednej firmie przerywa przebieg,
  a ponieważ stan „trującego” rekordu się nie zmienia, każdy kolejny przebieg kończy się w tym samym miejscu. Firmy o wyższym
  `CompanyId` (sortowanie `OrderBy(CompanyId)`) nigdy nie dostają szkiców faktur cyklicznych.
- **Scenariusz (potwierdzony):** firma A tworzy szkic z ręcznym numerem `FV/2147483647/11/2026` (201), potem dowolną fakturę z numerem
  automatycznym - dostaje `FV/-2147483648/11/2026` (przepełnienie), i harmonogram z datą dzisiejszą. Firma B (założona później) ma
  poprawny harmonogram na dziś. `RecurringInvoices.RunAsync()` → `DbUpdateException: duplicate key "IX_Invoices_CompanyId_Number"`
  w każdym przebiegu; harmonogram B: `IssuedCount = 0`, `NextIssueDate` bez zmian. Stan trwa do końca miesiąca albo interwencji w bazie.
- **Propozycja poprawki:** `try/catch` wokół obsługi jednego harmonogramu/firmy z `ChangeTracker.Clear()` (albo osobny zakres DI na firmę),
  po błędzie wstrzymanie harmonogramu z `LastError`; w `InvoiceNumbering` ignorować numery spoza rozsądnego zakresu / liczyć w `long`
  i pomijać wartości, których `+1` nie mieści się w formacie. To samo w `PaymentReminders.RunAutomaticAsync` i `ReceivedInvoicesWorker`.
- **Test, który to wykryje:** `RecurringInvoiceApiTests.BladHarmonogramuJednejFirmy_NieWstrzymujeHarmonogramowInnychFirm`
  (scenariusz jak wyżej, asercja: szkic firmy B utworzony, harmonogram A wstrzymany z `LastError`).

### ISO-3. Konto będące członkiem cudzej firmy nie może założyć własnej firmy (403 na `PUT /api/company`)
- **Waga:** wysokie
- **Rodzaj:** błąd (autoryzacja ról stosowana do niewłaściwej firmy)
- **Pewność:** potwierdzone (analiza kodu + istniejący test `CompanyRoleTests.Ksiegowa_CzytaIEksportuje_...`, który dla księgowej
  BEZ własnej firmy oczekuje 403 na `PUT /api/company`)
- **Miejsce:** `FakturowanieKSeF.Api/Endpoints/CompanyEndpoints.cs:14-32`; `FakturowanieKSeF.Api/Infrastructure/CurrentCompany.cs:45-55, 112-121`
- **Problem:** `PUT /api/company` jest jednocześnie „utwórz moją firmę” i „edytuj bieżącą”. Filtr `RequireRoleForChanges(Owner)` ocenia rolę
  w firmie bieżącej, a dla konta bez własnej firmy bieżącą jest firma z członkostwa - więc osoba zaproszona (pracownik/księgowa), która
  nie zdążyła założyć firmy przed przyjęciem zaproszenia, dostaje 403 i nie ma żadnej drogi do założenia własnej firmy (D46 zakłada:
  „jedno konto może należeć do wielu firm i mieć własną”). Test `BiuroRachunkowe_...` przechodzi tylko dlatego, że zakłada firmę PRZED przyjęciem zaproszenia.
- **Scenariusz:** rejestracja konta z linku zaproszenia → `POST /api/account/invitations/accept` → `PUT /api/company` z danymi własnej
  firmy → 403 „Twoja rola w firmie (księgowa) nie pozwala na tę operację”. Nie pomaga też nagłówek/ciasteczko wyboru firmy (nie ma czego wybrać).
- **Propozycja poprawki:** rozdzielić tworzenie od edycji: tworzenie własnej firmy (`POST /api/company` albo `PUT`, gdy użytkownik nie
  jest właścicielem żadnej firmy) nie podlega filtrowi roli w cudzej firmie i zawsze dotyczy `OwnerId == UserId`; edycja - tylko gdy
  bieżąca firma jest własna. W endpoincie sprawdzać `db.Companies.Any(c => c.OwnerId == userId)`, a nie `current.FindAsync() is null`.
- **Test, który to wykryje:** `CompanyRoleTests.CzlonekCudzejFirmy_BezWlasnej_MozeZalozycWlasnaFirme` (accept → PUT → 201, potem dwie firmy
  w `/api/account/companies`).
