# Raport: Obszar 07 - testy, odporność na błędy i ogólna jakość kodu
Status: ZAKOŃCZONY
Ostatnia aktualizacja: 2026-11-12 (po wznowieniu; komplet)

## Postęp
- [x] Kontekst: 00-zasady.md, 07-testy-i-jakosc.md, docs/testy.md, raporty obszarów 01, 02, 03, 05 (ID znalezisk)
- [x] Infrastruktura testów: ApiFactory, TestUsers, atrapy (FakeKsefGateway, FakeNbpClient, FakeVatWhiteList, FakeEmailSender), E2EFixture
- [x] CI: .github/workflows/ci.yml
- [x] Jakość testów: klasy testów integracyjnych i jednostkowych (asercje, kruchość, czas, Task.Delay)
- [x] Jakość testów E2E: UserScenariosE2ETests (selektory, oczekiwania)
- [x] Mapa pokrycia ryzyk (tabela)
- [x] Obsługa błędów w kodzie produkcyjnym (catch, async void, .Result/.Wait, CancellationToken)
- [x] Jakość ogólna: martwy kod, duplikaty, appsettings vs kod
- [x] Build: liczba i rodzaje ostrzeżeń (czysty build w kopii poza repozytorium)
- [x] Uruchomienie `dotnet test` w repozytorium
- [x] Uruchomienie E2E (`E2E_TESTS=1`)
- [x] Podsumowanie, sekcje końcowe
Następny krok: brak - przegląd zakończony.

## Podsumowanie

Zestaw testów jest szeroki i w dobrym stanie: 323 przypadki, wszystkie uruchomione przeszły (290 zwykłych + 26 E2E),
kod własny buduje się bez ani jednego ostrzeżenia, w kodzie produkcyjnym nie ma `async void`, `.Result`/`.Wait()` ani
połykanych wyjątków. Najpoważniejszy problem to **zależność testów od zegara systemowego przy stałych datach w danych
testowych**: bez żadnej zmiany kodu 1 test zacznie zawodzić 16.11.2026, a kolejnych 6 - 29.11.2026 (potwierdzone
uruchomieniem z przesuniętym zegarem serwera). Przyczyną jest brak sterowania czasem w testach integracyjnych
(`ApiFactory` nie podmienia `TimeProvider`), przez co reguły czasowe są testowane obejściami albo wcale.
Drugie ryzyko to obsługa błędów usług zewnętrznych: odpowiedź NBP niebędąca JSON-em oraz niedostępność KSeF przy
zapisie tokenu kończą się błędem 500. Trzecie: atrapa KSeF i konfiguracja testów (środowisko Development, limity i pętle
usług w tle wyłączone) nie pozwalają wykryć większości błędów zgłoszonych w obszarach 02, 03 i 05 - mapa pokrycia
poniżej pokazuje, że współbieżność i ścieżki „drugiej instancji” mają dokładnie jeden test.

Liczba znalezisk: krytyczne 0, wysokie 1, średnie 5, niskie 6.

## Wynik uruchomienia testów (zadanie 1)

| Przebieg | Polecenie | Wynik | Czas |
|---|---|---|---|
| Jednostkowe + integracyjne (repozytorium, Debug) | `dotnet test` | **290 zaliczonych, 0 niezaliczonych, 33 pominięte** (26 E2E, 4 `KsefLiveTests`, 3 `ExternalApiLiveTests`), razem 323 | 27 s (36 s z budowaniem przyrostowym) |
| E2E (repozytorium) | `E2E_TESTS=1 E2E_CHROMIUM_PATH=… dotnet test --no-build --filter FullyQualifiedName~E2E` | **26 zaliczonych, 0 niezaliczonych** | 3 min 1 s |
| Testy na żywo (`KSEF_LIVE_TESTS`) | nie uruchamiane (zgodnie z zasadami) | 7 pominiętych | - |

Uwagi: przebiegi wykonano 12.11.2026, gdy w tym samym katalogu działała aplikacja i przeglądarka innego wykonawcy -
bez kolizji (build przyrostowy, losowy port Kestrela, osobne bazy `ksef_test_*`). E2E uruchomiono filtrem, żeby nie
powtarzać 290 testów; zestaw jest ten sam, co w CI.

Najwolniejsze testy zwykłe: `DemoDataTests.Faktury_ZgodneZFa3_…` 4,7 s, `IssuedImportTests.RecznyImport_…` 2,1 s,
`PaymentApiTests.FakturyOtrzymane_…` 2,0 s; klasy: `DemoDataTests` 6,0 s, `PaymentApiTests` 5,7 s, `IssuedImportTests` 4,6 s,
`CompanyRoleTests` 4,0 s. Suma czasów 59 s przy 27 s zegarowych - równoległość klas działa, 27 klas tworzy własną bazę
przez migracje i nie jest to wąskie gardło. Najwolniejsze E2E: `PulpitZWykresami_…` 18,2 s, `PrzypomnienieOPlatnosci_…`
14,5 s, `PolitykaCsp_…` 13,2 s, `FakturaKorygujaca_…` 12,6 s; całość 181 s sekwencyjnie w jednej kolekcji (zob. TST-9).

## Ostrzeżenia kompilatora (zadanie 5)

Czysty build rozwiązania (`dotnet build FakturowanieKSeF.sln -c Debug` w kopii roboczej poza repozytorium, bez `bin/obj`):
**223 ostrzeżenia, 0 błędów; wszystkie 223 pochodzą z submodułu `external/ksef-client-csharp`**:

| Źródło | Liczba | Rodzaje |
|---|---|---|
| Projekty własne (Api, Client, Contracts, Ksef, Models, Tests, tools/KsefSpike) | **0** | - |
| Submoduł MF - kompilator/analizatory | 191 | CS8625 (92), CS8600 (34), CS8604 (21), CS8603 (8), CS8601 (7), CS8602 (4), CS9107 (3), CS8002 (2), CS8765, CS1574, CA1805 (6), CA1852 (5), CA1305 (4), CA1816, CA1510, IDE0011 |
| Submoduł MF - NuGet (pominięte, obszar 01) | 32 | NU1903/NU1902 (`System.Security.Cryptography.Xml`, `…Pkcs`) |

Wniosek: skoro kod własny ma 0 ostrzeżeń, można bezpiecznie włączyć `TreatWarningsAsErrors` dla projektów własnych
(np. w `Directory.Build.props` z wyłączeniem `external/`), żeby stan się nie pogorszył (zob. TST-10).

## Mapa pokrycia ryzyk (zadanie 2)

Legenda: **jest** - reguła ma test, który zawiedzie po jej złamaniu; **częściowo** - test istnieje, ale nie obejmuje
wskazanej ścieżki; **brak** - nie ma testu. W kolumnie „Luka” ID znalezisk z zakończonych obszarów, które ujawniły
błąd niewykryty przez testy (tam też opis testu do dopisania).

| Reguła | Test, który ją sprawdza | Stan | Luka (czego test nie obejmuje) |
|---|---|---|---|
| Izolacja danych między firmami | `DataIsolationTests.UzytkownikB_NieMaDostepuDoDanychUzytkownikaA`, `…DoKataloguUzytkownikaA`, testy `UzytkownikB_*` w 14 klasach (KSeF, PDF, płatności, e-mail, eksport, pulpit, cykliczne, proformy, korekty, import, przypomnienia, otrzymane), `DemoDataTests.Izolacja_…`, E2E `UzytkownikB_NieWidziFakturyUzytkownikaA_PoAdresieStrony` | jest | ISO-7 (`/company/members/{id}`, zaproszenia, `/received-invoices/{id}/pdf`), ISO-6; brak testu konwencji „każdy endpoint `/api` wymaga logowania i filtra firmy” (TST-6) |
| Wybór firmy tylko spośród dostępnych | `CompanyRoleTests.WyborFirmy_TylkoSposrodDostepnych_…`, `BiuroRachunkowe_…`, `OdebranieDostepu_KonczyDostepDoDanych` | częściowo | ISO-3, ISO-5 |
| Role (właściciel / pracownik / księgowa) | `CompanyRoleTests.Ksiegowa_…`, `Pracownik_…`, `Zaproszenie_…`, `DemoDataTests.BiuroRachunkowe_…`, E2E `DostepKsiegowej_…` | częściowo | ISO-4, ISO-7 (kilkanaście endpointów zmieniających dane bez asercji 403) |
| Maszyna stanów wysyłki do KSeF | `KsefSendingTests.PelnyPrzeplyw_…`, `FakturaOdrzuconaPrzezKsef_…`, `AwariaSieciPrzedWyslaniem_…`, `AwariaSieciPoWyslaniu_NigdyNieOznaczaFakturyJakoOdrzuconej`, `Wysylka_BezTokenuKsef_Zwraca409` | częściowo | KSEF-1, KSEF-3, KSEF-4, KSEF-5, KSEF-12, KSEF-15; ISO-1; brak testu stanu „UPO jeszcze niegotowe” (`UpoReady=false` nie jest używane w żadnym teście) |
| Brak duplikatów wysyłki | `KsefSendingTests.PodwojneKlikniecieWyslij_…` (jedyny test współbieżności w projekcie), `PaymentApiTests.ZaplaconoPrzyWystawieniu_PonownaWysylkaPoOdrzuceniu_NieDublujePlatnosci`, `IssuedImportTests.FakturaWyslanaZAplikacji_NieJestImportowanaPonownie_…` | częściowo | KSEF-1 (utracona odpowiedź po przyjęciu), KSEF-2 (dwa procesory) - atrapa nie potrafi tego zasymulować (TST-4) |
| Odbiór „do HWM”, budżet, deduplikacja | `ReceivedInvoicesTests.Synchronizacja_…DoHwm`, `FakturyPozaHwm_…`, `LimitPobranNaSynchronizacje_…`, `BladPobierania_…`, `RecznaSynchronizacja_…OgraniczonaCzestotliwoscia` | częściowo | stronicowanie (`HasMore`, `MaxPagesPerSync`) i `IsTruncated` nigdy nie są wykonywane (testy publikują najwyżej 45 faktur przy stronie 100, atrapa zawsze zwraca `IsTruncated=false`); KSEF-6, KSEF-7, KSEF-8, KSEF-9; od 29.11.2026 testy tej klasy zawodzą (TST-1) |
| Kwoty VAT i zaokrąglenia | `InvoiceCalculatorTests` (4), `VatRateTests`, `Fa3XmlGeneratorTests.FakturaB2B_…`, `WszystkieObslugiwaneStawki_…`, `InvoiceApiTests.UtworzenieFaktury_…PrzeliczaKwoty`, `ExportApiTests.ZestawieniaCsv_…` | jest | brak testu własności „suma w bazie = suma w XML = suma w PDF/CSV” dla ilości i cen z wieloma miejscami po przecinku (zob. kandydat FAK-B w raporcie obszaru 04, w toku) |
| Korekty | `CorrectionCalculatorTests` (2), `CorrectionXmlTests` (3), `CorrectionApiTests` (5), E2E `FakturaKorygujaca_…` | częściowo | brak testu kolejności i równoległości korekt (kandydaci FAK-A, FAK-D obszaru 04), PERF-12 |
| Waluty i kurs NBP | `ExchangeRateLookupTests` (3), `CurrencyXmlTests`, `CurrencyApiTests` (3), E2E `FakturaWEuro_…` | częściowo | odpowiedź NBP inna niż JSON (TST-3), SEC-6, KSEF-13, data sprzedaży w przyszłości (kandydat FAK-H obszaru 04) |
| Numeracja | `InvoiceCalculatorTests.NumerFaktury_MaFormatFvNumerMiesiacRok`, `InvoiceApiTests.UtworzenieFaktury_NadajeNumer_…` (FV/1, FV/2), `DataModelTests.NumerFaktury_UnikalnyWRamachFirmy_…`, `NumerUsunietegoSzkicu_…`, `CorrectionApiTests` (KOR/1, KOR/2), `ProformaTests` (PRO/1) | częściowo | PERF-13 (równoległe nadawanie numeru - brak testu); brak testu przejścia miesiąca/roku (FV/1/12 po FV/n/11) i `next-number` |
| Proforma → faktura VAT (jednokrotność) | `ProformaTests.ZamianaNaFaktureVat_RazZPowiazaniem_…` (druga zamiana sekwencyjnie → 409) | częściowo | PERF-7 (równolegle) |
| Płatności (nadpłata, stany) | `PaymentRulesTests` (2), `PaymentApiTests` (8) | częściowo | PERF-6 (równoległe wpłaty); od 16.11.2026 `Filtry_PoTerminie_…` zawodzi (TST-1) |
| 2FA | `TwoFactorTests` (4), E2E `LogowanieDwuskladnikowe_…` | częściowo | SEC-1 (brak limitu prób - brak testu), brak testu ponownego użycia tego samego kodu TOTP |
| Limity żądań | `RateLimitTests.Logowanie_PoPrzekroczeniuLimituZAdresu_429ZRetryAfter`, `ApiRateLimitTests.Api_LimitNaUzytkownika`, `VatRegistryLimitTests`, `EmailApiTests.DziennyLimitFirmy_…`, limity „raz na minutę” w `ReceivedInvoicesTests`/`IssuedImportTests` | częściowo | SEC-3, SEC-4, SEC-5, SEC-7, KSEF-8, PERF-14; żaden test nie sprawdza, że limity są domyślnie WŁĄCZONE (wszystkie pozostałe testy mają `RateLimiting:Enabled=false`) ani upływu okna (brak sterowania czasem, TST-2) |
| CSP i nagłówki | `SecurityHeadersTests` (6 przypadków), E2E `PolitykaCsp_BezNaruszenNaGlownychStronach` | jest | SEC-11 (HSTS - nietestowane, bo testy działają w Development, TST-5) |
| Blokada produkcyjnego KSeF | `KsefEnvironmentGuardTests` (4), `KsefClientRegistrationTests` | jest | KSEF-17 (przekierowania) |
| Token KSeF zaszyfrowany, nie wraca w API | `KsefSettingsTests.PoprawnyToken_JestZapisywanyZaszyfrowany_INieWracaDoKlienta`, `CompanyNipChangeTests` | jest | brak testu, że token nie trafia do logów |
| XML zawsze po walidacji XSD | `Fa3XmlGeneratorTests` (12), `Fa3SchemaValidatorTests` (4, w tym XXE/DTD), `InvoiceXmlApiTests`, `KsefSendingTests.PelnyPrzeplyw_…` (waliduje `SentXml`) | jest | - |
| Usługi w tle (pętle `BackgroundService`) | E2E uruchamia tylko `KsefSubmissionWorker` i `EmailWorker`; logika wywoływana jawnie (`ProcessKsefAsync`, `ProcessEmailsAsync`, `RunAsync`) | częściowo | pętle `ReceivedInvoicesWorker`, `RecurringInvoiceWorker`, `PaymentReminderWorker` nie są wykonywane w żadnym teście - tam leżą ISO-2, KSEF-6, KSEF-14 (TST-5) |
| Prawdziwa bramka `KsefGateway`, `SmtpEmailSender` | tylko testy na żywo (poza CI) i `CompanyNipChangeTests.OdrzucenieTokenuPrzezKsef_RozpoznaneZKomunikatuKlientaMf` | brak w CI | KSEF-11, KSEF-18 (propozycje testów jednostkowych bramki z atrapą klienta MF) |
| Ścieżki produkcyjne (HTTPS, `Secure`, HSTS, proxy, blokada hasła) | zadanie CI `docker-image` (tylko blokada hasła deweloperskiego) | brak | TST-5 |

## Znaleziska

### TST-1. Testy z datami na sztywno zaczną zawodzić 16.11.2026 i 29.11.2026 („bomba zegarowa”)
- **Waga:** wysokie
- **Rodzaj:** błąd (testy)
- **Pewność:** potwierdzone - uruchomienie zestawu w kopii roboczej z zegarem serwera przesuniętym o +16 i +18 dni
  (tymczasowy `TimeProvider` w `ApiFactory`, usunięty po sprawdzeniu): przy +16 dniach zawodzi 1 test, przy +18 - 7 testów.
- **Miejsce:** `FakturowanieKSeF.Tests/Infrastructure/TestUsers.cs:86-87` (stałe `IssueDate` 2026-11-01, `DueDate` 2026-11-15);
  `FakturowanieKSeF.Tests/PaymentTests.cs:143-167`; `FakturowanieKSeF.Tests/ReceivedInvoicesTests.cs:67` (`T0 = 2026-10-30 08:00 UTC`)
  i testy używające `Publish(nip, n, T0)`: linie 123, 147, 181, 199, 224, 242; `FakturowanieKSeF.Api/Services/ReceivedInvoiceSync.cs:43-45`
  (pierwsza synchronizacja: `now - 30 dni`); `FakturowanieKSeF.Tests/Infrastructure/ApiFactory.cs:84-90` (brak podmiany `TimeProvider`).
- **Problem:** serwer w testach używa prawdziwego zegara (`TimeProvider.System`), a dane testowe mają daty stałe. Faktura
  „bieżąca, w terminie” ma termin 15.11.2026, a faktury „opublikowane w KSeF” - datę 30.10.2026, która po 30 dniach wypada
  z okna pierwszej synchronizacji.
- **Scenariusz:** (1) od 16.11.2026: `PaymentApiTests.Filtry_PoTerminie_Nieoplacone_Oplacone_IPodsumowanieNaleznosci` -
  `Ids("Overdue")` zwraca `[1, 2]` zamiast `[1]` (zaobserwowane), bo faktura „bieżąca” staje się przeterminowana;
  (2) od 29.11.2026 08:00 UTC: 6 testów `ReceivedInvoicesTests` (`Synchronizacja_…DoHwm` - `Expected: 2, Actual: 0`,
  `LimitPobranNaSynchronizacje_…` - `Expected: 40, Actual: 0`, `SzczegolyIXml_…`, `BladPobierania_…`, `RecznaSynchronizacja_…`,
  `UzytkownikB_NieWidziFakturOtrzymanych…`), bo atrapa filtruje `PermanentStorageDate >= from`. CI staje się czerwone
  na każdej gałęzi, bez zmiany kodu - w okresie oddawania pracy.
- **Propozycja poprawki:** daty w danych testowych liczyć względem „dziś” (`Today.AddDays(14)`, `UtcNow.AddDays(-2)`)
  albo - lepiej - zarejestrować w `ApiFactory` `FakeTimeProvider` (pakiet `Microsoft.Extensions.TimeProvider.Testing`)
  ustawiony na stałą chwilę, np. 2026-11-10 12:00 czasu polskiego, i z niego brać „dziś” także w testach (TST-2).
- **Test, który to wykryje:** po wprowadzeniu `FakeTimeProvider` - `PaymentApiTests.Filtry_…` w dwóch wariantach
  (`[Theory]`: zegar 2026-11-10 i 2027-06-01) oraz jednorazowe zadanie CI uruchamiające zestaw z zegarem przesuniętym
  o +90 i +400 dni (zmienna środowiskowa odczytywana przez `ApiFactory`); oczekiwany wynik: komplet zaliczony.

### TST-2. Brak sterowania czasem w testach: reguły czasowe testowane obejściami, mieszanie dnia UTC z dniem polskim
- **Waga:** średnie
- **Rodzaj:** błąd (testy kruche) / luka w testach
- **Pewność:** potwierdzone lekturą kodu; skutki pośrednie widoczne w przebiegach z przesuniętym zegarem (przy +5 dniach
  zawodzi 9 testów, przy +60 - 25, większość dlatego, że test liczy „dziś” z `DateTime.UtcNow`, a serwer z `TimeProvider`).
- **Miejsce:** `FakturowanieKSeF.Tests/Infrastructure/ApiFactory.cs:84-90`; obejścia przez UPDATE w bazie:
  `ReminderTests.cs:124-131` („po 7 dniach”), `EmailTests.cs:165` (ponowienie), `CompanyRoleTests.cs:122` (wygaśnięcie zaproszenia);
  dzień UTC zamiast polskiego: `ProformaTests.cs:67`, `ReminderTests.cs:151-152`, `PaymentTests.cs:76`, `PaymentTests.cs:125`;
  E2E: `E2E/UserScenariosE2ETests.cs:467`, `:670`, `:704` (`DateTime.Today` procesu testów przy przeglądarce `Europe/Warsaw`
  i serwerze `PolishDate`).
- **Problem:** produkcyjny kod konsekwentnie przyjmuje `TimeProvider`, ale testy go nie wykorzystują. Skutki: (a) nie da się
  przetestować upływu czasu - odstępy ponowień wysyłki (KSEF-5), okno limitu „raz na minutę” (KSEF-8), wygaśnięcie bufora
  tokenu KSeF (KSEF-18), okno limitów żądań, pamięć podręczna NBP 12 h; (b) testy liczące „dziś” inaczej niż serwer są
  niestabilne w okolicach północy.
- **Scenariusz:** E2E `FakturaCykliczna_HarmonogramZFaktury_…` uruchomiony w CI (strefa UTC) między 23:00 a 24:00 UTC
  (zimą; latem 22:00-24:00): `#next-date` = dzisiejsza data UTC, czyli „wczoraj” według czasu polskiego → serwer odrzuca
  („data w przeszłości”, `RecurringInvoiceEndpoints.cs:49`) → brak przejścia na `/cykliczne` → test zawodzi po 30 s.
  Analogicznie `ProformaTests.ZamianaNaFaktureVat_…` i E2E eksportu w ostatnim dniu miesiąca.
- **Propozycja poprawki:** `FakeTimeProvider` w `ApiFactory` (właściwość `Time`), w testach `factory.Time.Advance(TimeSpan.FromDays(8))`
  zamiast modyfikacji `CreatedAt`; „dziś” w testach wyłącznie z `PolishDate.Today(factory.Time)`; w E2E data z przeglądarki
  (`page.EvaluateAsync("new Date().toLocaleDateString('sv-SE')")`) albo ustawienie zegara Playwright (`page.Clock`).
- **Test, który to wykryje:** `RecurringInvoiceApiTests.TerminLiczonyWedlugCzasuPolskiego` - zegar 2026-11-30 23:30 UTC,
  harmonogram z datą 2026-12-01 → `RunAsync` tworzy szkic; `PaymentReminderTests.KolejnePrzypomnienie_DopieroPo7Dniach`
  z `Advance(6 dni)` → brak, `Advance(1 dzień)` → jest.

### TST-3. Odpowiedź NBP inna niż JSON oraz niedostępny KSeF przy zapisie tokenu kończą się błędem 500
- **Waga:** średnie
- **Rodzaj:** błąd (obsługa wyjątków)
- **Pewność:** NBP - potwierdzone tymczasowym testem (usuniętym): `NbpClient.GetRatesAsync` dla odpowiedzi `200` z treścią HTML
  (zarówno `text/html`, jak i `application/json`) rzuca `System.Text.Json.JsonException`, a `POST /api/invoices` w EUR przy
  takim wyjątku klienta NBP zwraca `500`. KSeF - potwierdzone lekturą kodu (atrapa nie rzuca innych wyjątków niż
  `KsefAuthenticationException`, więc testem integracyjnym bez zmian w atrapie nie da się tego wywołać).
- **Miejsce:** `FakturowanieKSeF.Api/Integrations/Nbp/NbpExchangeRates.cs:49-52` (brak `catch` dla `JsonException`,
  `NotSupportedException`, `FormatException` z `DateOnly.ParseExact`); `FakturowanieKSeF.Api/Endpoints/InvoiceEndpoints.cs:544`
  i `ExchangeRateEndpoints.cs:22` (łapią tylko `NbpException`); `FakturowanieKSeF.Api/Endpoints/KsefSettingsEndpoints.cs:37`
  (`PUT /company/ksef/token` łapie tylko `KsefAuthenticationException`) i `:59` (`POST …/test-token` łapie
  `KsefAuthenticationException` i `HttpRequestException`, ale nie `TaskCanceledException`/`TimeoutException`/`KsefApiException`).
- **Problem:** błędy usług zewnętrznych, które powinny dać czytelne 4xx/502, trafiają do ogólnej obsługi wyjątków jako 500
  („wystąpił błąd”), a użytkownik nie wie, że wystarczy spróbować później. Klient wykazu VAT robi to poprawnie
  (`VatWhiteListClient.cs:89-125`) - testy `VatWhiteListClientTests.StronaZabezpieczeniaZamiastJson_…` mają odpowiednik tylko dla MF.
- **Scenariusz:** (1) NBP zwraca stronę błędu/zabezpieczenia w HTML (tak zachował się wykaz MF 6.11.2026 - `docs/testy.md`)
  → zapis każdej faktury walutowej i podgląd kursu → 500; harmonogram cykliczny w walucie dostaje nieobsłużony wyjątek
  (por. KSEF-13). (2) Testowy KSeF nie odpowiada w czasie → `PUT /api/company/ksef/token` → 500 zamiast „KSeF jest
  niedostępny, spróbuj ponownie”.
- **Propozycja poprawki:** w `NbpClient` objąć odczyt treści `catch (Exception e) when (e is JsonException or NotSupportedException or FormatException)`
  → `NbpException("Serwis kursów NBP zwrócił nieoczekiwaną odpowiedź.")`; w `KsefSettingsEndpoints` wspólny filtr wyjątków
  komunikacji (`HttpRequestException`, `TaskCanceledException` przy nieanulowanym `ct`, `TimeoutException`, `KsefApiException`)
  → `502` z komunikatem po polsku, bez `ex.Message` (por. SEC-8).
- **Test, który to wykryje:** `ExchangeRateLookupTests.KlientNbp_OdpowiedzHtml_ToNbpException` (StubHandler z HTML, oczekiwany
  `NbpException`); `CurrencyApiTests.NieoczekiwanaOdpowiedzNbp_Zwraca400ZKomunikatem` (`FakeNbpClient.Failure = new JsonException()`
  → 400, nie 500); `KsefSettingsTests.KsefNiedostepny_PrzyZapisieTokenu_Zwraca502` (nowe pole `FakeKsefGateway.VerifyFailure =
  new HttpRequestException()` → 502, token niezapisany).

### TST-4. Atrapa KSeF jest „zbyt uprzejma” - ukrywa klasy błędów znalezione w obszarze 03
- **Waga:** średnie
- **Rodzaj:** luka w testach (atrapa niezgodna z prawdziwą usługą)
- **Pewność:** potwierdzone lekturą kodu atrapy i porównaniem z `KsefGateway` oraz wynikami testów na żywo w `docs/testy.md`
- **Miejsce:** `FakturowanieKSeF.Tests/Infrastructure/FakeKsefGateway.cs`: `:57-65` (status), `:47-55` (wysyłka), `:104-112`
  (pobranie), `:40-45` (weryfikacja tokenu), `:73-83` i `:88-98` (zapytania), `:26-37` (kolekcje bez synchronizacji), `:117-118` (numer KSeF)
- **Problem:** różnice względem prawdziwego KSeF:
  1. status domyślnie od razu `200` z numerem KSeF - stan „w przetwarzaniu” występuje tylko w jednym teście; prawdziwy KSeF
     potrzebuje kilku sekund (`docs/testy.md`: ok. 7 s);
  2. `SendFailure` rzuca wyjątek **przed** zarejestrowaniem faktury - nie da się zasymulować „KSeF przyjął, odpowiedź
     przepadła” (KSEF-1), czyli najgroźniejszego scenariusza duplikatu;
  3. `DownloadInvoiceAsync` zwraca XML natychmiast - brak opóźnienia udostępnienia faktury (kod 21165; na żywo ok. 2 minut),
     brak błędu dla pojedynczej faktury (`DownloadFailure` dotyczy wszystkich - por. KSEF-7);
  4. brak symulacji `429`/limitów i liczenia wywołań per operacja (KSEF-9, KSEF-11, SEC-7);
  5. `VerifyTokenAsync` i `SendInvoiceAsync` ignorują NIP - token poprawny dla każdej firmy, więc scenariusz „token innego
     NIP” (status 450) istnieje tylko w teście tekstu komunikatu;
  6. `IsTruncated` zawsze `false`, a testy nigdy nie przekraczają jednej strony (45 faktur przy `PageSize = 100`) -
     gałęzie stronicowania w `ReceivedInvoiceSync.cs:56-112` i `IssuedInvoiceImport` nie są wykonywane;
  7. `GetUpoAsync` z `UpoReady = false` nie jest użyte w żadnym teście; `CloseSessionAsync` nigdy nie zawodzi;
  8. `SentInvoices`, `ClosedSessions`, `StatusResponses`, `Downloads`, `Received`/`Issued` to zwykłe kolekcje bez blokad,
     a w E2E usługa w tle wywołuje atrapę równolegle z testem (testy blokują tylko własne zapisy `Received`);
  9. wspólna lista `Queries` dla faktur otrzymanych i wystawionych; stała data `20261101` w numerze KSeF (kod QR i eksport
     nigdy nie widzą numeru z inną datą niż data wystawienia).
- **Scenariusz:** regresja w obsłudze statusu 150/„UPO niegotowe”/drugiej strony wyników przechodzi cały zestaw CI
  i wychodzi dopiero w ręcznym teście na żywo.
- **Propozycja poprawki:** rozbudować atrapę o sterowalne zachowania: `ProcessingPolls` (ile odpytań zwraca 150, domyślnie 1),
  `FailAfterAccept` (rejestruje fakturę i rzuca), `AvailabilityDelay`/`DownloadFailureFor(ksefNumber)`, `TokenNip` (token ważny
  dla jednego NIP), `Truncated`, liczniki wywołań per metoda, kolekcje współbieżne. Dodatkowo test kontraktowy uruchamiany
  na atrapie i (z `KSEF_LIVE_TESTS=1`) na prawdziwej bramce - te same asercje dla obu implementacji `IKsefGateway`.
- **Test, który to wykryje:** `ReceivedInvoicesTests.WieleStron_PobieraWszystkoWKolejnychPrzebiegach` (250 faktur, `PageSize` 100 →
  po kolejnych synchronizacjach 250 pobranych, bez duplikatów, punkt kontrolny = HWM); `KsefSendingTests.UpoNiegotowe_FakturaPrzyjeta_UpoPobraneWKolejnymPrzebiegu`;
  `ReceivedInvoicesTests.FakturaJeszczeNiedostepna_21165_PobranaPozniej_BezPrzesunieciaPunktuKontrolnego`; pozostałe - testy
  zaproponowane przy KSEF-1, KSEF-2, KSEF-7, KSEF-9.

### TST-5. Testy działają wyłącznie w konfiguracji deweloperskiej: ścieżki produkcyjne i pętle usług w tle bez testów
- **Waga:** średnie
- **Rodzaj:** luka w testach
- **Pewność:** potwierdzone lekturą `ApiFactory`, `Program.cs` i listy testów
- **Miejsce:** `FakturowanieKSeF.Tests/Infrastructure/ApiFactory.cs:62-78` (`UseEnvironment("Development")`, wyłączone
  `Ksef:Worker`, `Ksef:Receive`, `Email:Worker`, `Reminders:Worker`, `Recurring:Worker`, `RateLimiting`);
  `FakturowanieKSeF.Api/Program.cs:117-133`, `:151-155`, `:176-180`, `:204-209`; `FakturowanieKSeF.Tests/AuthTests.cs:77-95`;
  pętle: `ReceivedInvoiceSync.cs:161-205`, `RecurringInvoices.cs:153-183`, `Email/PaymentReminders.cs:90-120`
- **Problem:** (a) nic nie sprawdza zachowania w środowisku `Production`: `UseHsts`, przekierowanie HTTPS, flaga `Secure`
  ciasteczka sesji (test nazywa się „…UstawiaBezpieczneCiasteczko…”, ale sprawdza tylko `HttpOnly` i `SameSite`), nagłówki
  `X-Forwarded-*` za proxy (od nich zależy adres IP w limitach logowania), odmowa `DemoData:Seed` poza Development, brak Swaggera;
  jedyny test „produkcyjny” to krok `docker-image` w CI (blokada hasła deweloperskiego). (b) Pętle trzech usług w tle
  (`ReceivedInvoicesWorker` z importem sprzedaży, `RecurringInvoiceWorker`, `PaymentReminderWorker`) nie są wykonywane
  w żadnym teście - a to w nich są błędy ISO-2, KSEF-6 i KSEF-14 (wyjątek jednej firmy przerywa przebieg dla wszystkich).
- **Scenariusz:** zmiana kolejności middleware albo opcji ciasteczka powoduje, że produkcyjnie ciasteczko sesji nie ma `Secure`
  albo limit logowania liczy adres proxy zamiast klienta (wszyscy użytkownicy we wspólnym limicie) - zestaw testów zielony.
- **Propozycja poprawki:** `ProductionApiFactory : ApiFactory` (`UseEnvironment("Production")`, `ReverseProxy:Enabled=true`,
  hasło bazy inne niż deweloperskie) i klasa `ProductionHostingTests`; testy pętli usług w tle z `FakeTimeProvider`
  i krótkim interwałem albo wydzielenie jednego „przebiegu dla wszystkich firm” do metody wywoływanej z testu.
- **Test, który to wykryje:** `ProductionHostingTests.CiasteczkoSesji_MaSecure_PrzyXForwardedProtoHttps`,
  `…Hsts_JestWysylany` (por. SEC-11), `…Swagger_NieJestDostepny` (404), `…DemoDataSeed_PozaDevelopment_OdmowaStartu`,
  `RateLimitTests.LimitLogowania_LiczonyPoAdresieZXForwardedFor` (dwa adresy klienta przez to samo proxy → osobne limity);
  `ReceivedInvoicesWorkerTests.BladJednejFirmy_NiePrzerywaSynchronizacjiPozostalych` (por. KSEF-6).

### TST-6. Brak testów współbieżności i testu konwencji endpointów - błędy z obszarów 02, 03, 05 przechodzą zestaw
- **Waga:** średnie
- **Rodzaj:** luka w testach
- **Pewność:** potwierdzone - w całym projekcie testów jest jedno `Task.WhenAll` (`KsefSendingTests.cs:242`) i żadnego
  użycia `EndpointDataSource`
- **Miejsce:** `FakturowanieKSeF.Tests/KsefSendingTests.cs:236-248` (jedyny test równoległy); brak pliku z testem konwencji
- **Problem:** aplikacja jest wieloużytkownikowa i ma pięć usług w tle, a wszystkie wyścigi wskazane w zakończonych obszarach
  (PERF-6 wpłaty, PERF-7 proforma, PERF-13 numeracja, PERF-14 i SEC-4 limit e-mail, KSEF-2 dwa procesory, KSEF-8 limit
  ręcznej synchronizacji) są dla zestawu niewidoczne. Analogicznie reguła z `CLAUDE.md` „nowy endpoint zmieniający dane =
  filtr roli i przypadek w testach” jest pilnowana tylko ręcznie (ISO-4, ISO-7).
- **Scenariusz:** nowy endpoint `POST /api/…` dodany poza grupą z `RequireRoleForChanges` albo bez `RequireAuthorization`
  przechodzi CI; równoległe żądania tworzą duplikaty/nadpłaty, których testy sekwencyjne nie pokażą.
- **Propozycja poprawki:** (1) pomocnik `Concurrently(n, action)` i po jednym teście na każdy z wymienionych wyścigów
  (nazwy i oczekiwania w raportach 03 i 05 - nie powtarzam); (2) test konwencji oparty na metadanych endpointów.
- **Test, który to wykryje:** `EndpointConventionTests.KazdyEndpointApi_WymagaLogowania_AZmieniajacyDane_MaFiltrRoli` - warstwa
  integracyjna, pobiera `EndpointDataSource` z `factory.Services`, dla każdego `RouteEndpoint` pod `/api` (poza jawną listą
  wyjątków: `/api/auth/*`, `/api/health`, podgląd zaproszenia) sprawdza obecność `IAuthorizeData`, a dla metod
  POST/PUT/DELETE - metadanych filtra roli; oczekiwany wynik: pusta lista naruszeń, wyjątki wypisane w teście z uzasadnieniem.

### TST-7. Testy, które przechodzą niezależnie od sprawdzanego zachowania (niesprawdzony krok przygotowania)
- **Waga:** niskie
- **Rodzaj:** błąd (testy)
- **Pewność:** potwierdzone lekturą kodu
- **Miejsce:** `FakturowanieKSeF.Tests/AuthTests.cs:48`; `FakturowanieKSeF.Tests/ProductTests.cs:78-79`;
  `FakturowanieKSeF.Tests/PaymentTests.cs:150`; (mniej groźne: `AuthTests.cs:81`, `CompanyAndCustomerApiTests.cs:111`)
- **Problem:** wynik żądania przygotowującego nie jest sprawdzany, a asercja końcowa jest prawdziwa także wtedy, gdy to
  żądanie się nie powiodło.
- **Scenariusz:** `Logowanie_ZlymHaslem_Zwraca401` - gdy rejestracja zwróci 400/429, logowanie i tak da 401 → zielono, choć
  test nie sprawdził złego hasła istniejącego konta. `ZmianaKatalogu_NieZmieniaFaktury` - gdy `PUT`/`DELETE` produktu
  zwrócą 4xx, faktura oczywiście się nie zmieni → zielono. `Filtry_PoTerminie_…` - gdy utworzenie szkicu się nie powiedzie,
  asercja „szkic nie jest należnością” jest pusta.
- **Propozycja poprawki:** `EnsureSuccessStatusCode()` (albo asercja kodu) przy każdym kroku przygotowania; w `ProductTests`
  dodatkowo asercja, że produkt faktycznie ma nową cenę / został usunięty.
- **Test, który to wykryje:** nie dotyczy (poprawka istniejących testów); pomocniczo analizator `xUnit`/reguła przeglądu:
  żadne `await client.Post…(…);` bez użycia wyniku.

### TST-8. Wspólna baza i wspólna atrapa na klasę testów - wynik zależy od kolejności testów w klasie
- **Waga:** niskie
- **Rodzaj:** błąd (testy kruche)
- **Pewność:** potwierdzone lekturą kodu; w obecnej kolejności testy przechodzą
- **Miejsce:** `FakturowanieKSeF.Tests/Infrastructure/ApiFactory.cs:42-46` (`ProcessKsefAsync` przetwarza wysyłki wszystkich firm
  w bazie klasy); `FakturowanieKSeF.Tests/KsefSendingTests.cs:152-157` (jedna kolejka `StatusResponses`), `:236-248` (test zostawia
  wysyłkę w stanie `Pending`), `:183-207` i `:210-233` (`SendFailure`/`StatusFailure` globalne dla atrapy); `ReceivedInvoicesTests`
  (`factory.Ksef.Hwm` wspólne)
- **Problem:** `PodwojneKlikniecieWyslij_…` zostawia nieprzetworzoną wysyłkę; następny test z kolejką odpowiedzi (150, 450)
  dzieli ją z cudzą wysyłką - dziś wynik jest ten sam tylko dlatego, że procesor bierze wysyłki w kolejności identyfikatorów.
  Każdy nowy test wstawiający odpowiedzi do kolejki albo liczący `SentInvoices`/`Queries` bez filtra po NIP będzie niestabilny.
  Wszystkie firmy `CreateClientWithCompanyAsync()` mają też ten sam NIP `5260250274`, więc filtr po NIP nie rozróżnia testów.
- **Scenariusz:** dopisanie testu „pierwsze odpytanie 150, drugie 200” obok istniejących - zależnie od kolejności uruchomienia
  odpowiedź 150 zużywa wysyłka pozostawiona przez inny test i asercja liczby przebiegów zawodzi losowo.
- **Propozycja poprawki:** odpowiedzi atrapy kluczowane numerem referencyjnym/NIP (`StatusResponsesFor(nip)`), losowy NIP
  domyślnie w `CreateClientWithCompanyAsync`, `ProcessKsefAsync(companyNip)` do testów albo sprzątanie wysyłek `Pending`
  na końcu testu.
- **Test, który to wykryje:** uruchomienie zestawu z losową kolejnością testów w klasie (własny `ITestCaseOrderer` z ziarnem
  wypisywanym w logu) jako dodatkowe zadanie CI.

### TST-9. E2E: jedna kolekcja 26 scenariuszy (3 minuty sekwencyjnie) i część selektorów zależna od struktury i stylu
- **Waga:** niskie
- **Rodzaj:** wydajność (testów) / testy kruche
- **Pewność:** potwierdzone (czasy z pliku TRX, zliczenie selektorów)
- **Miejsce:** `FakturowanieKSeF.Tests/E2E/E2EFixture.cs:98-99` (jedna kolekcja „E2E”), `E2E/UserScenariosE2ETests.cs` (1044 linie,
  jedna klasa); selektory: `button[type=submit]` (37 użyć), `.alert-success`/`.alert-danger`, `.fs-5.text-end` (3), `tbody tr`,
  `.items-table tbody tr` (`:49`), `.invoice-sheet`, `path.viz-bar[d^='M']` (`:715`), `.viz-table tbody td`, kolor
  `rgb(230, 237, 240)` (`:724`); `WaitForTimeoutAsync(300)` (`:832`, tylko przed zrzutem ekranu)
- **Problem:** większość kroków używa `data-testid` (95 użyć), identyfikatorów pól i automatycznego czekania `Expect` - to jest
  dobre. Pozostałe selektory wiążą testy z klasami Bootstrapa i układem tabel (zmiana design systemu D50 = masowe poprawki),
  a `button[type=submit]` zakłada jeden formularz na stronie. Wszystkie scenariusze są niezależne (każdy rejestruje nowe konto),
  ale biegną jeden po drugim: 181 s z ok. 210 s całego CI testów.
- **Scenariusz:** dodanie drugiego formularza na stronie faktury (np. panel notatki) → `page.ClickAsync("button[type=submit]")`
  trafia w zły przycisk albo Playwright zgłasza niejednoznaczność w 37 miejscach.
- **Propozycja poprawki:** `data-testid` dla przycisku zapisu formularza, komunikatów (`alert-success` → `data-testid=saved`)
  i sumy brutto; podział scenariuszy na 3-4 klasy w osobnych kolekcjach, każda z własnym `E2EFixture` (aplikacja + Chromium
  startują ok. 5 s) - szacunkowo 3 min → ok. 1 min przy 4 rdzeniach CI; wspólne kroki do klasy pomocniczej.
- **Test, który to wykryje:** nie dotyczy (refaktoryzacja testów); kontrola: czas kroku „Test” w CI.

### TST-10. CI: brak limitu czasu, artefaktów diagnostycznych i pomiaru pokrycia; ostrzeżenia nie blokują
- **Waga:** niskie
- **Rodzaj:** uproszczenie / jakość procesu
- **Pewność:** potwierdzone lekturą `.github/workflows/ci.yml` i `FakturowanieKSeF.Tests.csproj`
- **Miejsce:** `.github/workflows/ci.yml:3-5` (`on: push` + `pull_request` - dwa przebiegi dla gałęzi z otwartym PR),
  `:8-9` i `:54-55` (brak `timeout-minutes`), `:48-51` (brak `--blame-hang-timeout`, loggera TRX i `upload-artifact`),
  `:31`, `:35`, `:57` (akcje przypięte do znacznika `@v4`, nie do skrótu commita); `FakturowanieKSeF.Tests/FakturowanieKSeF.Tests.csproj:20-23`
  (`coverlet.collector` dołączony, ale nigdzie nie uruchamiany `--collect:"XPlat Code Coverage"`)
- **Problem:** zawieszony test E2E (np. oczekiwanie na pobranie pliku) blokuje zadanie do domyślnych 6 godzin; po niezaliczonym
  E2E nie ma zrzutu ekranu ani śladu Playwright, więc błąd trzeba odtwarzać lokalnie; pokrycie kodu nie jest mierzone, choć
  praca dyplomowa ma rozdział o testach; kod własny ma 0 ostrzeżeń, ale nic tego stanu nie pilnuje.
- **Scenariusz:** niestabilny scenariusz E2E zawodzi raz na kilkanaście przebiegów - bez śladu i zrzutu nie da się ustalić
  przyczyny, test zostaje „uruchom ponownie”.
- **Propozycja poprawki:** `timeout-minutes: 20`; `dotnet test … --blame-hang-timeout 5m --logger trx --collect:"XPlat Code Coverage"`;
  w `E2EFixture.NewPageAsync` śledzenie Playwright (`Tracing.StartAsync`) zapisywane przy niepowodzeniu + `actions/upload-artifact`
  z `if: failure()`; `concurrency` z anulowaniem starszych przebiegów i `push` tylko dla `main`; `TreatWarningsAsErrors` dla
  projektów własnych; (opcjonalnie) akcje przypięte do SHA.
- **Test, który to wykryje:** nie dotyczy (konfiguracja CI).

### TST-11. Opcje konfiguracji jako literały rozsiane po kodzie, bez klas opcji i walidacji przy starcie
- **Waga:** niskie
- **Rodzaj:** uproszczenie / błąd w rzadkim przypadku
- **Pewność:** potwierdzone (zestawienie kluczy z kodu i z `appsettings*.json`)
- **Miejsce:** `FakturowanieKSeF.Api/appsettings.json` (tylko `ConnectionStrings`, `Logging`, `AllowedHosts`); klucze czytane w kodzie:
  `Program.cs:117-123`, `:157`, `:176`; `Services/KsefSubmissionWorker.cs:31-37`; `Services/ReceivedInvoiceSync.cs:35`, `:166-192`;
  `Services/IssuedInvoiceImport.cs:32` (ten sam klucz `Ksef:Receive:InitialDays` w dwóch klasach); `Services/RecurringInvoices.cs:159-165`;
  `Services/Email/PaymentReminders.cs:96-102`; `Services/Email/EmailOutbox.cs:141-147`; `Integrations/VatWhiteList/VatRegistryLookup.cs:92-93`;
  `Infrastructure/RateLimits.cs:25`; `App:PublicUrl` w trzech plikach
- **Problem:** ok. 25 opcji (`Ksef:Worker:*`, `Ksef:Receive:*`, `Ksef:ImportIssued:Enabled`, `Email:Worker:*`, `Reminders:Worker:*`,
  `Recurring:Worker:*`, `RateLimiting:*`, `VatWhiteList:*`, `App:PublicUrl`, `ReverseProxy:Enabled`, `Database:MigrateOnStartup`,
  `DataProtection:*`) nie występuje w `appsettings.json`, wartości domyślne są w wywołaniach `GetValue` w kilkunastu miejscach,
  a jedyną listą jest tabela w `docs/wdrozenie.md`. Tylko `Email` ma klasę opcji. Literówka w nazwie zmiennej środowiskowej
  jest ignorowana po cichu, a wartości spoza zakresu (np. `Ksef:Worker:IntervalSeconds=0`, `Email:Worker:IntervalSeconds=0`)
  nie są sprawdzane - `TimeSpan.Zero` w pętli usługi w tle oznacza odpytywanie bazy bez przerwy (tylko `Ksef:Receive`,
  `Recurring` i `Reminders` mają `Math.Max`).
- **Scenariusz:** wdrożenie z `RateLimiting__AuthPerMinut=5` (literówka) - aplikacja startuje z domyślnym limitem 10, nikt nie
  zauważa; `Ksef__Worker__IntervalSeconds=0` - procesor wysyłek kręci się w pętli bez opóźnienia.
- **Propozycja poprawki:** klasy opcji (`KsefWorkerOptions`, `RateLimitOptions`, …) z `ValidateDataAnnotations().ValidateOnStart()`
  i wartościami domyślnymi w jednym miejscu; sekcje z wartościami domyślnymi w `appsettings.json` jako dokumentacja.
- **Test, który to wykryje:** `HostingTests.NieprawidlowaKonfiguracja_OdmowaStartu` (fabryka z `Ksef:Worker:IntervalSeconds=0` →
  wyjątek walidacji opcji przy starcie) oraz test zgodności: każda opcja z klas opcji występuje w tabeli `docs/wdrozenie.md`.

### TST-12. Drobne niespójności: dokument testów, prototyp w rozwiązaniu, powtórzone kroki testów
- **Waga:** niskie
- **Rodzaj:** uproszczenie
- **Pewność:** potwierdzone
- **Miejsce:** `docs/testy.md:11-14` (diagram: „E2E (9)”, „na żywo (3)”, „integracyjne ok. 90”, „jednostkowe ok. 80” - faktycznie
  26 E2E, 7 na żywo, 323 łącznie się zgadza); `FakturowanieKSeF.sln:16-18` i `tools/FakturowanieKSeF.KsefSpike` (prototyp z etapu 1
  nadal w rozwiązaniu - budowany przy każdym `dotnet build`/CI i kopiowany do kontekstu obrazu `COPY . .`);
  powtórzenia w testach: pętla `for (var i = 0; i < 5 && await factory.ProcessKsefAsync() > 0; i++) { }` w 4 plikach
  (`TestUsers.cs:50`, `KsefSendingTests.cs:88`, `PaymentTests.cs:66`, `ProformaTests.cs:93`), własne kopie „zaloguj i ustaw token Bearer”
  (`TwoFactorTests.cs:18-26`, `SecurityHardeningTests.cs:86-91`, `AuthTests`), lokalne `AcceptedInvoiceAsync` w `PaymentTests.cs:48-71`
  obok `TestUsers.AcceptedInvoiceAsync`; rekordy `LoginResponse` zdefiniowane 3 razy; pusty `GlobalUsings.cs`
- **Problem:** dokument, który trafi do pracy, podaje nieaktualne liczby; powtórzony kod pomocniczy utrudnia wprowadzenie
  zmian z TST-2 i TST-8 (każdą trzeba zrobić w kilku miejscach).
- **Scenariusz:** recenzent porównuje diagram z `docs/testy.md` z wynikiem `dotnet test` - liczby się nie zgadzają.
- **Propozycja poprawki:** zaktualizować diagram (albo generować liczby skryptem z wyniku TRX); prototyp przenieść poza
  rozwiązanie główne (osobny `.sln` w `tools/`) lub usunąć po opisaniu w decyzjach; wspólne pomocniki w `TestUsers`.
- **Test, który to wykryje:** nie dotyczy.

## Sprawdzone bez uwag

- **Kod produkcyjny - wzorce asynchroniczne:** brak `async void`, `.Result`, `.Wait()`, `GetAwaiter().GetResult()`, `Thread.Sleep`,
  `Task.Run` w projektach Api, Client, Contracts, Ksef, Models; `CancellationToken` przekazywany w endpointach, usługach i klientach HTTP.
- **Połykane wyjątki:** 58 bloków `catch` przejrzanych - wszystkie `catch (Exception)` w usługach w tle logują wyjątek
  i przepuszczają `OperationCanceledException`; puste `catch` tylko dla `SemaphoreFullException` (sygnał już ustawiony),
  `TaskCanceledException` debounce i `JSException` przełącznika motywu - uzasadnione. Filtry wyjątków z `is … or … &&` mają
  poprawny priorytet (`NbpExchangeRates.cs:40`, `Program.cs:167`).
- **Zamiana wyjątków bazy na 409:** `ApiErrors.ExceptionHandler` (konflikt wersji, naruszenie unikalności) - testy
  `InvoiceApiTests.Edycja_…Zwraca409`, `CustomerApiTests.Kontrahent_ZDuplikatemNip_Zwraca409`, `ProductApiTests.TaSamaNazwa_…`.
- **Martwy kod:** przeszukanie wszystkich typów i metod bez odwołań w kodzie produkcyjnym oraz komponentów Razor bez użycia -
  brak trafień (poza prototypem z TST-12).
- **Bezpiecznik bazy testowej:** `ApiFactory` tworzy i usuwa wyłącznie bazy `ksef_test_*` (podwójne sprawdzenie nazwy);
  po przebiegach nie zostają bazy tymczasowe przy poprawnym zakończeniu.
- **Bramkowanie testów na żywo:** `LiveKsefFact` (także `ExternalApiLiveTests`) i `E2EFact` pomijają testy bez zmiennych
  środowiskowych; w przebiegach tego przeglądu 7 testów na żywo było pominiętych.
- **Atrapy `FakeNbpClient`, `FakeVatWhiteList`, `FakeEmailSender`:** proste i zgodne z kontraktem; realne formaty odpowiedzi
  NBP i MF są sprawdzane osobno testami klientów ze `StubHandler` (uwaga do NBP - TST-3).
- **Testy jednostkowe logiki:** `NipValidatorTests`, `InvoiceCalculatorTests`, `VatRateTests`, `CorrectionCalculatorTests`,
  `PaymentRulesTests`, `RecurringScheduleTests`, `CsvFormatTests`, `Fa3XmlGeneratorTests` (ze stałym `TimeProvider`),
  `Fa3SchemaValidatorTests`, `KsefEnvironmentGuardTests` - asercje na wynikach, daty przekazywane jawnie, bez zależności od zegara.
- **E2E - czekanie:** 196 asercji `Expect` z automatycznym ponawianiem, 36 `WaitForURLAsync`; pętle z `Task.Delay(250)`
  mają termin 20 s i asercję po pętli; brak stałych opóźnień wpływających na wynik.
- **Ostrzeżenia kompilatora w kodzie własnym:** 0.

## Poza obszarem

- Kandydaci z raportu obszaru 04 (w toku) przywołani w mapie pokrycia jako FAK-A, FAK-B, FAK-D, FAK-H mają tam numerację
  roboczą - po zakończeniu obszaru 04 identyfikatory w tabeli warto uzgodnić.
- Nie znalazłem nowych problemów krytycznych spoza obszaru.

## Pytania do autora

1. Czy stałe daty „2026-11” w danych testowych są celowe (zgodność ze zrzutami ekranu i przykładami w pracy)? Jeśli tak,
   właściwą poprawką TST-1 jest zamrożenie zegara serwera w testach, a nie przeliczanie dat.
2. Czy prototyp `tools/FakturowanieKSeF.KsefSpike` ma zostać w repozytorium jako materiał do rozdziału o integracji?
   Jeśli tak - czy może wyjść z głównego rozwiązania?
3. Czy pomiar pokrycia kodu ma trafić do pracy? `coverlet.collector` jest już w projekcie, wystarczy krok w CI.
4. Czy testy na żywo mają być okresowo uruchamiane automatycznie (np. ręcznie wyzwalane zadanie `workflow_dispatch`)?
   Dziś jedynym dowodem zgodności atrapy z KSeF są przebiegi ręczne opisane w `docs/testy.md`.
