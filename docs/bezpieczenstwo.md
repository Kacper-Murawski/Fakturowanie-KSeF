# Bezpieczeństwo

Aplikacja przetwarza dane podatkowe wielu niezależnych firm, a token KSeF daje pełny dostęp do faktur firmy
w KSeF. Środki bezpieczeństwa zestawiono według zagrożeń. Numery w nawiasach odsyłają do
[`decyzje-projektowe.md`](decyzje-projektowe.md).

| Zagrożenie | Zabezpieczenie | Weryfikacja |
|---|---|---|
| **Dostęp do danych innego użytkownika** (IDOR, multi-tenancy) | `CompanyId` wyłącznie z tożsamości zalogowanego użytkownika (`CurrentCompany`), warunek w każdym zapytaniu; cudzy rekord = 404, bez ujawnienia jego istnienia (D15) | `DataIsolationTests`, testy izolacji wysyłki, UPO, PDF i faktur otrzymanych; test E2E dostępu po adresie strony |
| **Kradzież sesji przez XSS** | Ciasteczko sesji `HttpOnly` (niedostępne dla skryptów); klient nie przechowuje tokenów (D18); Blazor domyślnie koduje dane w HTML | Test flag ciasteczka |
| **CSRF** | `SameSite=Strict`; API przyjmuje JSON, a żądanie z obcej witryny wymagałoby zgody CORS, której API nie udziela; klient i API w jednej domenie (D6) | Konfiguracja, test flag ciasteczka |
| **Zgadywanie haseł** | Polityka haseł (min. 8 znaków, różne klasy znaków); blokada konta na 15 minut po 5 nieudanych próbach; hasła haszowane (PBKDF2, Identity) | Test polskich komunikatów polityki haseł |
| **Przekierowanie na obcą stronę po logowaniu** | `returnUrl` akceptowany tylko jako adres względny | Kod logowania |
| **Wyciek tokenu KSeF** | Token szyfrowany Data Protection (AES-256-CBC + HMAC-SHA256), odszyfrowywany tylko w pamięci serwera, nigdy w odpowiedziach API ani w logach; bufor tokenów dostępu kluczowany skrótem (D23) | Test: token nie występuje w odpowiedzi ani w bazie, odszyfrowanie tylko kluczem aplikacji |
| **Omyłkowe wystawienie prawdziwej faktury (produkcyjny KSeF)** | Typ `KsefEnvironment` bez wartości produkcyjnej; `KsefTestOnlyHandler` blokuje każde żądanie spoza `api-test.ksef.mf.gov.pl` przed wysłaniem; polityka sieci środowiska deweloperskiego (D8) | 11 testów blokady (produkcja, DEMO, API 1.0, brak HTTPS, podszywanie się pod host, potok klienta MF) |
| **Wstrzyknięcie treści do XML / ataki XXE** | XML budowany przez `XmlWriter` (kodowanie znaków), bez sklejania tekstu; walidacja XSD bez DTD i bez dostępu do zasobów zewnętrznych; czytnik XML z wyłączonym DTD (D21) | Testy znaków specjalnych, testy XXE walidatora i czytnika |
| **Zmanipulowane kwoty z przeglądarki** | Kwoty liczy wyłącznie serwer, klient ich nie przesyła (D16) | Testy kalkulatora i API |
| **Nieprawidłowe dane wejściowe** | Walidacja wszystkich żądań (DataAnnotations, D14), także obiektów zagnieżdżonych i list; ograniczenia długości zgodne z bazą i FA(3) | Testy walidacji API i E2E |
| **SQL injection** | Wyłącznie zapytania parametryzowane (EF Core / LINQ) | Brak SQL budowanego z tekstu |
| **Wyścigi i podwójne operacje** | Optymistyczna kontrola współbieżności (`xmin`), unikalne indeksy (numer faktury, NIP, numer KSeF) (D17) | Testy konfliktu wersji i podwójnej wysyłki |
| **Utrata lub duplikacja dokumentów podatkowych** | Miękkie usuwanie, `ON DELETE RESTRICT`, faktury wysłane nienaruszalne; brak oznaczania faktury jako odrzuconej po jej przyjęciu do przetwarzania przez KSeF (D12, D25) | Testy modelu danych i awarii wysyłki |
| **Przekroczenie limitów API KSeF (blokada konta integratora)** | Budżet zapytań na synchronizację, ręczna synchronizacja max raz na minutę, cyklicznie min. 15 minut (D26) | Test ograniczenia częstotliwości |
| **Wyczerpanie limitu API wykazu VAT przez jednego użytkownika** (limit MF liczony dla adresu IP serwera) | Pamięć podręczna wyników do końca dnia, dzienny limit na użytkownika i dla całej aplikacji, endpoint tylko dla zalogowanych (D28) | `VatRegistryLimitTests` |
| **Podatne zależności** | Wymuszone poprawione wersje bibliotek kryptograficznych klienta MF (D10); pakiety Microsoft w jednej wersji 10.0.x; `dotnet list package --vulnerable` bez zgłoszeń dla aplikacji | Audyt NuGet przy budowie |
| **Ujawnienie danych na współdzielonym środowisku testowym KSeF** | Ostrzeżenie w interfejsie przed używaniem prawdziwych danych firm (D24) | Interfejs |

## Ograniczenia i kierunki rozwoju

- **Klucze Data Protection są w tej samej bazie co zaszyfrowane tokeny.** Kopia bazy z kluczami pozwalałaby
  odszyfrować tokeny. Na produkcji klucze należy chronić certyfikatem lub magazynem sekretów
  (`ProtectKeysWith...`) (D23, etap 9).
- **HTTPS** jest wymagany produkcyjnie: ciasteczko ma wtedy flagę `Secure`. Realizacja w ramach wdrożenia (etap 9).
- **Sekrety** (hasło do bazy) są w repozytorium wyłącznie dla konfiguracji deweloperskiej. Produkcyjnie trafią
  do zmiennych środowiskowych.
- Brak uwierzytelniania dwuskładnikowego i potwierdzania adresu e-mail. Identity je obsługuje,
  a ich włączenie planowane jest po MVP.
