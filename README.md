# Fakturowanie-KSeF

Aplikacja webowa do wystawiania i odbierania faktur zgodna z Krajowym Systemem e-Faktur (KSeF).
Projekt inżynierski.

## Architektura w skrócie

| Projekt | Rola |
|---|---|
| `FakturowanieKSeF.Api` | ASP.NET Core (.NET 10): REST API pod `/api` + serwowanie aplikacji klienckiej |
| `FakturowanieKSeF.Client` | Blazor WebAssembly: interfejs użytkownika w przeglądarce |
| `FakturowanieKSeF.Models` | Encje domenowe (EF Core) |
| `FakturowanieKSeF.Ksef` | Integracja z KSeF (wyłącznie środowisko testowe), na bazie oficjalnego klienta MF |
| `external/ksef-client-csharp` | Oficjalny klient KSeF Ministerstwa Finansów (submoduł git, licencja MIT) |
| `tools/FakturowanieKSeF.KsefSpike` | Prototyp konsolowy: pełny przepływ wysyłki faktury do testowego KSeF |
| `FakturowanieKSeF.Tests` | Testy jednostkowe i integracyjne (xUnit) |

Baza danych: PostgreSQL 16. Dokumentacja projektowa: [`docs/`](docs/).

## Uruchomienie środowiska deweloperskiego

Wymagania: .NET 10 SDK, Docker.

```bash
git clone --recursive <adres-repozytorium>                  # --recursive pobiera też klienta KSeF MF
git submodule update --init                                # (jeśli sklonowano bez --recursive)
docker compose up -d                                       # baza PostgreSQL
dotnet tool install --global dotnet-ef                     # jednorazowo
dotnet ef database update --project FakturowanieKSeF.Api   # migracje
dotnet run --project FakturowanieKSeF.Api                  # aplikacja + API
```

Aplikacja: http://localhost:5085, dokumentacja API (Swagger): http://localhost:5085/swagger.

## Testy

```bash
docker compose up -d
dotnet test
```

Testy integracyjne tworzą własną tymczasową bazę (`ksef_test_*`) na serwerze PostgreSQL i usuwają ją po zakończeniu.

## Prototyp KSeF (środowisko testowe)

```bash
dotnet run --project tools/FakturowanieKSeF.KsefSpike
```

Generuje losowy NIP testowy i samopodpisany certyfikat, uwierzytelnia się w **testowym** KSeF,
wysyła przykładową fakturę FA(3), odbiera numer KSeF i UPO. Wyniki trafiają do `spike-output/`.
Aplikacja nie może połączyć się z produkcyjnym KSeF (blokada w kodzie, zob. `docs/decyzje-projektowe.md`, D8).
