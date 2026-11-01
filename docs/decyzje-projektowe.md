# Decyzje projektowe

Rejestr decyzji architektonicznych i technologicznych, prowadzony na bieżąco jako materiał
do pracy inżynierskiej. Każdy wpis: kontekst, decyzja, rozważane alternatywy, uzasadnienie, konsekwencje.

---

## D1. Aplikacja webowa na serwerze (SaaS) zamiast aplikacji lokalnej

**Kontekst:** system ma umożliwiać wystawianie i odbieranie faktur przez KSeF wielu niezależnym użytkownikom.

**Decyzja:** aplikacja webowa wdrażana centralnie na serwerze. Użytkownicy korzystają z niej przez przeglądarkę
po zalogowaniu na własne konto.

**Alternatywa:** aplikacja desktopowa instalowana na komputerze użytkownika, z lokalną bazą danych.

**Uzasadnienie:**
- brak instalacji i dostęp z dowolnego urządzenia z przeglądarką (komputer, tablet, telefon),
- jedna wersja programu: aktualizacje (np. zmiany schemy FA w KSeF) wdraża się raz, dla wszystkich,
- dane w centralnej bazie z kopiami zapasowymi, a nie na pojedynczym dysku użytkownika,
- serwer może w tle odpytywać KSeF o statusy, UPO i faktury przychodzące, także gdy użytkownik nie jest zalogowany.

**Konsekwencje:**
- konieczna izolacja danych między użytkownikami (każde zapytanie filtrowane po właścicielu),
- operator serwera odpowiada za bezpieczeństwo danych i tokenów KSeF (HTTPS, szyfrowanie sekretów, RODO),
- koszty utrzymania serwera i uzależnienie od dostępności serwera i internetu.

---

## D2. Stos technologiczny: .NET 8, Blazor WebAssembly, PostgreSQL

**Decyzja:** backend ASP.NET Core Web API (.NET 8 LTS), frontend Blazor WebAssembly, baza PostgreSQL 16, ORM EF Core 8.

**Uzasadnienie:**
- jeden język (C#) po stronie serwera i klienta, wspólny projekt modeli,
- .NET 8 ma długoterminowe wsparcie (LTS),
- PostgreSQL jest darmowy i open source, dobrze współpracuje z EF Core (Npgsql),
- uruchamianie bazy w Dockerze upraszcza development i wdrożenie.

---

## D3. Uwierzytelnianie: ASP.NET Core Identity z endpointami API

**Decyzja:** `AddIdentityApiEndpoints` + `MapIdentityApi`, gotowe endpointy `/register`, `/login`, `/refresh`,
tokeny Bearer. Użytkownicy przechowywani w tej samej bazie PostgreSQL.

**Alternatywy:** własna implementacja JWT; zewnętrzny dostawca tożsamości (Keycloak, Auth0, Entra ID).

**Uzasadnienie:** sprawdzone i bezpieczne mechanizmy (hashowanie haseł, blokada konta) bez zewnętrznej infrastruktury.

**Konsekwencje:** wbudowany `/register` przyjmuje tylko e-mail i hasło, więc imię i nazwisko (`FirstName`, `LastName`)
są opcjonalne i uzupełniane w profilu.

---

## D4. Jedno konto użytkownika = jedna firma (w MVP)

**Kontekst:** użytkownik może w przyszłości prowadzić kilka działalności lub obsługiwać kilka firm (np. biuro rachunkowe).

**Decyzja:** w MVP każde konto ma dokładnie jedną firmę. Model danych (encja `Company` powiązana z właścicielem)
pozostaje przygotowany na wiele firm, żeby rozszerzenie nie wymagało przebudowy.

**Alternatywa:** od początku wiele firm na konto, z przełącznikiem aktywnej firmy w interfejsie.

**Uzasadnienie:** prostsza izolacja danych, prostszy interfejs i mniej przypadków brzegowych; pozwala szybciej
dostarczyć działające minimum. Typowy użytkownik (jednoosobowa działalność) ma jedną firmę.

## D5. Faktury korygujące poza zakresem MVP

**Decyzja:** w MVP obsługiwana jest wyłącznie podstawowa faktura VAT. Korekty (KOR) i inne rodzaje faktur
zostaną dodane po ukończeniu minimalnej funkcjonalności.

**Uzasadnienie:** korekty wymagają powiązania z fakturą pierwotną (w tym jej numerem KSeF) i osobnej logiki
różnic „przed/po”; to rozszerzenie, które nie blokuje głównego przepływu wystawienia i wysyłki faktury.

---

## D6. Klient Blazor serwowany przez serwer API, API pod prefiksem `/api`

**Decyzja:** aplikacja kliencka (Blazor WebAssembly) jest serwowana jako pliki statyczne przez ten sam serwer
ASP.NET Core, który udostępnia API. Wszystkie endpointy API mają prefiks `/api` (np. `/api/auth/login`),
a pozostałe adresy obsługuje routing klienta (fallback do `index.html`).

**Alternatywa:** osobne wdrożenie klienta (np. na CDN / osobnej domenie) i API, z konfiguracją CORS.

**Uzasadnienie:**
- jedna usługa do wdrożenia i jeden certyfikat HTTPS,
- ta sama domena (same-origin), więc nie jest potrzebny CORS, co upraszcza konfigurację i zmniejsza ryzyko błędów bezpieczeństwa,
- prefiks `/api` rozdziela adresy API od adresów stron (np. strona `/login` vs endpoint `/api/auth/login`);
  nieznane adresy pod `/api` zwracają 404 zamiast strony aplikacji.

## D7. Strategia testów automatycznych

**Decyzja:** xUnit; testy integracyjne uruchamiają całą aplikację w pamięci (`WebApplicationFactory`)
i pracują na prawdziwym PostgreSQL, na tymczasowej bazie tworzonej z migracji i usuwanej po testach.
Testy uruchamiane automatycznie w GitHub Actions przy każdym pushu (usługa PostgreSQL w CI).

**Alternatywy:** baza w pamięci (EF Core InMemory / SQLite) — szybsza, ale zachowuje się inaczej niż PostgreSQL
(typy dat, ograniczenia, transakcje), przez co testy mogą przechodzić mimo błędów występujących produkcyjnie.

**Uzasadnienie:** testy sprawdzają rzeczywiste zachowanie systemu (migracje, ograniczenia bazy, Identity).
Zabezpieczenie: fabryka testowa odmawia pracy na bazie, której nazwa nie zaczyna się od `ksef_test_`,
co chroni bazę deweloperską i produkcyjną przed przypadkowym usunięciem.

---

## D8. Komunikacja wyłącznie ze środowiskiem testowym KSeF, wymuszona w kodzie

**Kontekst:** MF udostępnia trzy środowiska KSeF 2.0: TEST (`api-test.ksef.mf.gov.pl`), DEMO (przedprodukcyjne)
i PROD (`api.ksef.mf.gov.pl`), w którym faktury mają pełną moc prawną. Omyłkowe wysłanie faktury testowej
do produkcji skutkowałoby wystawieniem prawdziwego dokumentu podatkowego.

**Decyzja:** w fazie projektu aplikacja może łączyć się wyłącznie ze środowiskiem TEST. Ochrona jest wielowarstwowa:
1. typ `KsefEnvironment` nie zawiera wartości produkcyjnej - adresu produkcji nie da się wybrać konfiguracją,
2. `KsefTestOnlyHandler` (DelegatingHandler) sprawdza adres każdego wychodzącego żądania do KSeF i blokuje
   wszystko poza `https://api-test.ksef.mf.gov.pl` zanim żądanie opuści serwer (dotyczy również adresów
   zwracanych przez samo API, np. do pobrania UPO),
3. testy automatyczne weryfikują blokadę dla adresu produkcyjnego, DEMO, API 1.0, braku HTTPS
   i domen podszywających się pod host testowy,
4. na poziomie infrastruktury: polityka sieciowa środowiska deweloperskiego dopuszcza tylko host testowy.

**Alternatywa:** wybór środowiska wyłącznie przez plik konfiguracyjny - odrzucona, bo pojedyncza pomyłka
w konfiguracji wystarczyłaby do połączenia z produkcją.

**Konsekwencje:** przejście na produkcję wymaga świadomej zmiany kodu (nowa wartość typu i rozszerzenie
listy dozwolonych hostów) oraz przeglądu bezpieczeństwa.
