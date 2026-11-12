# Raport: Obszar 07 - testy, odporność na błędy i ogólna jakość kodu
Status: W TOKU
Ostatnia aktualizacja: 2026-11-12 (wznowienie 1)

## Postęp
- [x] Kontekst: 00-zasady.md, 07-testy-i-jakosc.md, docs/testy.md
- [x] Infrastruktura testów: ApiFactory, TestUsers, atrapy (FakeKsefGateway, FakeNbpClient, FakeVatWhiteList, FakeEmailSender), E2EFixture
- [x] CI: .github/workflows/ci.yml
- [x] Jakość testów: klasy testów integracyjnych i jednostkowych (asercje, kruchość, czas, Task.Delay)
- [x] Jakość testów E2E: UserScenariosE2ETests (selektory, oczekiwania)
- [ ] Mapa pokrycia ryzyk (tabela)
- [x] Obsługa błędów w kodzie produkcyjnym (catch, async void, .Result/.Wait, CancellationToken)
- [x] Jakość ogólna: martwy kod, duplikaty, appsettings vs kod
- [ ] Build: liczba i rodzaje ostrzeżeń
- [ ] Uruchomienie `dotnet test` (na końcu - równolegle pracują obszary 04 i 06)
- [ ] Uruchomienie E2E (`E2E_TESTS=1`)
- [ ] Podsumowanie, sekcje końcowe, status ZAKOŃCZONY
Następny krok: czysty build w kopii roboczej (poza repozytorium) - policzyć ostrzeżenia; potem tabela pokrycia ryzyk; na końcu `dotnet test` i E2E w repozytorium.

## Znaleziska
(wersja robocza - kandydaci zapisani, żeby nie przepadli przy przerwaniu; pełny opis na końcu)

- KANDYDAT TST-A (średnie): `FakeKsefGateway` odbiega od prawdziwego KSeF: (1) status domyślnie od razu 200 - brak stanu „w przetwarzaniu” bez jawnego `StatusResponses`; (2) `DownloadInvoiceAsync` nigdy nie zwraca „faktura jeszcze niedostępna” (21165) - `Infrastructure/FakeKsefGateway.cs:104-112`; (3) brak symulacji 429/limitów; (4) `VerifyTokenAsync`/`SendInvoiceAsync` ignorują NIP (token poprawny dla każdej firmy); (5) wspólna lista `Queries` dla zapytań o otrzymane i wystawione; (6) `SentInvoices`, `ClosedSessions`, `StatusResponses`, `Downloads` bez synchronizacji, a w E2E usługa w tle działa równolegle z testem; (7) stała data w numerze KSeF `20261101`.
- KANDYDAT TST-B (średnie): wszystkie testy integracyjne działają w środowisku `Development` (`ApiFactory.cs:62`) z `RateLimiting:Enabled=false` - ścieżki produkcyjne (HSTS/HTTPS, flagi Secure ciasteczek, blokada hasła deweloperskiego, certyfikat Data Protection) sprawdza tylko jeden krok `docker-image` w CI.
- KANDYDAT TST-C (niskie): `docs/testy.md` - nazwy klas/plików i liczba testów (323) do porównania z faktycznym wynikiem.
- KANDYDAT TST-D (niskie): CI - akcje przypięte do tagów (`@v4`), brak `timeout-minutes`, brak artefaktów (trace Playwright/wyniki TRX) przy niepowodzeniu, brak `--blame-hang`; `on: push` + `pull_request` uruchamia podwójnie dla gałęzi z PR.

- KANDYDAT TST-E (wysokie, potwierdzone czytaniem kodu): „bomba zegarowa” - `PaymentTests.cs:143-167` (`Filtry_PoTerminie_…`): faktura „bieżąca” z `TestUsers.SampleInvoice()` ma stały termin 2026-11-15 (`TestUsers.cs:87`), a serwer liczy „po terminie” z zegara systemowego (`TimeProvider.System`, `ApiFactory` go nie podmienia). Od 16.11.2026 `Ids("Overdue")` zwróci dwie faktury, `ReceivablesOverdueCount == 2` → test czerwony bez zmiany kodu. Sprawdzić E2E pod tym samym kątem.
- KANDYDAT TST-F (średnie): żaden test integracyjny nie steruje czasem - `ApiFactory` nie rejestruje `FakeTimeProvider`; testy „po 7 dniach” cofają `CreatedAt` w bazie (`ReminderTests.cs:124-131`), „raz na minutę”, wygasanie zaproszeń (`CompanyRoleTests.cs:122`) i ponawianie e-maili (`EmailTests.cs:165`) przez UPDATE w bazie; mieszanie `DateTime.UtcNow` (test) z dniem polskim (serwer) - `ProformaTests.cs:67`, `ReminderTests.cs:151-152`, `PaymentTests.cs:76,125` (okno 23:00-01:00).
- KANDYDAT TST-G (niskie): `CookieAuthTests.LogowanieCiasteczkiem_UstawiaBezpieczneCiasteczko…` (`AuthTests.cs:77`) nie sprawdza flagi `Secure` (środowisko Development) - nazwa obiecuje więcej; rejestracja bez sprawdzenia wyniku (`AuthTests.cs:81`).
- KANDYDAT TST-H (niskie): wspólna atrapa i baza na klasę: `ProcessKsefAsync` przetwarza wysyłki wszystkich firm klasy, a `StatusResponses` to jedna kolejka - np. `PodwojneKlikniecieWyslij…` zostawia wysyłkę w toku, którą przetworzy następny test (`KsefSendingTests.cs:236-248`, `152-157`); wynik zależy od kolejności testów.
- KANDYDAT TST-I (średnie/niskie): E2E mieszają strefy: `DateTime.Today` procesu testów (UTC w CI) vs przeglądarka `Europe/Warsaw` vs serwer `PolishDate` - `E2E/UserScenariosE2ETests.cs:467` (eksport, miesiąc), `:670` (faktura cykliczna: `#next-date` = dziś wg UTC < dziś wg czasu polskiego → 400 „data w przeszłości” między 23:00 a 24:00 UTC zimą, 22:00-24:00 latem), `:704`.
- KANDYDAT TST-J (niskie): selektory E2E - 95 użyć `data-testid`, ale też strukturalne/stylowe: `button[type=submit]` (37×), `.alert-success`, `.alert-danger`, `.fs-5.text-end` (3×), `tbody tr`, `.items-table tbody tr`, `.invoice-sheet`, `path.viz-bar[d^='M']`, `.viz-table tbody td`, kolor `rgb(230, 237, 240)`; `WaitForTimeoutAsync(300)` tylko przed zrzutem ekranu (`:832`).
- KANDYDAT TST-K (średnie, kod produkcyjny): nieobsłużone wyjątki usług zewnętrznych → 500: (1) `PUT /api/company/ksef/token` łapie tylko `KsefAuthenticationException` (`KsefSettingsEndpoints.cs:37`) - niedostępny KSeF (`HttpRequestException`, `TaskCanceledException`, `TimeoutException` bez statusu 4xx) → 500; `POST …/test-token` (`:59`) łapie `HttpRequestException`, ale nie `TimeoutException`/`TaskCanceledException`/`KsefApiException`; (2) `NbpClient.GetRatesAsync` (`NbpExchangeRates.cs:49-52`) - odpowiedź nie-JSON (strona błędu/ochrona przed botami) → `JsonException`/`NotSupportedException`/`FormatException` zamiast `NbpException` → 500 przy zapisie faktury walutowej (`InvoiceEndpoints.cs:544` łapie tylko `NbpException`). Klient wykazu VAT robi to poprawnie (`VatWhiteListClient.cs:125`). Atrapy nie potrafią zasymulować tych błędów inaczej niż wyjątkiem ustawionym ręcznie - brak testu.
- Sprawdzone bez uwag: brak `async void`, `.Result`, `.Wait()`, `GetAwaiter().GetResult()` w kodzie produkcyjnym; wszystkie `catch (Exception)` w usługach w tle logują i przepuszczają `OperationCanceledException`; `CancellationToken` przekazywany w endpointach i usługach. Martwy kod: przeszukanie typów i metod bez odwołań - brak trafień (poza `tools/FakturowanieKSeF.KsefSpike` - prototyp z etapu 1 nadal w rozwiązaniu, budowany w CI).
- KANDYDAT TST-L (niskie): konfiguracja - `appsettings.json` nie zawiera żadnej z ~25 opcji czytanych w kodzie (`Ksef:Worker:*`, `Ksef:Receive:*`, `Ksef:ImportIssued:Enabled`, `Email:Worker:*`, `Reminders:Worker:*`, `Recurring:Worker:*`, `RateLimiting:*`, `VatWhiteList:*`, `App:PublicUrl`, `ReverseProxy:Enabled`, `Database:MigrateOnStartup`, `DataProtection:*`); klucze jako literały rozsiane po plikach, bez klas opcji i walidacji przy starcie - literówka w zmiennej środowiskowej jest po cichu ignorowana (np. `RateLimiting__Enabled=flase` → wyjątek dopiero przy pierwszym użyciu albo wartość domyślna). Sprawdzić, czy jest dokument z listą opcji.
