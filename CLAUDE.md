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
  firma do konta (`Company.OwnerId`). Każde zapytanie musi być filtrowane po firmie zalogowanego użytkownika,
  żaden endpoint nie może zwrócić cudzych danych. `CompanyId` bierz WYŁĄCZNIE z `CurrentCompany`
  (tożsamość zalogowanego użytkownika), nigdy z żądania; cudzy rekord = 404. Nowy endpoint = nowy przypadek
  w `DataIsolationTests`.
- DTO, enumy, walidacja i kalkulator kwot są w `FakturowanieKSeF.Contracts` (bez zależności od EF) - współdzielone
  przez API i klienta Blazor. Klient NIE może referencjonować `FakturowanieKSeF.Models`.
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
- Testy: `dotnet test` (wymaga działającego PostgreSQL; testy tworzą i usuwają bazy `ksef_test_*`).
- API pod prefiksem `/api`; klient Blazor serwowany przez API (fallback do `index.html`).
- Migracje: `dotnet ef migrations add <Nazwa> --project FakturowanieKSeF.Api --output-dir Data/Migrations`.
