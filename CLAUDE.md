# Kontekst projektu: Fakturowanie-KSeF

## Charakter projektu
- To **projekt inżynierski** (praca dyplomowa). Na końcu potrzebna jest pełna dokumentacja do pracy:
  opis działania programu, architektura, decyzje projektowe z uzasadnieniem, model danych (diagram ERD),
  przypadki użycia, opis integracji z KSeF, bezpieczeństwo, wdrożenie, testy, instrukcja użytkownika.
- Każdą istotną decyzję projektową dopisuj na bieżąco do `docs/decyzje-projektowe.md`
  (co wybrano, jakie były alternatywy, dlaczego). To materiał źródłowy do rozdziałów pracy.
- Komunikacja z autorem i dokumentacja: po polsku.

## Model działania aplikacji
- Aplikacja **webowa, wdrażana na serwerze** (model SaaS, wielu użytkowników).
  Nie jest to aplikacja instalowana lokalnie u użytkownika.
- Każdy użytkownik wchodzi na stronę, zakłada konto, loguje się i zarządza swoimi danymi:
  firmami, kontrahentami, fakturami wystawianymi i odbieranymi z KSeF.
- Uruchamianie lokalne (docker compose + dotnet run) służy wyłącznie do developmentu i testów.

## Konsekwencje dla kodu
- **KSeF: WYŁĄCZNIE środowisko testowe (`api-test.ksef.mf.gov.pl`).** Nigdy nie łącz się z produkcyjnym
  (`api.ksef.mf.gov.pl`) ani DEMO. Każdy HttpClient do KSeF musi mieć `KsefTestOnlyHandler`;
  `KsefEnvironment` celowo nie ma wartości produkcyjnej. Nie zmieniaj tego bez wyraźnej decyzji autora.
- **Izolacja danych między użytkownikami (multi-tenancy):** dane biznesowe należą do firmy (`CompanyId`),
  firma ma właściciela (`Company.OwnerId`) i ewentualnie członków z rolami (`CompanyMembers`, D46). Każde zapytanie
  musi być filtrowane po bieżącej firmie, żaden endpoint nie może zwrócić cudzych danych. `CompanyId` bierz WYŁĄCZNIE
  z `CurrentCompany` (wybór firmy z ciasteczka/nagłówka jest uwzględniany tylko spośród firm dostępnych dla tożsamości),
  nigdy z żądania; cudzy rekord = 404. Nowy endpoint zmieniający dane = filtr roli (`RequireRoleForChanges`/`RequireRole`)
  i przypadek w testach izolacji/ról.
- DTO, enumy, walidacja i kalkulator kwot są w `FakturowanieKSeF.Contracts` (bez zależności od EF) - współdzielone
  przez API i klienta Blazor. Klient NIE może referencjonować `FakturowanieKSeF.Models`.
- Frontend (Blazor WASM): logowanie ciasteczkiem sesji (`AuthService`, bez tokenów w JS), wywołania API przez
  `ApiClient`, formularze z `DeepValidator` (walidacja zagnieżdżona + błędy serwera przy polach). Teksty UI po polsku.
  Pamiętaj o kulturze pl-PL w przeglądarce (przecinek dziesiętny) - np. `[Range]` z `ParseLimitsInInvariantCulture`.
- Opis API: `docs/api.md` - aktualizuj przy każdej zmianie endpointów.
- Model danych opisany w `docs/model-danych.md` (diagram ERD) - aktualizuj go przy każdej zmianie modelu.
- Sekrety (hasło do bazy, tokeny i certyfikaty KSeF) nie mogą trafiać do repozytorium w konfiguracji produkcyjnej:
  używaj zmiennych środowiskowych lub user-secrets.
- Produkcyjnie wymagane HTTPS. Do wdrożenia potrzebny jest Dockerfile dla API (serwującego też klienta Blazor WASM).

## Stos technologiczny
- .NET 10 (LTS): ASP.NET Core Web API (`FakturowanieKSeF.Api`), Blazor WebAssembly (`FakturowanieKSeF.Client`),
  wspólne modele (`FakturowanieKSeF.Models`).
- PostgreSQL 16 + EF Core 10 (Npgsql), ASP.NET Core Identity (`MapIdentityApi`, tokeny Bearer).
- Wszystkie pakiety Microsoft przypięte do tej samej wersji 10.0.x; SDK określone w `global.json`.
- Nie zatrzymuj procesów przez `pkill -f`/`grep` po nazwie projektu (zabija własną powłokę) - używaj `fuser -k <port>/tcp`.
- Integracja z KSeF w projekcie `FakturowanieKSeF.Ksef` (rejestracja: `AddKsefTestClient()`); oficjalny klient MF
  jako submoduł `external/ksef-client-csharp` przypięty do tagu - aktualizacja tylko świadomie (RELEASE_NOTES + testy).
- XML FA(3): `Fa3XmlGenerator` + `Fa3SchemaValidator` (`FakturowanieKSeF.Ksef/Fa3`, schematy XSD osadzone jako zasoby).
  Każdy XML przed wydaniem/wysyłką MUSI przejść walidację XSD. Mapowanie pól: `docs/fa3-xml.md`.
- Wysyłka do KSeF: `IKsefGateway` (prawdziwa `KsefGateway`, w testach `FakeKsefGateway`), stan w `KsefSubmissions`,
  przetwarzanie `KsefSubmissionProcessor` (usługa w tle). ZASADA: po przyjęciu faktury do przetwarzania przez KSeF
  nigdy nie oznaczaj jej jako `Rejected` z powodu własnego błędu komunikacji (ryzyko duplikatu). Opis: `docs/integracja-ksef.md`.
  Token KSeF tylko zaszyfrowany (`KsefTokenProtector`), nigdy w odpowiedziach API ani logach.
  Odbiór faktur: `ReceivedInvoiceSync` (przyrostowo do HWM, punkt kontrolny w `Company`), odczyty tylko z lokalnej bazy,
  szanuj limity API KSeF (budżet zapytań na synchronizację, ręczna synchronizacja max raz/min, cyklicznie min. 15 min).
  Test na żywo z testowym KSeF: `KSEF_LIVE_TESTS=1 dotnet test --filter KsefLiveTests`.
- Testy E2E (Playwright): `FakturowanieKSeF.Tests/E2E`, uruchamiane z `E2E_TESTS=1` (CI zawsze); nowa funkcja UI = scenariusz E2E.
  Lokalnie w kontenerze: `E2E_CHROMIUM_PATH=/opt/pw-browsers/chromium-1243/chrome-linux64/chrome`.
- Dokumentacja do pracy: indeks `docs/README.md` - aktualizuj odpowiedni dokument przy każdej zmianie.
- UI: design system w `wwwroot/css/app.css` (D50) - tokeny CSS dla trybu jasnego i ciemnego, Bootstrap 5.3; używaj klas
  `page-header`, `surface`, `pill pill-*` (statusy), komponentu `Icon` (SVG); żadnych zewnętrznych czcionek/skryptów (CSP).
  Po zmianie `app.css`/`js/theme.js` podbij parametr `?v=` w `wwwroot/index.html` (wymusza pobranie nowej wersji).
- Pola tekstowe formularzy: `InputTextLive` (oninput), nie `InputText` - zob. D22.
- Git: każda funkcja na osobnej gałęzi o nazwie tematycznej po polsku, np. `feature/faktury-korygujace`,
  `fix/walidacja-nip` (nie nazwy losowe); jeden PR do `main` na funkcję, scalanie przez merge commit.
- Kursy walut NBP (`api.nbp.pl`): `INbpClient` (w testach `FakeNbpClient`) + `ExchangeRateLookup` (kurs z ostatniego dnia
  roboczego przed dniem sprzedaży); kurs ustala serwer przy zapisie faktury walutowej (D43). Kwot w różnych walutach nie sumuj.
- Biała lista VAT (`wl-api.mf.gov.pl`): klient `IVatWhiteListClient` (w testach `FakeVatWhiteList`), odczyty przez
  `VatRegistryLookup` (pamięć podręczna + dzienne limity, bo MF limituje zapytania na adres IP serwera).
- Testy: `dotnet test` (wymaga działającego PostgreSQL; testy tworzą i usuwają bazy `ksef_test_*`).
- API pod prefiksem `/api`; klient Blazor serwowany przez API (fallback do `index.html`).
- Dane demonstracyjne: `DemoDataSeeder` (`DemoData:Seed=true`, tylko Development, D49); nowa funkcja z danymi = uwzględnij ją w seedzie.
- Migracje: `dotnet ef migrations add <Nazwa> --project FakturowanieKSeF.Api --output-dir Data/Migrations`.
