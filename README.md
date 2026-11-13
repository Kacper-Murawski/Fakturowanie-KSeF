# Fakturowanie-KSeF

Aplikacja webowa do wystawiania i odbierania faktur zgodna z Krajowym Systemem e-Faktur (KSeF).
Projekt inżynierski.

## Architektura w skrócie

| Projekt | Rola |
|---|---|
| `FakturowanieKSeF.Api` | ASP.NET Core (.NET 10): REST API pod `/api` + serwowanie aplikacji klienckiej |
| `FakturowanieKSeF.Client` | Blazor WebAssembly: interfejs użytkownika w przeglądarce |
| `FakturowanieKSeF.Contracts` | DTO, enumy, walidacja i kalkulator kwot - współdzielone przez API i klienta |
| `FakturowanieKSeF.Models` | Encje domenowe (EF Core) |
| `FakturowanieKSeF.Ksef` | Integracja z KSeF (wyłącznie środowisko testowe), na bazie oficjalnego klienta MF |
| `external/ksef-client-csharp` | Oficjalny klient KSeF Ministerstwa Finansów (submoduł git, licencja MIT) |
| `tools/FakturowanieKSeF.KsefSpike` | Prototyp konsolowy: pełny przepływ wysyłki faktury do testowego KSeF |
| `FakturowanieKSeF.Tests` | Testy jednostkowe i integracyjne (xUnit) |

Baza danych: PostgreSQL 16. Dokumentacja projektowa (architektura, decyzje, model danych, KSeF, bezpieczeństwo, testy,
instrukcja użytkownika): [`docs/`](docs/README.md).

## Uruchomienie środowiska deweloperskiego

Wymagania: .NET 10 SDK, Docker.

**Windows - kliknij i działaj:** po sklonowaniu repozytorium uruchom `start-dev.bat`. Skrypt:
- sprawdza Git, .NET 10 SDK i Docker Desktop - brakujące instaluje przez `winget` (po instalacji Dockera
  potrzebny jest restart komputera), a niedziałający Docker Desktop uruchamia,
- pobiera submoduł klienta KSeF, uruchamia bazę i Mailpit, wykrywa bazę ze starszej wersji projektu
  (blokującą migracje) i proponuje jej wyczyszczenie,
- przy pustej bazie pyta jednorazowo o własne konto (zapis w user-secrets),
- uruchamia aplikację (migracje i dane demonstracyjne przy starcie) i otwiera przeglądarkę, gdy aplikacja odpowie.

`reset-dev.bat` usuwa lokalną bazę i uruchamia aplikację z nowymi danymi demonstracyjnymi.
Ręcznie, na dowolnym systemie:

```bash
git clone --recursive <adres-repozytorium>                  # --recursive pobiera też klienta KSeF MF
git submodule update --init                                # (jeśli sklonowano bez --recursive)
docker compose up -d                                       # baza PostgreSQL + skrzynka testowa Mailpit
dotnet build                                               # pobranie pakietów i kompilacja (wymagane przed dotnet ef)
dotnet tool install --global dotnet-ef                     # jednorazowo
dotnet ef database update --project FakturowanieKSeF.Api   # migracje
dotnet run --project FakturowanieKSeF.Api                  # aplikacja + API
```

Aplikacja: http://localhost:5085, dokumentacja API (Swagger): http://localhost:5085/swagger.
Wiadomości e-mail wysyłane przez aplikację (lokalnie nie trafiają do odbiorców): http://localhost:8025 (Mailpit).
Nowe konto trzeba potwierdzić linkiem z wiadomości - lokalnie znajdziesz ją w Mailpicie (tak samo link do resetu hasła).
Konta demonstracyjne i konta założone przed tą zmianą są już potwierdzone.

Jeśli `dotnet ef database update` zgłasza `relation "..." already exists`, baza zawiera tabele ze starszej wersji
projektu. Wyczyść ją (usuwa dane testowe): `docker compose down -v`, `docker compose up -d`, ponownie `dotnet ef database update`.

### Dane demonstracyjne (konta testowe)

Przy uruchomieniu z `DemoData__Seed=true` (robi to `start-dev.bat`) aplikacja w trybie deweloperskim tworzy konta
z kompletnymi danymi firm, kontrahentami, katalogiem i fakturami wystawionymi między sobą (D49):

| Konto | Hasło | Firma |
|---|---|---|
| TestUser1@example.com | TestUser1@ | Studio Graficzne Piksel (biuro rachunkowe TestUser4 ma tu rolę księgowej) |
| TestUser2@example.com | TestUser2@ | Hurtownia Biuro-Max (proformy) |
| TestUser3@example.com | TestUser3@ | Kod i Kawa (faktura w EUR) |
| TestUser4@example.com | TestUser4@ | Biuro Rachunkowe Saldo |

- **Historia** (ostatnie 6 miesięcy): faktury oznaczone jako przyjęte w KSeF z **przykładowym** numerem KSeF - nie
  zostały wysłane do KSeF (kod QR na PDF się nie zweryfikuje, brak UPO). U nabywcy z danych demonstracyjnych ta sama
  faktura jest fakturą otrzymaną. Są też płatności, korekta, proformy, faktura cykliczna i faktura odrzucona.
- **Szkice z dzisiejszą datą**: można je naprawdę wysłać do **testowego** KSeF. Przy pierwszym starcie aplikacja generuje
  firmom tokeny testowego KSeF (gdy KSeF nie odpowiada - token wygenerujesz później w „Moja firma”). Faktura wysłana do
  innego konta trafia do niego po „Pobierz z KSeF” (KSeF udostępnia ją nabywcy zwykle po około minucie).
- Dane powstają raz; pełny reset: `docker compose down -v` i ponowne uruchomienie. NIP-y firm są losowe przy każdym
  resecie, więc ponowna wysyłka szkiców do testowego KSeF nie koliduje z poprzednimi.

**Własne konto** (np. z prawdziwym adresem, na który mają przychodzić powiadomienia) - dane logowania tylko w
user-secrets, nie w repozytorium. `start-dev.bat` pyta o nie przy pustej bazie; ręcznie:

```bash
dotnet user-secrets --project FakturowanieKSeF.Api set "DemoData:Owner:Email" "twoj.adres@gmail.com"
dotnet user-secrets --project FakturowanieKSeF.Api set "DemoData:Owner:Password" "TwojeHaslo1@"
```

Konto dostaje własną firmę z włączonymi powiadomieniami o nowych fakturach kosztowych i przypomnieniami; konta
testowe mają dla niego szkice faktur. Lokalnie poczta trafia do Mailpita (http://localhost:8025). Aby wiadomości
dochodziły na prawdziwą skrzynkę, ustaw serwer SMTP, np. Gmail z
[hasłem aplikacji](https://myaccount.google.com/apppasswords):

```bash
dotnet user-secrets --project FakturowanieKSeF.Api set "Email:SmtpHost" "smtp.gmail.com"
dotnet user-secrets --project FakturowanieKSeF.Api set "Email:SmtpPort" "587"
dotnet user-secrets --project FakturowanieKSeF.Api set "Email:Security" "StartTls"
dotnet user-secrets --project FakturowanieKSeF.Api set "Email:SmtpUsername" "twoj.adres@gmail.com"
dotnet user-secrets --project FakturowanieKSeF.Api set "Email:SmtpPassword" "haslo-aplikacji"
dotnet user-secrets --project FakturowanieKSeF.Api set "Email:From" "twoj.adres@gmail.com"
```

Konta i dane demonstracyjne są wyłącznie deweloperskie - poza środowiskiem Development aplikacja odmawia ich wgrania.

## Wdrożenie

Konfiguracja produkcyjna (Docker Compose: aplikacja, PostgreSQL, Caddy z automatycznym HTTPS) jest w katalogu `deploy/`,
opis krok po kroku: [`docs/wdrozenie.md`](docs/wdrozenie.md).

## Testy

```bash
docker compose up -d
dotnet test
```

Testy integracyjne tworzą własną tymczasową bazę (`ksef_test_*`) na serwerze PostgreSQL i usuwają ją po zakończeniu.
Komunikacja z KSeF jest w nich zastąpiona atrapą. Testy E2E w przeglądarce (Playwright, wymagają Chromium -
instalacja: `pwsh FakturowanieKSeF.Tests/bin/Debug/net10.0/playwright.ps1 install chromium`): `E2E_TESTS=1 dotnet test`. Test na żywo z **testowym** KSeF (wymaga dostępu do
`api-test.ksef.mf.gov.pl`): `KSEF_LIVE_TESTS=1 dotnet test --filter KsefLiveTests`.

## Prototyp KSeF (środowisko testowe)

```bash
dotnet run --project tools/FakturowanieKSeF.KsefSpike
```

Generuje losowy NIP testowy i samopodpisany certyfikat, uwierzytelnia się w **testowym** KSeF,
wysyła przykładową fakturę FA(3), odbiera numer KSeF i UPO. Z parametrem `-- --generated` wysyła fakturę
zbudowaną generatorem XML aplikacji (`Fa3XmlGenerator`). Wyniki trafiają do `spike-output/`.
Aplikacja nie może połączyć się z produkcyjnym KSeF (blokada w kodzie, zob. `docs/decyzje-projektowe.md`, D8).
