# Fakturowanie-KSeF

Aplikacja webowa do wystawiania i odbierania faktur zgodna z Krajowym Systemem e-Faktur (KSeF).
Projekt inżynierski.

## Architektura w skrócie

| Projekt | Rola |
|---|---|
| `FakturowanieKSeF.Api` | ASP.NET Core (.NET 8): REST API pod `/api` + serwowanie aplikacji klienckiej |
| `FakturowanieKSeF.Client` | Blazor WebAssembly: interfejs użytkownika w przeglądarce |
| `FakturowanieKSeF.Models` | Encje domenowe (EF Core) |
| `FakturowanieKSeF.Tests` | Testy jednostkowe i integracyjne (xUnit) |

Baza danych: PostgreSQL 16. Dokumentacja projektowa: [`docs/`](docs/).

## Uruchomienie środowiska deweloperskiego

Wymagania: .NET 8 SDK, Docker.

```bash
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
