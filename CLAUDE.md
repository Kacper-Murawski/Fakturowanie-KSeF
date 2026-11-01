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
- **Izolacja danych między użytkownikami (multi-tenancy):** każde zapytanie do bazy musi być
  filtrowane po zalogowanym właścicielu (`OwnerId`), żaden endpoint nie może zwrócić cudzych danych.
- Sekrety (hasło do bazy, tokeny i certyfikaty KSeF) nie mogą trafiać do repozytorium w konfiguracji produkcyjnej:
  używaj zmiennych środowiskowych lub user-secrets.
- Produkcyjnie wymagane HTTPS. Do wdrożenia potrzebny jest Dockerfile dla API (serwującego też klienta Blazor WASM).

## Stos technologiczny
- .NET 8: ASP.NET Core Web API (`FakturowanieKSeF.Api`), Blazor WebAssembly (`FakturowanieKSeF.Client`),
  wspólne modele (`FakturowanieKSeF.Models`).
- PostgreSQL 16 + EF Core 8 (Npgsql), ASP.NET Core Identity (`MapIdentityApi`, tokeny Bearer).
- Wszystkie pakiety Microsoft przypięte do tej samej wersji 8.0.x.
- Testy: `dotnet test` (wymaga działającego PostgreSQL; testy tworzą i usuwają bazy `ksef_test_*`).
- API pod prefiksem `/api`; klient Blazor serwowany przez API (fallback do `index.html`).
- Migracje: `dotnet ef migrations add <Nazwa> --project FakturowanieKSeF.Api --output-dir Data/Migrations`.
