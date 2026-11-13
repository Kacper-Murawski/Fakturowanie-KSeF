# Bezpieczeństwo

Aplikacja przetwarza dane podatkowe wielu niezależnych firm, a token KSeF daje pełny dostęp do faktur firmy
w KSeF. Środki bezpieczeństwa zestawiono według zagrożeń. Numery w nawiasach odsyłają do
[`decyzje-projektowe.md`](decyzje-projektowe.md).

| Zagrożenie | Zabezpieczenie | Weryfikacja |
|---|---|---|
| **Dostęp do danych innego użytkownika** (IDOR, multi-tenancy) | `CompanyId` wyłącznie z tożsamości zalogowanego użytkownika (`CurrentCompany`), warunek w każdym zapytaniu; cudzy rekord = 404, bez ujawnienia jego istnienia (D15) | `DataIsolationTests`, testy izolacji wysyłki, UPO, PDF, faktur otrzymanych, płatności, eksportu, katalogu i ról w firmie; test E2E dostępu po adresie strony |
| **Kradzież sesji przez XSS** | Ciasteczko sesji `HttpOnly` (niedostępne dla skryptów); klient nie przechowuje tokenów (D18); Blazor domyślnie koduje dane w HTML; **Content-Security-Policy** - przeglądarka wykonuje tylko skrypty i WebAssembly z serwera aplikacji (D47) | Test flag ciasteczka; `SecurityHeadersTests`; E2E bez naruszeń CSP |
| **Osadzenie aplikacji w obcej stronie (clickjacking)** | `X-Frame-Options: DENY`, CSP `frame-ancestors 'none'` (D47) | `SecurityHeadersTests` |
| **Wyciek adresu z tokenem (zaproszenie) przez nagłówek Referer** | `Referrer-Policy: no-referrer` (D47) | `SecurityHeadersTests` |
| **Token zaproszenia lub kod resetu hasła w logach serwera/proxy** | Tokeny i kody w linkach są we fragmencie adresu (`#token=…`), którego przeglądarka nie wysyła do serwera; podgląd zaproszenia i reset hasła - token w treści żądania POST (SEC-9, D53) | `CompanyRoleTests`, `AccountEmailTests`, E2E (brak tokenu w żądaniach) |
| **Utrata dostępu do konta / przejęcie konta przez zmianę adresu e-mail** | Reset hasła linkiem e-mail (1 h, jednorazowy; 2FA zostaje; odpowiedź nie zdradza, czy konto istnieje); zmiana adresu tylko z hasłem (i kodem 2FA), potwierdzeniem na nowy adres i powiadomieniem na stary; wbudowana zmiana adresu bez hasła zablokowana (D53) | `AccountEmailTests`, E2E `Konto_…` |
| **Masowe próby logowania na wiele kont, masowa rejestracja, zgadywanie tokenów** | Limity żądań: logowanie, rejestracja, odświeżanie tokenu, zmiana hasła i zaproszenia - 30/min z adresu IP, logowanie dodatkowo 10/min na parę adres + e-mail; sprawdzenie sesji i wylogowanie poza tym limitem (osoby za wspólnym adresem nie blokują sobie logowania); całe API - 600/min na użytkownika; 429 z `Retry-After` (D47, D52) | `RateLimitTests`, `SessionRateLimitTests`, `LoginPairRateLimitTests`, `ApiRateLimitTests` |
| **CSRF** | `SameSite=Strict`; API przyjmuje JSON, a żądanie z obcej witryny wymagałoby zgody CORS, której API nie udziela; klient i API w jednej domenie (D6) | Konfiguracja, test flag ciasteczka |
| **Zgadywanie haseł** | Polityka haseł (min. 8 znaków, różne klasy znaków); blokada konta na 15 minut po 5 nieudanych próbach; hasła haszowane (PBKDF2, Identity) | Test polskich komunikatów polityki haseł |
| **Przejęcie hasła (wyciek, phishing)** | Opcjonalne logowanie dwuskładnikowe TOTP z jednorazowymi kodami odzyskiwania; wyłączenie 2FA i nowe kody tylko po podaniu hasła i kodu - przejęta sesja nie zdejmie zabezpieczenia (wbudowany endpoint Identity zablokowany) (D41, D52) | `TwoFactorTests` (niezależny generator TOTP), test E2E |
| **Zgadywanie kodu 2FA na przejętej sesji** | Błędny kod lub hasło przy operacjach 2FA liczy się do tej samej blokady konta co logowanie (5 prób → 15 minut), plus limit 5 żądań/min na użytkownika (D52) | `TwoFactorTests`, `TwoFactorRateLimitTests` |
| **Używanie starych sesji i tokenów po zmianie zabezpieczeń** | Zmiana hasła i włączenie/wyłączenie 2FA zmieniają znacznik bezpieczeństwa konta: inne sesje (ciasteczka) wygasają w ciągu minuty, tokeny odświeżania przestają działać od razu, token dostępu Bearer żyje 15 minut (D52) | `TwoFactorTests.ZmianaZabezpieczen_UniewaznaTokenOdswiezania` |
| **Przekierowanie na obcą stronę po logowaniu** (open redirect) | `returnUrl` akceptowany tylko jako ścieżka wewnątrz aplikacji: bez ukośnika na początku, dwukropka (schematu), odwrotnego ukośnika i znaków sterujących (`ReturnUrl.Sanitize`, D52) | `ReturnUrlTests`, test E2E |
| **Wyciek tokenu KSeF** | Token szyfrowany Data Protection (AES-256-CBC + HMAC-SHA256), odszyfrowywany tylko w pamięci serwera, nigdy w odpowiedziach API ani w logach; bufor tokenów dostępu kluczowany skrótem (D23) | Test: token nie występuje w odpowiedzi ani w bazie, odszyfrowanie tylko kluczem aplikacji |
| **Omyłkowe wystawienie prawdziwej faktury (produkcyjny KSeF)** | Typ `KsefEnvironment` bez wartości produkcyjnej; `KsefTestOnlyHandler` blokuje każde żądanie spoza `api-test.ksef.mf.gov.pl` przed wysłaniem; polityka sieci środowiska deweloperskiego (D8) | 11 testów blokady (produkcja, DEMO, API 1.0, brak HTTPS, podszywanie się pod host, potok klienta MF) |
| **Wstrzyknięcie treści do XML / ataki XXE** | XML budowany przez `XmlWriter` (kodowanie znaków), bez sklejania tekstu; walidacja XSD bez DTD i bez dostępu do zasobów zewnętrznych; czytnik XML z wyłączonym DTD (D21) | Testy znaków specjalnych, testy XXE walidatora i czytnika |
| **Zmanipulowane kwoty z przeglądarki** | Kwoty liczy wyłącznie serwer, klient ich nie przesyła (D16) | Testy kalkulatora i API |
| **Nieprawidłowe dane wejściowe** | Walidacja wszystkich żądań (DataAnnotations, D14), także obiektów zagnieżdżonych i list; ograniczenia długości zgodne z bazą i FA(3) | Testy walidacji API i E2E |
| **Wstrzyknięcie formuł do arkusza (CSV injection)** | Pola tekstowe eksportu CSV zaczynające się od `=`, `+`, `-`, `@` poprzedzane apostrofem - nazwy kontrahentów z faktur otrzymanych pochodzą od osób trzecich (D34) | `CsvFormatTests`, test eksportu z „złośliwą” nazwą sprzedawcy |
| **Użycie aplikacji do rozsyłania spamu lub podszywania się** | Rejestracja wymaga potwierdzenia adresu e-mail (D53); dzienny limit wiadomości do kontrahentów na firmę i limit całej aplikacji - liczniki zwiększane atomowo w bazie, równoległe żądania nie przekroczą limitu (D53); wiadomości systemowe konta w osobnej puli (5 na adres na godzinę); wysyłka tylko faktur własnej firmy przyjętych w KSeF; nadawcą technicznym jest adres aplikacji (SPF/DMARC), firma tylko jako nazwa i adres odpowiedzi; ostrzeżenie o środowisku testowym w każdej wiadomości (D36) | `EmailApiTests` (limit, izolacja, szkic) |
| **Wstrzyknięcie nagłówków i treści do wiadomości e-mail** | Znaki sterujące usuwane z nagłówków, adresy parsowane przez MailKit, treść wyłącznie tekstowa (bez HTML) (D36) | `EmailMessageTests` (próba dopisania nagłówka Bcc w temacie) |
| **Dane finansowe pozostawione na urządzeniu (PWA)** | Service worker przechowuje tylko pliki aplikacji z listy publikacji (kontrola SHA-256); odpowiedzi `/api/` nigdy nie trafiają do pamięci podręcznej (D45) | `HostingTests`, sprawdzenie opublikowanej wersji w przeglądarce |
| **Nadużycie uprawnień przez współpracowników** | Role w firmie (właściciel, pracownik, księgowa) sprawdzane przez serwer filtrem endpointów - najmniejsze potrzebne uprawnienia; 403 przy braku uprawnień (D46) | `CompanyRoleTests` |
| **Wskazanie cudzej firmy (wybór firmy, nagłówek `X-Company-Id`)** | Wybór firmy uwzględniany tylko, gdy użytkownik ma do niej dostęp (własna lub członkostwo); inaczej ignorowany (D46) | `CompanyRoleTests` (nagłówek z cudzą firmą, wybór cudzej firmy) |
| **Przejęcie zaproszenia** | Jednorazowy token 256-bitowy w linku wysłanym na zapraszany adres (w bazie tylko skrót SHA-256), ważny 7 dni, odwoływalny; dodatkowo konto musi mieć zapraszany adres (D46) | `CompanyRoleTests` (cudze konto, zgadywany, użyty, wygasły, odwołany token) |
| **Ekspozycja usług na serwerze** | Do internetu wystawione tylko porty 80/443 proxy; aplikacja i baza wyłącznie w sieci kontenerów; aplikacja jako użytkownik bez uprawnień administratora; `AllowedHosts` ograniczone do domeny (D48) | Uruchomienie konfiguracji w kontenerach (`docs/wdrozenie.md`) |
| **SQL injection** | Wyłącznie zapytania parametryzowane (EF Core / LINQ) | Brak SQL budowanego z tekstu |
| **Wyścigi i podwójne operacje** | Optymistyczna kontrola współbieżności (`xmin`), unikalne indeksy (numer faktury, NIP, numer KSeF) (D17) | Testy konfliktu wersji i podwójnej wysyłki |
| **Utrata lub duplikacja dokumentów podatkowych** | Miękkie usuwanie, `ON DELETE RESTRICT`, faktury wysłane nienaruszalne; brak oznaczania faktury jako odrzuconej po jej przyjęciu do przetwarzania przez KSeF (D12, D25) | Testy modelu danych i awarii wysyłki |
| **Przekroczenie limitów API KSeF (blokada konta integratora)** | Budżet zapytań na synchronizację, ręczna synchronizacja max raz na minutę, cyklicznie min. 15 minut (D26) | Test ograniczenia częstotliwości |
| **Wyczerpanie limitu API wykazu VAT przez jednego użytkownika** (limit MF liczony dla adresu IP serwera) | Pamięć podręczna wyników do końca dnia, dzienny limit na użytkownika, na adres IP klienta (kilka kont z jednego komputera dzieli pulę) i dla całej aplikacji, endpoint tylko dla zalogowanych, konta z potwierdzonym adresem (D28, D53, D54) | `VatRegistryLimitTests` |
| **Zalewanie zapytaniami usług zewnętrznych z adresu serwera** (NBP, KSeF) | Podgląd kursu NBP tylko dla dat 02.01.2002 … jutro, brak kursu zapamiętywany na 15 minut, osobna pamięć podręczna kursów z limitem wpisów; zapis i wygenerowanie tokenu KSeF - 5 prób na firmę na 10 minut (D54) | `ExchangeRateRangeTests`, `ExchangeRateLookupTests`, `KsefTokenLimitTests` |
| **Ujawnienie szczegółów infrastruktury w komunikatach błędów** | Błędy wysyłki e-mail i połączenia z KSeF pokazywane użytkownikowi jako ogólne opisy (np. „Serwer poczty odrzucił wiadomość”); nazwy serwerów, porty i odpowiedzi serwera tylko w logu (D54) | `EmailApiTests.BladWysylki_NieUjawniaSzczegolowSerweraPoczty` |
| **Podatne zależności** | Wymuszone poprawione wersje bibliotek kryptograficznych klienta MF (D10); pakiety Microsoft w jednej wersji 10.0.x; `dotnet list package --vulnerable` bez zgłoszeń dla aplikacji | Audyt NuGet przy budowie |
| **Ujawnienie danych na współdzielonym środowisku testowym KSeF** | Ostrzeżenie w interfejsie przed używaniem prawdziwych danych firm (D24) | Interfejs |

## Ograniczenia i kierunki rozwoju

- **Klucze Data Protection** są w bazie, ale produkcyjnie szyfrowane certyfikatem z pliku spoza bazy
  (`DataProtection__CertificatePath`, D48) - kopia bazy bez certyfikatu nie pozwala odszyfrować tokenów KSeF.
  Certyfikat trzeba chronić i przechowywać oddzielnie od kopii bazy.
- **HTTPS**: proxy Caddy z automatycznym certyfikatem, przekierowanie HTTP → HTTPS, HSTS na 365 dni (bez subdomen - D52),
  ciasteczka `Secure` (D48).
- **Sekrety** (hasło do bazy, SMTP, hasło certyfikatu) produkcyjnie wyłącznie w `.env` spoza repozytorium; aplikacja
  odmawia startu z hasłem deweloperskim (D48). W repozytorium są tylko dane środowiska deweloperskiego.
- Brak potwierdzania adresu e-mail przy rejestracji (Identity je obsługuje). Skutki ograniczono: zaproszenia wymagają
  tokenu z wiadomości (D46). Logowanie dwuskładnikowe jest dostępne (D41), ale opcjonalne - nie jest wymuszane.
- Rejestracja ujawnia, że adres jest już zajęty (komunikat Identity) - typowe dla aplikacji z samodzielną rejestracją;
  limit żądań (D47) utrudnia masowe sprawdzanie adresów.
- `style-src 'unsafe-inline'` w CSP - wymagane przez atrybuty `style` w komponentach (D47).
