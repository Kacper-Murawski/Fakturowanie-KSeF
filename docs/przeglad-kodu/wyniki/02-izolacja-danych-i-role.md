# Raport: Obszar 02 - izolacja danych między firmami i autoryzacja ról
Status: ZAKOŃCZONY
Ostatnia aktualizacja: 2026-11-11 (zakończenie przeglądu)

## Postęp
- [x] `Infrastructure/CurrentCompany.cs` + pytania kontrolne o wybór firmy
- [x] `Data/AppDbContext.cs` (filtry globalne, miękkie usuwanie)
- [x] `Program.cs` (mapowanie grup, uwierzytelnienie, filtry ról)
- [x] `Endpoints/*.cs` - wszystkie 16 plików, tabela endpointów poniżej
- [x] `Services/AccountingExport`, `InvoiceTemplates`, `RecurringInvoices`, `InvoiceNumbering`, `InvoiceFiles`, `Email/*`, `Infrastructure/Mapping.cs`, `VatRegistryLookup`
- [x] usługi w tle: `KsefSubmissionProcessor`, `ReceivedInvoiceSync`, `IssuedInvoiceImport`, `RecurringInvoices`, `PaymentReminders`, `EmailOutbox`
- [x] potwierdzenia testem tymczasowym `FakturowanieKSeF.Tests/Tmp02IsolationTests.cs` (T1-T3) - uruchomiony i USUNIĘTY
- [x] zaproszenia / członkowie firmy
- [x] miękkie usuwanie w eksportach/PDF/wykresach, endpointy plików
- [x] `DemoDataSeeder`, `docs/api.md` (reguły ról)
- [x] pokrycie testami izolacji i ról
Następny krok: brak - przegląd zakończony.

## Podsumowanie

Podstawowa izolacja danych jest zrobiona konsekwentnie: wszystkie endpointy biznesowe (tabela niżej) biorą `CompanyId` wyłącznie z
`CurrentCompany`, każdy odczyt i zapis (także zapytania pomocnicze: kontrahent, faktura korygowana, proforma, wzór faktury
cyklicznej, płatności, e-maile, pliki PDF/XML/UPO/ZIP/CSV) ma warunek `CompanyId == company.Id`, cudzy rekord daje 404, a wybór
firmy nagłówkiem/ciasteczkiem działa tylko wśród firm dostępnych dla tożsamości. **Nie znalazłem żadnej ścieżki odczytu ani
modyfikacji cudzych danych przez API (IDOR) ani obejścia ról dla operacji wystawiania faktur.**
Najpoważniejsze ryzyka leżą poza samymi zapytaniami: (1) współdzielone zasoby między firmami - globalnie unikalny numer KSeF
i wspólne kolejki usług w tle pozwalają jednej firmie zatrzymać wysyłkę do KSeF i faktury cykliczne wszystkich pozostałych
(ISO-1, ISO-2, oba potwierdzone testem); (2) filtr roli na `PUT /api/company` uniemożliwia członkowi cudzej firmy założenie
własnej (ISO-3); (3) drobniejsze luki w rolach księgowej i w pokryciu testami ról (ISO-4, ISO-7).
Liczba znalezisk: krytyczne 1, wysokie 2, średnie 2, niskie 2.

## Tabela endpointów

Legenda: **Uwierz.** - wymagane zalogowanie (`RequireAuthorization`); **Rola (zmiany)** - kto może wykonać operację
(W = właściciel, P = pracownik, K = księgowa); **Filtr firmy** - czy wszystkie zapytania mają warunek `CompanyId` z `CurrentCompany`;
**Test** - istniejący test izolacji (I) / ról (R); „-” = brak.

| Metoda | Ścieżka | Uwierz. | Rola | Filtr firmy | Test |
|---|---|---|---|---|---|
| * | `/api/auth/*` (Identity: register, login, refresh, manage/info…) | wg Identity | n/d | n/d (dane konta) | AuthTests |
| POST | `/api/auth/logout` | tak | każda | n/d | AuthTests |
| GET | `/api/health` | nie | - | n/d | HostingTests |
| GET | `/api/company` | tak | W P K | tak (firma bieżąca) | I: CompanyAndCustomerApiTests |
| PUT | `/api/company` | tak | W (lub brak firmy) - **zob. ISO-3** | tak | R: CompanyRoleTests |
| GET | `/api/company/ksef` | tak | W P K | tak | - |
| PUT | `/api/company/ksef/token` | tak | W | tak | R: - (tylko DELETE) |
| POST | `/api/company/ksef/test-token` | tak | W | tak | R: - |
| DELETE | `/api/company/ksef/token` | tak | W | tak | R: księgowa |
| GET | `/api/company/notifications` | tak | W P K | tak | - |
| PUT | `/api/company/notifications` | tak | W | tak | R: pracownik |
| GET | `/api/company/members` | tak | W | tak | R: księgowa |
| POST | `/api/company/members/invitations` | tak | W | tak (firma zaproszenia = bieżąca) | R: pracownik |
| DELETE | `/api/company/members/invitations/{id}` | tak | W | tak | I: -, R: - |
| PUT | `/api/company/members/{memberId}` | tak | W | tak | I: -, R: - |
| DELETE | `/api/company/members/{memberId}` | tak | W | tak | I: -, R: - |
| GET | `/api/account/companies` | tak | każda | po `UserId` (własna + członkostwa) | CompanyRoleTests |
| POST | `/api/account/active-company` | tak | każda | `AccessAsync` - cudza firma 404 | I: CompanyRoleTests |
| GET | `/api/account/invitations/{token}` | tak | każda | po skrócie tokenu | CompanyRoleTests |
| POST | `/api/account/invitations/accept` | tak | każda | token + e-mail konta | CompanyRoleTests |
| GET/POST | `/api/account/2fa`, `/setup`, `/enable`, `/disable`, `/recovery-codes` | tak | każda | n/d (dane konta) | TwoFactorTests |
| GET | `/api/customers`, `/api/customers/{id}` | tak | W P K | tak | I: InvoiceApiTests |
| POST/PUT/DELETE | `/api/customers`, `/api/customers/{id}` | tak | W P | tak | I: InvoiceApiTests; R: tylko POST (księgowa) |
| GET | `/api/products`, `/api/products/{id}` | tak | W P K | tak | I: InvoiceApiTests |
| POST/PUT/DELETE | `/api/products`, `/api/products/{id}` | tak | W P | tak | I: InvoiceApiTests; R: - |
| GET | `/api/invoices`, `/api/invoices/{id}` | tak | W P K | tak (także proforma/korekty/płatności) | I: InvoiceApiTests |
| GET | `/api/invoices/{id}/xml`, `/pdf`, `/upo` | tak | W P K | tak (`/upo` przez `s.Invoice.CompanyId`) | I: InvoiceApiTests, PdfTests, KsefSendingTests |
| GET | `/api/invoices/next-number` | tak | W P K | tak | - |
| POST | `/api/invoices` | tak | W P | tak (`CustomerId`, `CorrectedInvoiceId` tylko własne → 400) | I: InvoiceApiTests, CorrectionTests; R: księgowa |
| PUT/DELETE | `/api/invoices/{id}` | tak | W P | tak | I: InvoiceApiTests; R: - |
| POST | `/api/invoices/{id}/send` | tak | W P | tak | I: KsefSendingTests; R: pracownik (pozytywny), księgowa - |
| POST | `/api/invoices/{id}/convert` | tak | W P | tak | I: ProformaTests; R: - |
| POST | `/api/invoices/import-from-ksef` | tak | W P | tak (`KnownAsync` per firma - **zob. ISO-1**) | I: IssuedImportTests; R: - |
| POST | `/api/invoices/{id}/email` | tak | W P | tak | I: EmailTests; R: księgowa |
| GET | `/api/invoices/{id}/emails` | tak | W P K | tak (faktura i wiadomości) | I: EmailTests |
| POST | `/api/invoices/{id}/reminder` | tak | W P (`RequireRole`) | tak | I: ReminderTests; R: - |
| GET | `/api/invoices/{id}/email-recipient` | tak | W P K | tak (kontrahent i historia per firma) | I: ReminderTests |
| POST | `/api/invoices/{id}/payments` | tak | W P K (zamierzone) | tak | I: PaymentTests; R: księgowa (pozytywny) |
| DELETE | `/api/invoices/{id}/payments/{paymentId}` | tak | W P K | tak (płatność tylko przez fakturę firmy) | I: PaymentTests |
| POST/DELETE | `/api/received-invoices/{id}/payments[/{paymentId}]` | tak | W P K | tak | I: PaymentTests |
| GET | `/api/payments/summary` | tak | W P K | tak | I: PaymentTests |
| GET | `/api/received-invoices`, `/{id}`, `/{id}/xml`, `/{id}/pdf`, `/sync-status` | tak | W P K | tak | I: ReceivedInvoicesTests (bez `/pdf`) |
| POST | `/api/received-invoices/{id}/bank-account-check` | tak | W P K - **zob. ISO-4** | tak | I: ReceivedInvoicesTests; R: - |
| POST | `/api/received-invoices/sync` | tak | W P K - **zob. ISO-4** | tak | R: - |
| GET | `/api/dashboard` | tak | W P K | tak | I: DashboardTests |
| GET | `/api/exports/summary`, `/sales.csv`, `/purchases.csv`, `/package.zip` | tak | W P K | tak (`AccountingExport` dostaje `company.Id`) | I: ExportTests; R: księgowa (ZIP) |
| GET | `/api/recurring-invoices`, `/{id}` | tak | W P K | tak | I: RecurringInvoiceTests |
| POST/PUT/DELETE | `/api/recurring-invoices`, `/{id}` | tak | W P | tak (`TemplateInvoiceId` tylko własny → 400) | I: RecurringInvoiceTests; R: - |
| GET | `/api/exchange-rates/{currency}` | tak | każda | n/d (dane publiczne NBP) | CurrencyTests |
| GET | `/api/vat-registry/{nip}` | tak | każda | n/d (dane publiczne MF; limit per użytkownik) | VatRegistryTests |
| * | `/api/{**slug}` | nie | - | 404 | HostingTests |

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

### ISO-4. Księgowa może uruchomić synchronizację z KSeF i zapisać wynik sprawdzenia rachunku (grupa `/received-invoices` bez filtra ról)
- **Waga:** średnie
- **Rodzaj:** bezpieczeństwo (autoryzacja ról niezgodna z D46)
- **Pewność:** potwierdzone analizą kodu (grupa nie ma `RequireRoleForChanges`; brak testu ról dla tych endpointów)
- **Miejsce:** `FakturowanieKSeF.Api/Endpoints/ReceivedInvoiceEndpoints.cs:23` (grupa), `:99` (`POST /{id}/bank-account-check`), `:186` (`POST /sync`)
- **Problem:** D46 i `docs/api.md` („Reguły biznesowe”) mówią, że księgowa ma odczyt, eksport i płatności, a operacje zmieniające
  dane wymagają roli właściciela lub pracownika. Grupa faktur otrzymanych nie ma filtra ról, więc księgowa może wywołać
  `POST /api/received-invoices/sync` (użycie tokenu KSeF firmy, zużycie limitów API KSeF, zapis nowych faktur i punktu kontrolnego,
  wysyłka powiadomienia e-mail do właściciela) oraz `POST .../bank-account-check` (zapis w rekordzie faktury, zużycie dziennego
  limitu wykazu VAT). Żaden dokument nie wymienia tych operacji jako dozwolonych dla księgowej.
- **Scenariusz:** konto z rolą `Accountant` → `POST /api/received-invoices/sync` → 200 i pobranie faktur (oczekiwane wg D46: 403 albo
  jawny zapis w dokumentacji, że to dozwolone).
- **Propozycja poprawki:** świadoma decyzja: albo `RequireRoleForChanges(CompanyRoles.Issuers)` na grupie (płatności są w osobnej
  grupie, więc nie ucierpią), albo dopisanie do D46/`api.md`, że synchronizacja i sprawdzenie rachunku są dostępne dla każdej roli.
- **Test, który to wykryje:** `CompanyRoleTests.Ksiegowa_NieUruchamiaSynchronizacjiZKsef` (403 dla `/sync`; ewentualnie 200 dla
  `bank-account-check`, jeśli autor uzna to za czynność księgowej).

### ISO-5. Po odebraniu dostępu wybór firmy „po cichu” wraca do firmy własnej - kolejne zapisy trafiają do innej firmy
- **Waga:** średnie
- **Rodzaj:** błąd (kontekst firmy) / ochrona „w głąb”
- **Pewność:** potwierdzone analizą kodu po stronie serwera; do weryfikacji zachowanie klienta Blazor (czy po 409/zmianie firmy
  przeładowuje stan) - brak scenariusza E2E
- **Miejsce:** `FakturowanieKSeF.Api/Infrastructure/CurrentCompany.cs:38-57` (gdy wskazana firma jest niedostępna - domyślna własna lub
  pierwsze członkostwo, bez sygnału dla klienta)
- **Problem:** ignorowanie niedostępnego wyboru jest poprawne dla izolacji (dostęp kończy się od następnego żądania - potwierdza
  test `OdebranieDostepu_KonczyDostepDoDanych`), ale serwer nie informuje klienta, że kontekst się zmienił. Osoba, która ma własną
  firmę i pracowała w firmie klienta, po odebraniu dostępu (albo po usunięciu ciasteczka `ksef.company`, które żyje 30 dni
  niezależnie od sesji) wykonuje następne żądania w SWOJEJ firmie: otwarty formularz „Nowa faktura” zapisze szkic we własnej firmie
  z własnym sprzedawcą, lista kontrahentów w formularzu pochodzi jeszcze z firmy klienta.
- **Scenariusz:** biuro rachunkowe (własna firma X + rola pracownika w A) ma otwarty formularz faktury w A → właściciel A odbiera
  dostęp → `POST /api/invoices` z danymi nabywcy wpisanymi ręcznie → 201, szkic powstaje w firmie X (z `CustomerId` z A byłby 400).
- **Propozycja poprawki:** gdy żądanie jawnie wskazuje firmę (ciasteczko/nagłówek), a wskazanie jest nieważne - nie przełączać
  po cichu dla operacji zmieniających dane: zwrócić 409 „Utracono dostęp do wybranej firmy” i skasować ciasteczko (klient
  przeładowuje listę firm); ewentualnie zwracać w odpowiedzi nagłówek z identyfikatorem firmy faktycznie użytej.
- **Test, który to wykryje:** `CompanyRoleTests.OdebranieDostepu_PrzyWlasnejFirmie_NiePrzenosiZapisowDoWlasnejFirmy`
  (własna firma + członkostwo, nagłówek `X-Company-Id` = A, odebranie dostępu, `POST /api/invoices` → 409, 0 faktur w X).

### ISO-6. Zapytania pomocnicze korekt bez warunku `CompanyId` (obrona w głąb)
- **Waga:** niskie
- **Rodzaj:** bezpieczeństwo (obrona w głąb) / uproszczenie
- **Pewność:** potwierdzone analizą kodu - obecnie NIE da się tego wykorzystać (identyfikator pochodzi z rekordu już zweryfikowanego
  po firmie), ale łamie zasadę z D15/CLAUDE.md „każde zapytanie filtrowane po bieżącej firmie”
- **Miejsce:** `FakturowanieKSeF.Api/Endpoints/InvoiceEndpoints.cs:577-581` (`CurrentStateAsync` - korekty po `CorrectedInvoiceId`),
  `:595-596` (`CheckCorrectionStateAsync` - `SingleAsync(i => i.Id == correction.CorrectedInvoiceId)`), `:378` (`RecurringInvoices.AnyAsync`
  po `TemplateInvoiceId`), `Services/Email/PaymentReminders.cs:67-69` (historia przypomnień po `InvoiceId` bez `CompanyId`)
- **Problem:** izolacja tych zapytań zależy od tego, że `CorrectedInvoiceId`/`TemplateInvoiceId` zawsze wskazuje fakturę tej samej
  firmy. Dziś gwarantują to tylko ścieżki zapisu (`ApplyAsync`, import); baza tego nie wymusza (klucz obcy bez `CompanyId`), więc
  przyszła zmiana lub ręczna poprawka danych otworzy przeciek stanu pozycji cudzej faktury do korekty.
- **Scenariusz:** brak w obecnym kodzie; hipotetycznie rekord z `CorrectedInvoiceId` innej firmy → `CurrentStateAsync` skopiuje
  cudze pozycje do „stanu przed korektą”.
- **Propozycja poprawki:** dodać `&& c.CompanyId == original.CompanyId` / parametr `companyId` do obu metod i warunek `CompanyId` w
  pozostałych dwóch zapytaniach; w `CheckCorrectionStateAsync` dodatkowo obsłużyć `null` po miękkim usunięciu (dziś `SingleAsync` → 500).
- **Test, który to wykryje:** `DataModelTests.KorektaZFakturaKorygowanaInnejFirmy_JestIgnorowana` (rekord wstawiony wprost do bazy,
  `PUT` korekty nie kopiuje cudzych pozycji).

### ISO-7. Luki w testach ról i izolacji (endpointy bez przypadku testowego)
- **Waga:** niskie
- **Rodzaj:** bezpieczeństwo (brak siatki bezpieczeństwa przy zmianach) - CLAUDE.md wymaga przypadku ról dla każdego endpointu zmieniającego dane
- **Pewność:** potwierdzone przeglądem `CompanyRoleTests.cs` i testów `UzytkownikB_*` (kod endpointów jest poprawny - brakuje testów)
- **Miejsce:** `FakturowanieKSeF.Tests/CompanyRoleTests.cs:52-91` (jedyne asercje 403); zob. kolumna „Test” w tabeli endpointów
- **Problem:** role są sprawdzane filtrem na grupie, więc przeniesienie endpointu do innej grupy (jak grupy płatności i faktur
  otrzymanych, które filtra nie mają) po cichu zdejmuje ochronę. Bez testu ról są: dla księgowej - `PUT/DELETE /invoices/{id}`,
  `/send`, `/convert`, `/import-from-ksef`, `/reminder`, `POST/PUT/DELETE /products`, `/recurring-invoices`, `PUT/DELETE /customers/{id}`,
  `PUT /company/notifications`, `PUT /company/ksef/token`, `POST /company/ksef/test-token`; dla pracownika - `/company/ksef/*`,
  `GET/PUT/DELETE /company/members*`. Bez testu izolacji: `PUT/DELETE /company/members/{id}` i `DELETE /company/members/invitations/{id}`
  z identyfikatorem z cudzej firmy (oczekiwane 404), `GET /received-invoices/{id}/pdf`, cudzy `ProformaId`/faktura w `/convert` jest (ProformaTests).
- **Scenariusz:** refaktoryzacja przenosi np. `/invoices/{id}/reminder` pod grupę bez filtra → księgowa wysyła e-maile do kontrahentów,
  żaden test nie zawodzi.
- **Propozycja poprawki / testy:** jeden test tabelaryczny `CompanyRoleTests.MacierzRol_KazdyEndpointZmieniajacy(rola, metoda, ścieżka, oczekiwanyKod)`
  (`[Theory]` po wszystkich POST/PUT/DELETE z tabeli) oraz najlepiej test strukturalny po `EndpointDataSource`: każdy endpoint
  z metodą inną niż GET/HEAD musi mieć filtr roli albo znajdować się na jawnej liście wyjątków (płatności, konto);
  `CompanyRoleTests.WlascicielA_NieZmieniaCzlonkowAniZaproszenFirmyB` (404 dla trzech endpointów).

## Sprawdzone bez uwag

- **`CurrentCompany`**: usługa `Scoped`, wynik buforowany tylko w obrębie jednego żądania (brak pól statycznych) - firma jednego żądania
  nie przenosi się do innego; usługi w tle nie używają `CurrentCompany` (dostają encję `Company`). Wskazanie cudzej lub usuniętej firmy
  (ciasteczko/nagłówek) → `AccessAsync` zwraca null → firma domyślna; nieliczbowa wartość ignorowana. Odebranie dostępu i zmiana roli
  działają od następnego żądania (odczyt z bazy przy każdym żądaniu; test `OdebranieDostepu_KonczyDostepDoDanych`).
- **Filtry ról**: `RequireRoleForChanges` przepuszcza tylko GET/HEAD; `RequireRole` blokuje także odczyt (`/company/members`); brak firmy →
  decyzja endpointu (`CompanyRequired` 409). Członek nigdy nie ma roli `Owner` (zaproszenie i zmiana roli odrzucają `Owner`).
- **Zaproszenia**: firma zaproszenia = bieżąca firma właściciela (nie z żądania) - właściciel A nie zaprosi do B; przyjęcie wymaga tokenu
  (w bazie tylko SHA-256) i konta z zapraszanym adresem; istniejący członek/właściciel → 409, więc przyjęcie nie podnosi roli; rola pochodzi
  z zaproszenia; jednorazowość, wygaśnięcie, odwołanie - poprawne; równoległe przyjęcie zatrzymuje indeks `(CompanyId, UserId)`.
  Odwołanie/zmiana roli/usunięcie członka filtrowane po `CompanyId`.
- **IDOR pośredni**: `CustomerId` (InvoiceEndpoints.cs:470), `CorrectedInvoiceId` (:430), proforma w `/convert` (:251-260), `TemplateInvoiceId`
  (RecurringInvoiceEndpoints.cs:43; generator: RecurringInvoices.cs:91-96), płatności (tylko przez fakturę firmy), e-maile
  (`InvoiceId` + `CompanyId`; `EmailOutbox.BuildAsync` ładuje załącznik z warunkiem `CompanyId == message.CompanyId`),
  podpowiedź adresata (`PaymentReminders.RecipientAsync` - kontrahent i historia per firma). `ProformaId` nie jest przyjmowany z żądania.
- **Kody odpowiedzi**: cudzy rekord → 404 (ścieżka) lub 400 „nie znaleziono” (identyfikator w ciele) - identycznie jak dla nieistniejącego,
  brak 403 ujawniającego istnienie.
- **Pliki**: PDF/XML/UPO faktur, XML/PDF faktur otrzymanych, CSV/ZIP eksportu - te same warunki firmy co endpointy danych.
- **Miękkie usuwanie**: filtry globalne na `Company`, `Customer`, `Product`, `Invoice` (+ pozycje, wysyłki), `RecurringInvoice`; listy,
  pulpit, eksport, podsumowanie płatności i PDF czytają przez `db.Invoices` z filtrem; `IgnoreQueryFilters` tylko w `IssuedInvoiceImport.KnownAsync`
  (celowo - deduplikacja). Płatność `FromInvoice` po odrzuconej i usuniętej fakturze zostaje w tabeli, ale nigdzie nie jest sumowana
  (wszystkie sumy idą przez faktury w statusie Pending/Accepted).
- **Usługi w tle - przypisanie `CompanyId`**: `ReceivedInvoiceSync`, `IssuedInvoiceImport`, `RecurringInvoices`, `PaymentReminders`,
  `EmailOutbox`, `RecordPaymentFromInvoice` tworzą rekordy z `CompanyId` firmy przetwarzanej; token KSeF i NIP z tej samej firmy
  (`KsefSubmissionProcessor` bierze NIP z `invoice.Seller.Nip` i token z `invoice.Company`). Mieszania danych nie ma - problemem jest
  wyłącznie izolacja błędów (ISO-1, ISO-2).
- **`DemoDataSeeder`**: uruchamiany tylko przy `DemoData:Seed=true` i wyłącznie w Development (`Program.cs` rzuca wyjątek w innym
  środowisku); rekordy tworzone z `CompanyId` właściwej firmy; jedyne powiązanie między firmami to zamierzona rola księgowej TestUser4 → TestUser1.
- **Endpointy publiczne względem firmy** (`/exchange-rates`, `/vat-registry`, `/account/2fa`): nie dotykają danych firm.

## Poza obszarem

- **Wykaz VAT - wspólny dzienny limit** (`VatRegistryLimiter`: 90 wyszukiwań/dobę na serwer, 20 na użytkownika): pięć kont wyczerpuje
  limit wszystkim firmom. To zamierzony kompromis (D28), ale ma ten sam charakter „jedna firma psuje innym” co ISO-1/2 - do oceny w obszarze 01.
- **`InvoiceNumbering`**: przepełnienie `int` przy ręcznym numerze `FV/2147483647/MM/RRRR` daje numer `FV/-2147483648/...` (potwierdzone
  w T3) - błąd poprawności numeracji dla obszaru 04.
- **`RecurringInvoices.CreateDraftAsync`**: chwilowy błąd NBP (`ApplyAsync` zwraca błąd waluty) trwale wstrzymuje harmonogram - obszar 03.

## Pytania do autora

1. Czy dwie firmy o tym samym NIP w aplikacji są dozwolone (np. właściciel i biuro rachunkowe na osobnych kontach, zanim powstały
   zaproszenia)? Od odpowiedzi zależy kształt poprawki ISO-1 (indeks per firma vs unikalny NIP).
2. Czy księgowa ma móc uruchamiać synchronizację faktur otrzymanych i sprawdzenie rachunku (ISO-4)?
3. `GET /api/company/notifications` i `GET /api/company/ksef` są czytelne dla każdej roli (pracownik widzi adres e-mail właściciela
   i ustawienia powiadomień, mimo że D46 wyłącza mu „powiadomienia”). `api.md` mówi „odczyt - każda rola” - czy to zamierzone także tutaj?
4. Czy członek cudzej firmy ma móc założyć własną firmę po przyjęciu zaproszenia (ISO-3), czy kolejność „najpierw własna firma” jest wymagana?
