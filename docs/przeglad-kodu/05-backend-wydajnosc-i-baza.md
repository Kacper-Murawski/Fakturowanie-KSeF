# Obszar 05 - backend: wydajność, baza danych, współbieżność

Raport zapisz w `docs/przeglad-kodu/wyniki/05-backend-wydajnosc-i-baza.md`. ID znalezisk: `PERF-1`, `PERF-2`, …
Najpierw przeczytaj `00-zasady.md`, potem `docs/architektura.md` i `docs/model-danych.md`.

## Zakres plików

- `FakturowanieKSeF.Api/Data/AppDbContext.cs`, `Data/Migrations/*` (indeksy, typy kolumn - tylko najnowszy snapshot
  `AppDbContextModelSnapshot.cs` i ostatnie migracje)
- `FakturowanieKSeF.Models/Models/*`
- wszystkie `FakturowanieKSeF.Api/Endpoints/*.cs` i `Services/*.cs` (zapytania EF Core)
- `FakturowanieKSeF.Api/Program.cs` (rejestracja usług, czasy życia, konfiguracja Kestrel/HttpClient)
- `FakturowanieKSeF.Api/Services/InvoicePdfRenderer.cs`, `AccountingExport.cs` (generowanie plików)

## Pytania kontrolne

**Zapytania do bazy**
- N+1: pętle wykonujące zapytanie na element (listy faktur, pulpit, eksport, przypomnienia, usługi w tle).
- Pobieranie całych encji z `Include` tam, gdzie wystarczy projekcja (`Select`) - szczególnie duże kolumny
  (`SentXml`, `UpoXml`, `Xml` faktur otrzymanych) pobierane przy listach.
- Brakujące `AsNoTracking()` przy odczytach; zbędne `SaveChangesAsync` w pętli zamiast jednego.
- Filtrowanie i sortowanie po stronie aplikacji zamiast bazy (`ToListAsync()` przed `Where`/`OrderBy`/`Count`).
- Wyszukiwanie tekstowe (`q`): `Contains`/`ILike` na dużych tabelach - czy są indeksy (np. trigramowe) lub limity?
- Indeksy: porównaj zapytania z indeksami w snapshot (filtry po `CompanyId` + data/status/numer, `KsefNumber`,
  `NextIssueDate`, kolejka e-mail, wysyłki otwarte). Wskaż brakujące i zbędne.
- Stronicowanie: limit `pageSize`, stabilne sortowanie, `Count` liczony osobno - czy da się taniej?

**Współbieżność i spójność**
- Wyścigi „sprawdź, potem zapisz” (numeracja, unikalność, jednokrotne operacje): czy chroni je indeks unikalny,
  kontrola wersji (`xmin`/`Version`) lub transakcja? Czy `DbUpdateConcurrencyException` i naruszenia unikalności
  zamieniają się w czytelne 409, a nie 500?
- Transakcje obejmujące wiele zapisów (np. wysyłka: zapis XML + stan + płatność) - czy częściowy zapis jest możliwy?
- `DbContext` w usługach w tle: zakres na przebieg, a nie na całe życie usługi.

**Zasoby i pamięć**
- Generowanie PDF/ZIP/CSV: całość w pamięci vs strumieniowanie; rozmiar przy eksporcie za rok.
- `HttpClient`: tworzenie przez `IHttpClientFactory`, limity czasu, ponowne użycie połączeń.
- `IMemoryCache`: limity rozmiaru, klucze rosnące bez końca (np. per NIP / per token).
- Alokacje w gorących ścieżkach (np. serializacja, wyrażenia regularne bez `GeneratedRegex`, LINQ w pętlach).
- Logowanie: poziomy, koszty, dane w logach (ilość, nie bezpieczeństwo - to obszar 01).

**Uproszczenia**
- Powtarzający się kod w endpointach (pobranie firmy, sprawdzenie istnienia, mapowanie) - czy da się wydzielić
  bez dodawania niepotrzebnych warstw? Martwy kod, nieużywane DTO/metody, zbędne konwersje.
- `InvoiceEndpoints.cs` jest bardzo długi - czy podział poprawiłby czytelność bez zmiany zachowania (propozycja,
  nie wymóg)?

Jeśli możesz, zmierz zamiast zgadywać: uruchom aplikację z danymi demonstracyjnymi lokalnie
(baza PostgreSQL, `DemoData:Seed=true`, `DemoData:GenerateKsefTokens=false`, `Ksef:Receive:Enabled=false`)
i włącz logowanie zapytań EF (`Microsoft.EntityFrameworkCore.Database.Command=Information`), żeby policzyć zapytania
na typowych ekranach (pulpit, lista faktur, faktura, eksport).
