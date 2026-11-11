# Obszar 02 - izolacja danych między firmami i autoryzacja ról

Raport zapisz w `docs/przeglad-kodu/wyniki/02-izolacja-danych-i-role.md`. ID znalezisk: `ISO-1`, `ISO-2`, …
Najpierw przeczytaj `00-zasady.md`, potem `docs/decyzje-projektowe.md` (D15, D46) i `docs/api.md`.

To najważniejszy obszar bezpieczeństwa aplikacji SaaS: **żaden użytkownik nie może odczytać ani zmienić danych
firmy, do której nie ma dostępu, ani wykonać operacji ponad swoją rolę.**

## Zakres plików

- `FakturowanieKSeF.Api/Infrastructure/CurrentCompany.cs` (wybór firmy z ciasteczka `ksef.company` / nagłówka `X-Company-Id`)
- wszystkie pliki `FakturowanieKSeF.Api/Endpoints/*.cs` (filtry `RequireRoleForChanges` / `RequireRole`)
- `FakturowanieKSeF.Api/Data/AppDbContext.cs` (filtry globalne, miękkie usuwanie)
- usługi wywoływane z endpointów: `Services/*.cs` (np. `AccountingExport`, `InvoiceTemplates`, `RecurringInvoices`)
- testy: `FakturowanieKSeF.Tests/CompanyRoleTests.cs`, testy izolacji w `*ApiTests.cs`

## Metoda

Zbuduj tabelę **wszystkich endpointów** (metoda, ścieżka, wymagane uwierzytelnienie, wymagana rola, skąd bierze
`CompanyId`, czy każdy odczyt i zapis jest filtrowany po bieżącej firmie, czy test izolacji/ról istnieje).
Dołącz ją do raportu - to także materiał do pracy.

Dla każdego endpointu sprawdź:
- czy **każde** zapytanie do bazy ma warunek `CompanyId == company.Id` - także zapytania pomocnicze
  (np. walidacja `CustomerId`, `CorrectedInvoiceId`, `ProformaId`, `TemplateInvoiceId`, płatności, e-maile),
- czy identyfikatory z żądania (ciało, ścieżka, zapytanie) nie pozwalają dotknąć cudzego rekordu (IDOR),
  również pośrednio: odwołanie do cudzego kontrahenta przy tworzeniu faktury, do cudzej faktury jako wzoru
  faktury cyklicznej, korekty lub proformy,
- czy cudzy rekord daje **404**, a nie 403/200 (brak wycieku informacji o istnieniu),
- czy rola **księgowej** naprawdę nie może zmieniać danych (każdy POST/PUT/DELETE/PATCH),
  a **pracownik** - ustawień firmy, tokenu KSeF, powiadomień i użytkowników,
- czy usługi w tle (`KsefSubmissionProcessor`, `ReceivedInvoiceSync`, `IssuedInvoiceImport`, `RecurringInvoices`,
  `PaymentReminders`, `EmailOutbox`) nie mieszają danych firm (np. błędne `CompanyId` przy tworzeniu rekordów).

## Pytania kontrolne

- `CurrentCompany`: czy wskazanie firmy, do której użytkownik stracił dostęp (odebrana rola), jest ignorowane natychmiast?
  Co się dzieje, gdy ciasteczko wskazuje usuniętą firmę? Czy wynik jest buforowany w sposób, który może przenieść
  firmę jednego żądania do innego?
- Usunięcie członka firmy lub zmiana roli w trakcie trwającej sesji - czy uprawnienia działają od następnego żądania?
- Zaproszenia: czy właściciel firmy A może zaprosić kogoś do firmy B? Czy przyjęcie zaproszenia może podnieść rolę?
- Miękko usunięte rekordy (`IsDeleted`): czy nie wracają w zapytaniach, eksporcie, wykresach, PDF?
- Endpointy plików (PDF, XML, UPO, ZIP, CSV): czy filtrują po firmie tak samo jak endpointy danych?
- Dane demonstracyjne (`DemoDataSeeder`): czy nie da się ich uruchomić poza Development i czy nie tworzą
  powiązań między firmami, które naruszają izolację (rola księgowej TestUser4 w firmie TestUser1 jest zamierzona).

## Testy

Wskaż endpointy bez testu izolacji lub ról i zaproponuj konkretne testy (nazwa, scenariusz). Jeśli zbudujesz
tymczasowy test, który wykazuje wyciek - opisz go dokładnie w raporcie i usuń.
