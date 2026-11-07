# Podsumowanie i kierunki rozwoju

## Cel pracy i jego realizacja

Celem było zaprojektowanie i wykonanie aplikacji webowej (model SaaS), w której mała firma wystawia faktury
ustrukturyzowane, wysyła je do Krajowego Systemu e-Faktur (KSeF 2.0) i odbiera z niego faktury kosztowe.
Cel zrealizowano w całości; integracja działa ze **środowiskiem testowym KSeF** (D8) - faktury nie mają mocy prawnej.

| Założenie | Realizacja | Dokument |
|---|---|---|
| Wielu użytkowników, izolacja danych | konta, firmy z właścicielem, role (właściciel, pracownik, księgowa), każde zapytanie filtrowane po bieżącej firmie, testy izolacji | D4, D15, D46, `bezpieczenstwo.md` |
| Wystawianie faktur zgodnych z FA(3) | generator XML z walidacją XSD przed każdym wydaniem; faktury VAT, korygujące, walutowe, proformy (poza KSeF) | D21, D32, D42, D43, `fa3-xml.md` |
| Wysyłka do KSeF | trwała maszyna stanów w tle, numer KSeF, UPO, bez ryzyka duplikatu przy awarii komunikacji | D25, `integracja-ksef.md` |
| Odbiór faktur z KSeF | synchronizacja przyrostowa do HWM z limitem zapytań; import własnych faktur wystawionych poza aplikacją | D26, D44 |
| Wydruk | PDF z dokumentu XML z kodem QR weryfikacji | D27 |
| Bezpieczeństwo | ciasteczko sesji HttpOnly, 2FA, szyfrowanie tokenów KSeF, CSP, limity żądań, blokada środowiska produkcyjnego KSeF | D18, D23, D41, D47 |
| Wdrożenie | obraz Docker, PostgreSQL, Caddy z HTTPS; konfiguracja sprawdzona w kontenerach (faktyczne wdrożenie odłożone) | D48, `wdrozenie.md` |
| Jakość | ponad 300 testów automatycznych: jednostkowe, integracyjne API z prawdziwą bazą, E2E w przeglądarce; CI | `testy.md` |

Poza zakresem MVP zrealizowano 18 z 19 funkcji dodatkowych wyłonionych na podstawie analizy programów
konkurencyjnych (`funkcje-dodatkowe.md`), m.in. wykaz podatników VAT, rozliczenia, e-mail z przypomnieniami,
faktury cykliczne, pulpit z wykresami, eksport dla księgowej i aplikację na telefon (PWA).

## Najważniejsze wnioski

- **KSeF zmienia charakter programu do fakturowania.** Faktura istnieje dopiero po przyjęciu przez KSeF, a jej treścią
  jest dokument XML. Stąd decyzje: dokument XML jako źródło prawdy (PDF i podgląd z XML, D27), utrwalanie danych
  sprzedawcy w chwili wysyłki, faktura przyjęta jest niezmienna (zmiany tylko korektą, D32).
- **Integracja z zewnętrznym systemem wymaga ostrożności wobec błędów.** Najtrudniejszą częścią była poprawna obsługa
  awarii: wysyłka jako maszyna stanów odporna na restart, zasada „nie oznaczaj jako odrzuconej po przyjęciu do
  przetwarzania” (ryzyko duplikatu w KSeF), budżet zapytań przy odbiorze faktur.
- **Wspólny kod klienta i serwera** (DTO, walidacja, kalkulator kwot w `Contracts`, D13, D19) usunął całą klasę błędów
  rozbieżności między formularzem a API; serwer i tak liczy kwoty ponownie.
- **Testy z prawdziwą bazą i przeglądarką** wykryły błędy niewidoczne w testach jednostkowych: kultura pl-PL (przecinek
  dziesiętny, D20), klasy CSS niedostępne w danej wersji Bootstrapa, naruszenia CSP.
- **Usługi państwowe mają ograniczenia praktyczne:** limity zapytań KSeF i wykazu VAT, blokada zapytań z adresów
  centrów danych przez zabezpieczenie wykazu VAT (D43) - aplikacja musi działać, gdy usługa jest niedostępna.

## Ograniczenia

- Wyłącznie środowisko testowe KSeF - użycie produkcyjne wymagałoby świadomej zmiany kodu, uwierzytelniania
  prawdziwym tokenem lub certyfikatem i przeglądu prawnego (D8).
- Brak trybu offline KSeF (funkcja 17) - przy niedostępności KSeF faktura czeka w kolejce, ale nie można jej wręczyć
  nabywcy jako faktury offline z kodem QR II.
- Aplikacja na telefon wymaga połączenia z internetem (PWA bez danych lokalnych, D45).
- Wysyłka e-mail i powiadomienia wymagają zewnętrznego serwera SMTP; brak powiadomień push.
- Jedna instancja aplikacji: usługi w tle i limity żądań działają w pamięci procesu (skalowanie poziome wymagałoby
  blokad w bazie lub kolejki zewnętrznej).
- Konfiguracja wdrożenia sprawdzona w kontenerach lokalnie; nie przeprowadzono testów obciążeniowych.

## Kierunki rozwoju

| Kierunek | Opis | Punkt wyjścia w kodzie |
|---|---|---|
| Tryb offline KSeF (kod QR II) | faktura wystawiona przy niedostępności KSeF z kodem QR II podpisanym certyfikatem KSeF, dosłanie w terminie | `KsefSubmissionProcessor`, generator PDF (D27) |
| Produkcyjny KSeF | uwierzytelnianie certyfikatem kwalifikowanym lub tokenem z Aplikacji Podatnika, osobna konfiguracja środowiska, przegląd prawny | `KsefEnvironment`, `KsefTestOnlyHandler` (D8) |
| Powiadomienia push | Web Push w service workerze PWA (nowa faktura kosztowa, odrzucenie przez KSeF) zamiast samego e-maila | D38, D45 |
| Logowanie kluczem dostępu (passkeys / WebAuthn) | logowanie bez hasła odporne na phishing, jako alternatywa dla 2FA TOTP | D41 |
| Integracja z bankiem | dopasowanie przelewów z wyciągu (MT940 / API bankowe) do faktur, automatyczne rozliczenia | D30 |
| Płatności online | link do płatności (np. bramka płatnicza) w e-mailu z fakturą | D36 |
| Integracje księgowe | eksport do formatów programów księgowych, JPK_V7 | D34 |
| Skalowanie | kilka instancji aplikacji: blokady w bazie dla usług w tle, rozproszone limity żądań | D25, D47 |
| Dziennik zdarzeń (audyt) | historia zmian faktur i działań członków firmy | D46 |
