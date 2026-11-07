# Testowanie

## Strategia

Testy obejmują wszystkie warstwy: logikę (testy jednostkowe), API z prawdziwą bazą (testy integracyjne),
interfejs w prawdziwej przeglądarce (E2E) oraz integrację z prawdziwym testowym KSeF (testy na żywo).
Strategię uzasadnia decyzja D7.

```mermaid
flowchart TB
    L["Testy na żywo z testowym KSeF (3)<br/>prawdziwa bramka: token, wysyłka, numer KSeF, UPO, odbiór A→B, korekta KOR"]
    E["Testy E2E w przeglądarce (9)<br/>Playwright + Chromium pl-PL, cały stos, atrapa KSeF"]
    I["Testy integracyjne (ok. 90)<br/>API + PostgreSQL + Identity + usługi w tle, atrapa KSeF"]
    U["Testy jednostkowe (ok. 80)<br/>NIP, kalkulator, stawki VAT, XML FA(3) + XSD, QR, blokada produkcji, wykaz VAT"]
    L --> E --> I --> U
```

| Rodzaj | Narzędzia | Uruchamianie | W CI |
|---|---|---|---|
| Jednostkowe | xUnit | `dotnet test` | tak |
| Integracyjne | xUnit, `WebApplicationFactory`, PostgreSQL (tymczasowa baza `ksef_test_*` na każdą klasę testów) | `dotnet test` (wymaga PostgreSQL) | tak (usługa PostgreSQL) |
| E2E | Microsoft.Playwright, Chromium (`pl-PL`), aplikacja na Kestrel | `E2E_TESTS=1 dotnet test` | tak |
| Na żywo z testowym KSeF | prawdziwa bramka + `api-test.ksef.mf.gov.pl` | `KSEF_LIVE_TESTS=1 dotnet test --filter KsefLiveTests` | nie (wymaga dostępu do KSeF) |

Łącznie **306 przypadków testowych**. CI w GitHub Actions uruchamia przy każdym wypchnięciu zmian testy
jednostkowe, integracyjne i E2E.

## Zakres

| Obszar | Klasy testów | Co sprawdzają |
|---|---|---|
| Walidacja i obliczenia | `NipValidatorTests`, `InvoiceCalculatorTests`, `VatRateTests`, `ValidationCultureTests` | NIP z sumą kontrolną; zaokrąglenia; VAT od sumy netto w stawce; zgodność stawek z XSD; walidacja w kulturach pl-PL/en-US/de-DE |
| Model danych | `DataModelTests` | znaczniki czasu, miękkie usuwanie, unikalność, `RESTRICT`, niezmienność kopii danych na fakturze, precyzja FA(3), konflikt wersji |
| API i uwierzytelnianie | `AuthTests`, `CookieAuthTests`, `CompanyApiTests`, `CustomerApiTests`, `InvoiceApiTests`, `HostingTests` | rejestracja i logowanie, flagi ciasteczka, walidacja, numeracja, statusy, kontrola wersji, filtrowanie, serwowanie klienta |
| Izolacja danych | `DataIsolationTests` i testy izolacji w klasach KSeF, PDF i faktur otrzymanych | brak dostępu do danych innego użytkownika (odczyt, lista, zmiana, usunięcie, wysyłka, UPO, XML, PDF) |
| XML FA(3) | `Fa3XmlGeneratorTests`, `Fa3SchemaValidatorTests`, `InvoiceXmlApiTests`, `Fa3InvoiceReaderTests` | każdy wariant faktury zgodny z oficjalnym XSD; UTF-8 bez BOM; znaki specjalne; ochrona przed XXE; odczyt faktur MF |
| Integracja z KSeF | `KsefSettingsTests`, `KsefSendingTests`, `ReceivedInvoicesTests`, `KsefEnvironmentGuardTests`, `KsefClientRegistrationTests` | szyfrowanie tokenu; pełny przepływ wysyłki; odrzucenie i ponowna wysyłka; awarie przed i po wysyłce; podwójna wysyłka; synchronizacja do HWM, limity, deduplikacja; blokada produkcji |
| PDF i QR | `InvoicePdfTests`, `KsefQrTests` | treść PDF odczytana z pliku (PdfPig); zgodność linku QR z klientem MF |
| Faktury korygujące | `CorrectionCalculatorTests`, `CorrectionXmlTests`, `CorrectionApiTests` | różnica per stawka; XML KOR zgodny z XSD (obniżka, korekta do zera, dopłata), odczyt KOR; tworzenie, numeracja, wysyłka, kolejna korekta, blokada przy korekcie w toku, reguły, PDF, rozliczenie po korekcie, izolacja |
| Rozliczenia | `PaymentRulesTests`, `PaymentApiTests` | stan rozliczenia i zaległości; wpłaty częściowe i pełne; nadpłata, data z przyszłości, szkic; filtry list; podsumowanie należności i zobowiązań; zapłata oznaczona w fakturze (XML, płatność przy wysyłce, brak duplikatu po odrzuceniu, blokada usunięcia); izolacja |
| Katalog towarów i usług | `ProductApiTests`, przypadek w `DataIsolationTests` | dodawanie, edycja, wyszukiwanie, usuwanie i ponowne dodanie; unikalna nazwa w firmie (409); walidacja; zmiana katalogu nie zmienia faktury; izolacja (ta sama nazwa w innej firmie dozwolona) |
| Poczta e-mail | `EmailMessageTests`, `EmailApiTests` | budowa wiadomości MIME (nadawca, odpowiedź, załącznik, wstrzyknięcie nagłówka); treść faktury i korekty; kolejka i wysyłka z PDF i XML (XML identyczny z wysłanym do KSeF); ponawianie i stan błędu; dzienny limit; walidacja; izolacja |
| Przypomnienia o płatności | `PaymentReminderTests` | treść (dni po terminie, kwota pozostała, rachunek); przypomnienie ręczne z kwotą po wpłacie częściowej; brak dla faktury opłaconej i szkicu; automatyczne: wyłączone/włączone, bez duplikatu tego samego dnia, co 7 dni, najwyżej 3, pomijanie opłaconych, niedawno po terminie i bez adresu; ustawienia; izolacja |
| Powiadomienia o fakturach kosztowych | `ReceivedNotificationTemplateTests`, `ReceivedNotificationTests` | treść (kolejność po terminie, ostrzeżenie o białej liście tylko dla PLN > 15 000 zł, link, skracanie listy); jedna wiadomość na synchronizację; brak przy synchronizacji bez nowych faktur; domyślnie wyłączone; adres konta; walidacja adresu |
| Faktury cykliczne | `RecurringScheduleTests`, `RecurringInvoiceApiTests` | kolejne daty (koniec miesiąca, rok przestępny); szkic na wzór z aktualnymi danymi kontrahenta i terminem; brak duplikatu tego samego dnia; jeden szkic po przestoju; data końcowa; wstrzymanie; walidacja; blokada usunięcia wzoru; powiadomienie; izolacja |
| Pulpit z wykresami | `DashboardTests` | sprzedaż i koszty w miesiącach (szkice pominięte), wiekowanie należności po wpłatach, najwięksi nabywcy, izolacja; E2E: kolumny, tabela danych, ranking |
| Logowanie dwuskładnikowe | `TwoFactorTests` | URI `otpauth://` i kod QR; zły kod nie włącza; logowanie wymaga kodu (`RequiresTwoFactor`), zły kod odrzucony; kod odzyskiwania jednorazowy; wyłączenie i nowe kody tylko z kodem; wbudowany `manage/2fa` zablokowany; kody liczone niezależną implementacją TOTP (RFC 6238); E2E: włączenie i logowanie z kodem |
| Faktura proforma | `ProformaTests` | seria PRO niezależna od FV; blokada KSeF, XML i płatności; PDF oznaczony jako proforma; e-mail tylko z PDF; zamiana na fakturę VAT (dane, termin, powiązania, jednokrotność, wysyłka faktury do KSeF); walidacja; izolacja; E2E |
| Waluty obce | `ExchangeRateLookupTests`, `CurrencyXmlTests`, `CurrencyApiTests`, `ExternalApiLiveTests` | dzień kursu (weekend, 11 listopada, święta); pamięć podręczna; odczyt odpowiedzi API NBP i 404; XML w EUR zgodny z XSD (`P_14_xW`, `KursWaluty`), odczyt; kurs przy zapisie i po zmianie daty; awaria NBP; korekta z kursem faktury korygowanej; należności i eksport bez sumowania walut; E2E |
| Import sprzedaży z KSeF | `IssuedImportTests` | dane z XML (nabywca, adres, termin, pozycje, kwoty); tylko do odczytu, rozliczenia; brak duplikatów (ponowny import, faktura wysłana z aplikacji rozpoznana po skrócie); ten sam numer z innego programu; korekta łączona z fakturą; pomijanie nieobsługiwanych rodzajów; limit czasu, izolacja; brak odpytywania o UPO; E2E |
| Aplikacja mobilna (PWA) | `HostingTests` (PWA), E2E `Pwa_…` | manifest (typ, tryb, ikony PNG, ikona maskable), service worker i rejestracja; produkcyjny service worker pomija `/api/`; E2E: rejestracja service workera, ekran telefonu (menu, formularz faktury bez poziomego przewijania); ręcznie: opublikowana wersja - 70 plików w pamięci podręcznej, 0 odpowiedzi API |
| Role w firmie | `CompanyRoleTests` | księgowa: odczyt, eksport, płatności, 403 przy wystawianiu i zmianach; pracownik: wystawianie i wysyłka, 403 przy ustawieniach i użytkownikach; zaproszenie: cudze konto, zgadywany, jednorazowy, odwołany, wygasły token, w bazie tylko skrót; wybór cudzej firmy nagłówkiem lub ciasteczkiem nie daje dostępu; biuro rachunkowe z własną firmą i firmą klienta; zmiana roli i odebranie dostępu; E2E: zaproszenie, przyjęcie, tryb księgowej |
| Utwardzenie (D47) | `SecurityHeadersTests`, `RateLimitTests`, `ApiRateLimitTests`, E2E `PolitykaCsp_…` | nagłówki (CSP, nosniff, DENY, no-referrer, Permissions-Policy) na stronach, API i plikach; brak skryptów w stronie; limit logowania z adresu (429, `Retry-After`); limit API na użytkownika; E2E - główne strony bez naruszeń CSP, kod QR jako obraz `data:` |
| Eksport dla księgowej | `CsvFormatTests`, `ExportApiTests` | format CSV (BOM, separator, ochrona przed formułami, kwoty ujemne); okres; tylko faktury przyjęte w KSeF i otrzymane w okresie; kwoty per stawka z XML; zawartość paczki ZIP (XML zgodny z wysłanym, UPO); izolacja |
| Wykaz podatników VAT (uzupełnienie) | `VatWhiteListClientTests` | strona zabezpieczenia przed botami (HTML) - czytelny komunikat |
| Wykaz podatników VAT | `WhiteListAddressTests`, `VatWhiteListClientTests`, `VatRegistryApiTests`, `VatRegistryLimitTests`, testy rachunku w `ReceivedInvoicesTests` | rozbiór adresu z wykazu; odczyt odpowiedzi i błędów API MF (wyszukiwanie, sprawdzenie rachunku); pamięć podręczna; limity dzienne; awaria MF; zapis wyniku sprawdzenia, rachunki zagraniczne, izolacja |
| Interfejs (E2E) | `UserScenariosE2ETests` | główne scenariusze użytkownika i regresje błędów interfejsu |
| Na żywo | `KsefLiveTests` | prawdziwy testowy KSeF: wysyłka, numer KSeF, UPO; faktura firmy A odebrana przez firmę B; faktura korygująca przyjęta przez KSeF; faktura oznaczona jako zapłacona przyjęta przez KSeF |

## Atrapa KSeF

`FakeKsefGateway` implementuje `IKsefGateway` bez sieci. Pozwala symulować przyjęcie, przetwarzanie i odrzucenie
faktury, awarie komunikacji, faktury otrzymane i HWM. Dzięki temu CI sprawdza wszystkie ścieżki, w tym te,
których nie da się łatwo wywołać na prawdziwym KSeF, np. awarię sieci dokładnie po przyjęciu faktury.

Analogicznie `FakeVatWhiteList` zastępuje API wykazu podatników VAT (podmioty, awaria, licznik zapytań do MF),
a `FakeEmailSender` serwer poczty (zapamiętuje wiadomości z załącznikami, symuluje awarię). Prawdziwa wysyłka
przez SMTP (MailKit) została sprawdzona ręcznie z lokalnym serwerem testowym; w środowisku deweloperskim pocztę
przechwytuje Mailpit.

**Zrzuty ekranu do dokumentacji** powstają w testach E2E: `E2E_SCREENSHOTS_DIR=docs/zrzuty` zapisuje je
w trakcie scenariuszy (bez tej zmiennej testy nie robią zrzutów).

## Błędy wykryte przez testy

| Błąd | Wykryty przez | Opis | Poprawka |
|---|---|---|---|
| Brak middleware autoryzacji | testy integracyjne (etap 0) | endpointy wymagające logowania zwracały 500 | dodanie `UseAuthorization` |
| Testy usuwały bazę deweloperską | obserwacja podczas testów (etap 0) | konfiguracja odczytywana przy starcie, przed nadpisaniem przez testy | leniwy odczyt konfiguracji i bezpiecznik: usuwanie tylko baz `ksef_test_*` |
| Walidacja ilości w kulturze pl-PL (D20) | test E2E w przeglądarce (etap 4) | `[Range]` parsował granice według kultury, w przeglądarce rzucał wyjątek | parsowanie niezależne od kultury i test regresyjny |
| „Nieklikalny” przycisk Zapisz (D22) | test E2E w przeglądarce (etap 5) | komunikat błędu znikał w chwili kliknięcia i przesuwał przycisk | walidacja podczas pisania (`InputTextLive`) |
| Numer KSeF atrapy niezgodny z formatem KSeF | walidacja XSD korekty (funkcja „Faktury korygujące”) | atrapa nadawała numery `…-FAKTURA-1-00`, a XML korekty zawiera numer faktury korygowanej (typ `TNumerKSeF`) | atrapa nadaje numery w formacie prawdziwego KSeF |
| Przycisk „Pobierz z wykazu VAT” nad górnym paskiem | zrzut ekranu z testu E2E (funkcja „Katalog produktów”) | przy przewijaniu przycisk grupy pól (z-index Bootstrapa) nachodził na przyklejony pasek z adresem e-mail | wyższy `z-index` paska |
| Niewidoczne znaczniki stanu („wysłano”, „w fakturze”) | zrzut ekranu z testu E2E (funkcja „Przypomnienia o płatności”) | klasy `text-bg-*` pochodzą z Bootstrapa 5.2, a projekt używa 5.1 - biały tekst bez tła; test sprawdzał tylko treść, nie wygląd | klasy `bg-*` zgodne z 5.1 |
| Paski należności nieproporcjonalne do kwot | zrzut ekranu z testu E2E (funkcja „Pulpit z wykresami”) | etykieta kwoty w tym samym elemencie elastycznym skracała pasek - różne kwoty miały równe paski | pasek i kwota w osobnych kolumnach siatki |
| Zawijanie numeru KSeF pod kodem QR | test treści PDF (etap 8) | numer KSeF dzielony na dwie linie | szersza kolumna oznaczenia |

Wniosek: testy API nie wystarczają. Dwa z błędów (D20, D22) były widoczne wyłącznie w prawdziwej przeglądarce,
co uzasadnia automatyczne testy E2E w CI.

## Wyniki testów na żywo (testowy KSeF, 1.11.2026)

| Scenariusz | Wynik |
|---|---|
| Wysyłka faktury z generatora aplikacji | przyjęta, numer KSeF i UPO w ok. 7 s |
| Wysyłka z aplikacji (przeglądarka, usługa w tle) | numer KSeF i UPO ok. 10 s od kliknięcia „Wyślij” |
| Zgodność skrótu dokumentu (kod QR) z UPO | identyczny dla wszystkich sprawdzonych faktur |
| Odbiór faktury firmy A przez firmę B | faktura dostępna do pobrania ok. 2 minuty po nadaniu numeru KSeF |
| Faktura korygująca (KOR) do faktury przyjętej w KSeF | przyjęta, numer KSeF; cały test (faktura + korekta) ok. 9 s |
| Faktura oznaczona jako zapłacona (`Zaplacono`, `DataZaplaty`), 5.11.2026 | przyjęta, numer KSeF; ok. 6 s |
| Faktura w EUR z kursem pobranym na żywo z API NBP (`P_14_1W`, `KursWaluty`), 6.11.2026 | przyjęta, numer KSeF |
| Kurs EUR z API NBP (`api.nbp.pl`), 6.11.2026 | kurs z 5.11.2026, tabela 216/A/NBP/2026 |
| Wykaz podatników VAT (`wl-api.mf.gov.pl`) z adresu w chmurze, 6.11.2026 | **zablokowane** przez zabezpieczenie MF przed botami (HTML zamiast JSON) - aplikacja pokazuje komunikat; do sprawdzenia z adresu docelowego serwera |
