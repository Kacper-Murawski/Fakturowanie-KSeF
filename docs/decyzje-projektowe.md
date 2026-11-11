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

**Decyzja:** backend ASP.NET Core Web API (.NET 10 LTS, pierwotnie .NET 8 - zob. D9), frontend Blazor WebAssembly, baza PostgreSQL 16, ORM EF Core 8.

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

## D4. Jedno konto użytkownika = jedna firma (w MVP; rozszerzone w D46 - dostęp do wielu firm z rolami)

**Kontekst:** użytkownik może w przyszłości prowadzić kilka działalności lub obsługiwać kilka firm (np. biuro rachunkowe).

**Decyzja:** w MVP każde konto ma dokładnie jedną firmę. Model danych (encja `Company` powiązana z właścicielem)
pozostaje przygotowany na wiele firm, żeby rozszerzenie nie wymagało przebudowy.

**Alternatywa:** od początku wiele firm na konto, z przełącznikiem aktywnej firmy w interfejsie.

**Uzasadnienie:** prostsza izolacja danych, prostszy interfejs i mniej przypadków brzegowych; pozwala szybciej
dostarczyć działające minimum. Typowy użytkownik (jednoosobowa działalność) ma jedną firmę.

## D5. Faktury korygujące poza zakresem MVP (zrealizowane po MVP - D32)

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

---

## D9. Migracja z .NET 8 na .NET 10

**Kontekst:** projekt rozpoczęto na .NET 8 (LTS), którego wsparcie kończy się 10 listopada 2026 r.
Po tej dacie Microsoft nie publikuje poprawek bezpieczeństwa.

**Decyzja:** migracja wszystkich projektów na .NET 10 (LTS, wsparcie do listopada 2028 r.) wraz z pakietami
w wersji 10.0.x (EF Core, Identity, Npgsql 10). Wersja SDK przypięta w `global.json`.

**Uzasadnienie:**
- aplikacja jest publicznie dostępna na serwerze, więc nie może działać na platformie bez poprawek bezpieczeństwa,
- oficjalny klient KSeF Ministerstwa Finansów jest budowany m.in. pod .NET 10 (zob. D10),
- na wczesnym etapie projektu migracja jest tania: model danych i migracje bazy pozostały bez zmian,
  wszystkie testy przechodzą bez modyfikacji.

---

## D10. Oficjalny klient KSeF Ministerstwa Finansów jako submoduł git

**Kontekst:** komunikacja z KSeF 2.0 wymaga m.in. podpisu XAdES lub szyfrowania tokenu kluczem publicznym MF,
szyfrowania faktur (AES-256, klucz szyfrowany RSA-OAEP), skrótów SHA-256, obsługi sesji, statusów i UPO.
MF udostępnia referencyjnego klienta C# (`CIRFMF/ksef-client-csharp`, licencja MIT), publikowanego jako pakiet
wyłącznie w GitHub Packages (wymaga uwierzytelnienia tokenem PAT).

**Decyzja:** klient MF dołączony jako submoduł git (`external/ksef-client-csharp`), przypięty do wydania 2.9.0
i budowany ze źródeł razem z aplikacją. Aplikacja korzysta z niego wyłącznie przez własny projekt
`FakturowanieKSeF.Ksef`, który konfiguruje klienta na środowisko testowe i dopina blokadę produkcji (D8).

**Alternatywy:**
- pakiet z GitHub Packages - wymaga tokenu PAT lokalnie, w CI i przy budowie obrazu Docker,
- kopia kodu w repozytorium - utrudnia śledzenie wersji i aktualizacje,
- własna implementacja protokołu - największy nakład i ryzyko błędów w kryptografii; samodzielna implementacja
  mechanizmów kryptograficznych bez potrzeby jest uznawana za złą praktykę.

**Uzasadnienie:** protokół i kryptografia utrzymywane przez wydawcę API, zgodne z jego zmianami; brak sekretów
potrzebnych do budowania; wersja przypięta, a aktualizacja jest świadomą decyzją (MF wprowadza zmiany łamiące
zgodność także w mniejszych wydaniach, np. 2.8.0 i 2.9.0). Wyraźny podział między kodem autora a biblioteką.

**Konsekwencje:**
- klonowanie repozytorium z `--recursive`, w CI `submodules: true`,
- zależności kryptograficzne biblioteki (`System.Security.Cryptography.Xml`, `.Pkcs` 10.0.10) miały znane
  podatności - w projekcie `FakturowanieKSeF.Ksef` wymuszono wersje poprawione (10.0.14); `dotnet list package --vulnerable`
  nie zgłasza podatności dla aplikacji. Ostrzeżenia NuGet z wewnętrznego restore biblioteki dla platform,
  których aplikacja nie używa (.NET 8/9, netstandard2.0), pozostają w logu budowania,
- test automatyczny sprawdza, że potok HTTP klienta MF skonfigurowany przez `AddKsefTestClient()` blokuje produkcję.

---

## D11. Wynik spike'a KSeF: przepływ potwierdzony na środowisku testowym

**Wynik:** prototyp przeszedł pełny przepływ (uwierzytelnienie XAdES, sesja online, faktura FA(3), numer KSeF, UPO)
w ok. 7 s. Szczegóły i artefakty: [`docs/spike-ksef/`](spike-ksef/README.md).

**Decyzje wynikające ze spike'a:**
- wybór oficjalnego klienta MF (D10) potwierdzony w praktyce, bez potrzeby własnej implementacji protokołu,
- model faktury zostanie rozszerzony o dane KSeF: numery referencyjne, numer KSeF, datę jego nadania,
  skrót dokumentu, status ze szczegółami błędów, wysłany XML i UPO (etap 2),
- obsługa statusów asynchronicznie, przez usługę w tle (etap 6),
- docelowe uwierzytelnianie użytkowników tokenem KSeF przechowywanym w postaci zaszyfrowanej (etap 6).

---

## D12. Model danych v2

Pełny opis i diagram ERD: [`docs/model-danych.md`](model-danych.md).

**Najważniejsze decyzje:**
1. **Kopie danych stron na fakturze** (`Seller`, `Buyer` jako typy własne EF Core) zamiast odwołania do aktualnych
   danych firmy i kontrahenta - faktura musi pozostać niezmienna po wystawieniu.
   *Alternatywa:* wersjonowanie danych kontrahentów (tabela historii) - bardziej złożone, bez korzyści w MVP.
2. **Osobna tabela prób wysyłki do KSeF** (`KsefSubmissions`) zamiast kolumn w fakturze - zachowuje historię
   (odrzucenie, poprawka, ponowna wysyłka) i dokładnie wysłany XML wraz z UPO, co ma znaczenie dowodowe.
3. **Stawka VAT jako wyliczenie zgodne z FA(3)**, a nie liczba - stawki „zw”, „oo”, „np” oraz warianty 0%
   (kraj, WDT, eksport) mają odrębne znaczenie prawne i są raportowane osobno.
4. **Precyzja liczb i typy dat zgodne ze schemą FA(3)** (`date` dla dat dokumentu, `numeric` z precyzją typów XSD),
   co eliminuje konwersje i zaokrąglenia przy generowaniu XML.
5. **Miękkie usuwanie + `ON DELETE RESTRICT`** - dokumenty podatkowe nie mogą znikać kaskadowo; ochrona działa
   zarówno w aplikacji (filtr globalny, automatyczna zamiana usunięcia), jak i na poziomie bazy (klucze obce).
6. **Kontrola współbieżności** (`xmin`) na fakturze - zapobiega m.in. podwójnej wysyłce do KSeF.
7. **Dane biznesowe przypisane do firmy** (`CompanyId`), a firma do konta - izolacja danych po firmie
   zalogowanego użytkownika; model gotowy na wiele firm na konto w przyszłości (D4).
8. **Jedna migracja początkowa.** Ponieważ aplikacja nie była jeszcze wdrożona i nie istniały żadne dane,
   wcześniejszą migrację zastąpiono jedną migracją `InitialCreate` z nowym modelem. Automatycznie wygenerowana
   migracja przyrostowa zawierała błędne „zmiany nazw” kolumn (np. stary `NetPrice` → `VatAmount`), które
   zaciemniałyby historię schematu. Od chwili pierwszego wdrożenia każda zmiana modelu będzie osobną migracją.

---

## D13. Projekt `Contracts` współdzielony przez API i klienta Blazor

**Decyzja:** DTO, enumy (stawka VAT, forma płatności, status), reguły walidacji i kalkulator kwot faktury
wydzielono do projektu `FakturowanieKSeF.Contracts` bez zależności od EF Core. Klient Blazor referencjonuje
tylko ten projekt, a encje bazodanowe (`Models`) są dostępne wyłącznie po stronie serwera.

**Uzasadnienie:**
- encje nie są wysyłane do przeglądarki (mniejszy rozmiar aplikacji WebAssembly, brak EF Core i Identity w kliencie),
- kontrakt API jest jawny i niezależny od schematu bazy,
- ten sam kalkulator liczy kwoty w API i na żywo w formularzu, więc użytkownik widzi dokładnie te kwoty,
  które zostaną zapisane i wysłane do KSeF.

## D14. Walidacja: DataAnnotations z wbudowaną walidacją minimal API (.NET 10)

**Decyzja:** reguły zapisane atrybutami i `IValidatableObject` w DTO; API waliduje żądania automatycznie
(`AddValidation()`, nowość .NET 10), także obiekty zagnieżdżone i elementy list (np. `Items[1].Quantity`).
Własne atrybuty: `[Nip]` (suma kontrolna), `[BankAccount]` (format TNrRB).

**Alternatywa:** FluentValidation - popularna, ale to dodatkowa zależność, a reguł nie da się bezpośrednio użyć
w standardowym walidatorze formularzy Blazor.

**Uzasadnienie:** jedno źródło reguł dla API i formularzy, brak zewnętrznych bibliotek, standardowy format
błędów (ValidationProblemDetails).

## D15. Izolacja danych przez firmę ustalaną z tożsamości użytkownika (z rolami - D46)

**Decyzja:** usługa `CurrentCompany` ustala firmę na podstawie zalogowanego użytkownika; każde zapytanie
zawiera warunek `CompanyId == firma użytkownika`. Cudze rekordy są traktowane jak nieistniejące (404).

**Alternatywa:** globalny filtr zapytań EF Core oparty o firmę z kontekstu żądania - działa automatycznie,
ale komplikuje usługi działające w tle (wysyłka do KSeF dla wielu firm) i ukrywa warunek bezpieczeństwa
w konfiguracji. Wybrano jawne warunki w każdym zapytaniu.

**Uzasadnienie i weryfikacja:** warunek jest widoczny w kodzie każdego endpointu; test `DataIsolationTests`
sprawdza odczyt, listy, edycję, usuwanie i użycie cudzego kontrahenta. Odpowiedź 404 zamiast 403 nie ujawnia
nawet istnienia cudzych danych.

## D16. Metoda obliczania kwot faktury

**Decyzja:** metoda „od netto”: netto pozycji = ilość × cena (zaokrąglenie do groszy); VAT liczony od sumy netto
w ramach każdej stawki i zaokrąglany raz; zaokrąglenie połówkowe od zera. VAT i brutto pozycji mają charakter
informacyjny. Kwoty liczy wyłącznie serwer - klient ich nie przesyła.

**Uzasadnienie:** struktura FA(3) raportuje podatek per stawka (P_13_x, P_14_x), więc liczenie od sumy netto
w stawce zapewnia zgodność kwot z XML; liczenie per pozycja mogłoby dawać różnice groszowe (przykład w teście
`VatFaktury_LiczonyOdSumyNettoWStawce_NieSumaVatPozycji`). Zaufanie wyłącznie obliczeniom serwera chroni
przed zmanipulowanymi kwotami z przeglądarki.

## D17. Numeracja faktur i kontrola współbieżności w API

**Decyzja:**
- numer automatyczny `FV/{n}/{MM}/{RRRR}`, narastający w miesiącu; użytkownik może podać własny,
  a unikalność w ramach firmy gwarantuje indeks bazy. Przy kolizji równoczesnych zapisów numer jest nadawany
  ponownie (do 3 prób),
- edycja faktury wymaga przesłania wersji z ostatniego odczytu (`xmin`). Nieaktualna wersja daje 409
  (optymistyczna kontrola współbieżności - bez blokad w bazie),
- faktury w statusie `Pending` i `Accepted` nie mogą być edytowane ani usuwane.

---

## D18. Uwierzytelnianie przeglądarki ciasteczkiem sesji zamiast tokenu w JavaScripcie

**Decyzja:** aplikacja Blazor loguje się przez `/api/auth/login?useCookies=true`. Serwer ustawia ciasteczko
`HttpOnly` (niedostępne dla skryptów) z `SameSite=Strict` (niewysyłane przy żądaniach z innych witryn)
i `Secure` w środowisku produkcyjnym. Kod klienta nie ma dostępu do żadnych danych uwierzytelniających;
stan zalogowania ustala zapytaniem do serwera (`/api/auth/manage/info`).

**Alternatywa:** token Bearer (JWT) przechowywany w `localStorage` i dołączany do żądań przez klienta.

**Uzasadnienie:** token w `localStorage` może zostać wykradziony przez dowolny skrypt w razie podatności XSS;
ciasteczko HttpOnly - nie. Możliwe dzięki temu, że klient i API działają w jednej domenie (D6). `SameSite=Strict`
oraz wymaganie treści JSON (żądanie z innej witryny wymagałoby zgody CORS, której API nie udziela) chronią przed CSRF.
Dodatkowo: polityka haseł (min. 8 znaków, różne klasy znaków), blokada konta po 5 nieudanych próbach,
polskie komunikaty błędów, ochrona przed przekierowaniem na obcą stronę po zalogowaniu (`returnUrl` tylko względny).

## D19. Walidacja formularzy w przeglądarce tymi samymi regułami co w API

**Decyzja:** formularze używają komponentu `DeepValidator`, który stosuje atrybuty DataAnnotations z projektu
`Contracts` (D13, D14) również do obiektów zagnieżdżonych (adres, nabywca) i elementów list (pozycje faktury)
- czego standardowy `DataAnnotationsValidator` nie robi. Ten sam komponent przypina do pól formularza błędy
zwrócone przez serwer (np. `Items[1].Quantity`, konflikt numeru faktury), więc użytkownik widzi je przy właściwym polu.

**Uzasadnienie:** natychmiastowa informacja zwrotna bez wysyłania formularza, jedno źródło reguł; serwer pozostaje
ostateczną instancją walidacji (klient może zostać zmodyfikowany).

## D20. Wnioski z testów w przeglądarce: kultura pl-PL

Test całego scenariusza w prawdziwej przeglądarce (rejestracja → firma → kontrahent → faktura → edycja → lista
→ wylogowanie) wykrył błąd niewidoczny w testach API: atrybut `[Range(typeof(decimal), "0.000001", ...)]` parsował
granice zakresu według bieżących ustawień regionalnych. Na serwerze (kultura niezmienna, kropka dziesiętna)
działał poprawnie, w przeglądarce z kulturą `pl-PL` (przecinek) - rzucał wyjątek przy walidacji ilości.
Poprawka: `ParseLimitsInInvariantCulture` i `ConvertValueInInvariantCulture`, oraz test regresyjny uruchamiany
w kulturach pl-PL, en-US i de-DE. Wniosek: testy API nie zastąpią testów interfejsu - automatyczne testy E2E
są zaplanowane w etapie 10.

---

## D21. Generowanie XML FA(3): własny generator na `XmlWriter` + walidacja XSD przed każdym wydaniem (waluty obce - D43)

**Decyzja:** XML faktury tworzy `Fa3XmlGenerator` strumieniowo przez `XmlWriter`, w kolejności elementów
zgodnej z sekwencjami schematu. Każdy wynik jest sprawdzany przez `Fa3SchemaValidator` względem oficjalnego
schematu FA(3) osadzonego w aplikacji (wraz ze schematami bazowymi MF). Szczegóły i mapowanie: [`docs/fa3-xml.md`](fa3-xml.md).

**Alternatywy:**
- klasy wygenerowane ze schematu (`xsd.exe`/`XmlSerializer`) - schemat FA(3) ma kilkaset typów, wygenerowany
  kod byłby ogromny i nieczytelny, a aplikacja używa niewielkiego podzbioru pól,
- szablon XML z podmianą tekstu - podatny na błędy kodowania znaków specjalnych i wstrzyknięcie znaczników,
- biblioteka MF nie dostarcza modelu FA(3) (tylko ogólny serializer).

**Uzasadnienie:** jawne, czytelne mapowanie pól (łatwe do opisania i zweryfikowania), poprawne kodowanie
znaków przez `XmlWriter`, pełna kontrola nad wymaganiami technicznymi KSeF (UTF-8 bez BOM).

**Weryfikacja:** testy automatyczne każdego wariantu (stawki, B2C, płatności, znaki specjalne) względem XSD,
oraz wysyłka wygenerowanej faktury do testowego KSeF zakończona nadaniem numeru KSeF i UPO.

**Ograniczenia MVP wynikające ze struktury FA(3):** tylko PLN (waluta obca wymaga kwot VAT w PLN i kursu),
bez stawek 4% i 3% (wyłącznie ryczałt dla taksówek), bez mechanizmu podzielonej płatności; dla stawki „zw”
wymagana podstawa prawna zwolnienia (nowe pole `ExemptionBasis`, P_19A). Ograniczenia egzekwowane są
w walidacji formularza i API, w generatorze oraz w interfejsie.

## D22. Pola formularzy walidowane podczas pisania (`InputTextLive`)

**Kontekst:** test E2E wykrył, że po poprawieniu błędnego pola i natychmiastowym kliknięciu „Zapisz” formularz
„nie reagował”. Standardowe pole Blazor (`InputText`) przyjmuje wartość dopiero po opuszczeniu pola; komunikat
błędu znikał w chwili kliknięcia, układ strony przesuwał się i kliknięcie trafiało obok przycisku.

**Decyzja:** komponent `InputTextLive` aktualizuje model przy każdym naciśnięciu klawisza (`oninput`), więc
walidacja i komunikaty aktualizują się na bieżąco, zanim użytkownik sięgnie po przycisk. Stosowany we wszystkich
formularzach danych.

**Wniosek:** kolejny błąd interfejsu, którego nie wykrywają testy API (por. D20) - argument za automatycznymi
testami E2E (etap 10).

---

## D23. Przechowywanie tokenów KSeF: ASP.NET Core Data Protection, klucze w bazie

**Decyzja:** token KSeF firmy jest szyfrowany przez Data Protection (AES-256-CBC + HMAC-SHA256, osobny cel
kryptograficzny) i zapisywany w bazie wyłącznie w postaci zaszyfrowanej. Odszyfrowanie następuje tylko w pamięci
serwera w chwili komunikacji z KSeF. Token nie jest nigdy zwracany przez API ani zapisywany w logach.
Klucze Data Protection są przechowywane w bazie (`DataProtectionKeys`), więc przetrwają restart i wdrożenie.

**Alternatywy:** zapis jawny (niedopuszczalne - token daje pełny dostęp do faktur firmy w KSeF); własne
szyfrowanie AES z kluczem w konfiguracji (ręczne zarządzanie kluczem i jego rotacją, łatwo o błąd);
zewnętrzny magazyn sekretów (np. Azure Key Vault) - najlepsza ochrona, ale dodatkowa infrastruktura.

**Ograniczenie i kierunek rozwoju:** klucze i zaszyfrowane dane leżą w tej samej bazie - przejęcie kopii bazy
wraz z kluczami pozwoliłoby odszyfrować tokeny. Na produkcji klucze należy dodatkowo chronić
(`ProtectKeysWithCertificate` lub magazyn sekretów).

## D24. Token testowy generowany przez aplikację (tylko środowisko testowe)

**Decyzja:** obok wklejenia tokenu (sposób docelowy - token z Aplikacji Podatnika) aplikacja oferuje przycisk
„Wygeneruj token testowy”: uwierzytelnia się w testowym KSeF samopodpisanym certyfikatem dla NIP firmy
(dopuszczalnym wyłącznie na środowisku testowym) i generuje token z uprawnieniami `InvoiceWrite` i `InvoiceRead`.

**Uzasadnienie:** umożliwia pełne przetestowanie aplikacji bez dostępu do prawdziwego konta podatnika, przy
zachowaniu docelowego mechanizmu (uwierzytelnianie tokenem). Ponieważ środowisko testowe jest współdzielone przez
integratorów, aplikacja ostrzega przed używaniem prawdziwych danych firmy.

## D25. Asynchroniczna wysyłka do KSeF jako trwała maszyna stanów

**Decyzja:** kliknięcie „Wyślij” tylko przygotowuje i zapisuje wysyłkę (XML + skrót, status `Pending`); wysyłkę,
odpytywanie o status i pobranie UPO realizuje usługa w tle, zapisując stan każdego kroku w bazie.
Szczegóły: [`docs/integracja-ksef.md`](integracja-ksef.md).

**Alternatywa:** wysyłka i oczekiwanie na wynik w ramach żądania HTTP - KSeF przetwarza faktury asynchronicznie
(sekundy, w okresach obciążenia dłużej), żądanie mogłoby przekroczyć limit czasu, a jego przerwanie zostawiłoby
fakturę w nieznanym stanie.

**Kluczowa reguła poprawności:** faktura, która mogła już trafić do KSeF (ma numer referencyjny), nigdy nie jest
oznaczana jako odrzucona z powodu błędu komunikacji - tylko ponawiane jest odpytanie o status. Ponowna wysyłka
faktury przyjętej przez KSeF utworzyłaby duplikat dokumentu podatkowego. Przerwanie wysyłki (po 10 próbach lub
przy nieprawidłowym tokenie) jest dopuszczalne wyłącznie, gdy faktura na pewno nie dotarła do KSeF.

**Testowalność:** bramka `IKsefGateway` z atrapą pozwala w CI sprawdzić wszystkie ścieżki (przyjęcie, odrzucenie,
awarie przed i po wysyłce, podwójna wysyłka); prawdziwą bramkę sprawdza test na żywo i test E2E z testowym KSeF.

---

## D26. Odbiór faktur: synchronizacja przyrostowa „do HWM” w trybie mieszanym

**Decyzja:**
- faktury otrzymane są pobierane przyrostowo przez `/invoices/query/metadata` (Podmiot2, data trwałego zapisu)
  z ograniczeniem do HWM; punkt kontrolny jest przechowywany per firma,
- deduplikacja odbywa się po numerze KSeF,
- synchronizacja działa cyklicznie co 15 minut, a dodatkowo można ją uruchomić ręcznie (nie częściej niż raz
  na minutę), z budżetem zapytań mieszczącym się w limitach API,
- przeglądanie korzysta wyłącznie z lokalnej bazy.

Szczegóły: [`docs/integracja-ksef.md`](integracja-ksef.md#odbiór-faktur-faktury-otrzymane).

**Alternatywy:**
- eksport paczek (`/invoices/exports`): asynchroniczny, szyfrowany, zalecany dla dużych wolumenów; dla małych
  i średnich firm nadmiarowy (dodatkowe odpytywanie statusu, rozpakowywanie archiwów),
- synchronizacja „do Teraz”: najświeższe faktury szybciej, ale wymaga ponownego sprawdzania okna (HWM, Teraz]
  i deduplikacji przy każdym przebiegu,
- pobieranie z KSeF przy każdym wyświetleniu: wprost odradzane przez MF (ryzyko blokady za przekroczenie limitów).

**Uzasadnienie:** zgodność z zaleceniami MF dla profilu niskiego wolumenu, gwarancja kompletności danych bez
wielokrotnego sprawdzania (HWM), odporność na przerwania (punkt kontrolny przesuwany tylko za faktury zapisane
w bazie), ochrona przed przekroczeniem limitów API.

**Obserwacja z testów na żywo:** faktura była dostępna do pobrania ok. 2 minut po nadaniu numeru KSeF.

---

## D27. Wydruk PDF z dokumentu XML i kod QR weryfikacji KSeF

**Decyzja:** PDF faktury jest generowany biblioteką QuestPDF z treści dokumentu XML FA (a nie bezpośrednio z bazy) -
jednym rendererem dla faktur wystawionych i otrzymanych. Faktury przekazane do KSeF mają kod QR weryfikacji (KOD I)
z numerem KSeF (lub „OFFLINE” przed jego nadaniem); szkice i faktury odrzucone są wyraźnie oznaczone jako niebędące fakturą.

**Alternatywy:** generowanie HTML i konwersja do PDF przeglądarką bezgłowicową (cięższe wdrożenie - przeglądarka na
serwerze); wydruk z przeglądarki użytkownika (brak kontroli nad układem, brak pliku do wysłania); biblioteki
niskopoziomowe (PdfSharp/iText - więcej kodu; iText na licencji AGPL).

**Uzasadnienie:** wydruk odpowiada dokładnie dokumentowi w KSeF (jedno źródło prawdy - XML), deklaratywny układ
w C#, licencja QuestPDF Community bezpłatna dla projektu akademickiego (oraz firm z przychodem < 1 mln USD).
Poprawność kodu QR potwierdzona zgodnością linku z klientem MF i skrótu dokumentu z UPO wystawionym przez KSeF.

---

## D28. Dane kontrahenta z wykazu podatników VAT (Biała lista MF)

**Kontekst:** wszystkie konkurencyjne programy do faktur uzupełniają dane kontrahenta po wpisaniu NIP. Źródłem
może być wykaz podatników VAT Ministerstwa Finansów (publiczne API `wl-api.mf.gov.pl`, bez klucza) albo rejestr
REGON GUS (API BIR, wymaga klucza i protokołu SOAP). API wykazu ogranicza dzienną liczbę zapytań wyszukujących
dla adresu IP, a w modelu SaaS wszystkie zapytania wychodzą z jednego adresu serwera.

**Decyzja:** dane po NIP są pobierane z wykazu podatników VAT przez usługę `VatRegistryLookup`:
- wynik (także „brak w wykazie”) jest przechowywany w pamięci podręcznej do końca dnia - wykaz podaje stan na dzień,
- liczba zapytań do MF jest ograniczona dziennie dla całej aplikacji i dla każdego użytkownika (konfiguracja
  `VatWhiteList:DailyLimit`, `VatWhiteList:DailyLimitPerUser`), żeby jeden użytkownik nie zablokował funkcji innym,
- adres z wykazu (jeden tekst wielkimi literami) jest rozbijany na pola formularza z polską pisownią
  („UL. JANA PAWŁA II 15” → „ul. Jana Pawła II 15”); gdy nie da się go rozpoznać, użytkownik dostaje komunikat,
- dane tylko podpowiadają wartości w formularzu - użytkownik je widzi i zatwierdza przed zapisem,
- interfejs pokazuje status VAT i ostrzega, gdy podmiot nie jest czynnym podatnikiem VAT.

**Alternatywy:**
- REGON GUS (BIR1): obejmuje też podmioty niebędące podatnikami VAT, ale wymaga klucza od GUS, protokołu SOAP
  i nie podaje statusu VAT ani rachunków; możliwe rozszerzenie w przyszłości,
- zapytania bez pamięci podręcznej i limitów: prostsze, ale ryzyko przekroczenia limitu MF dla całej aplikacji,
- pamięć podręczna w bazie danych zamiast w pamięci procesu: przetrwałaby restart, ale przy jednej instancji
  aplikacji nie daje istotnej korzyści (limity MF odnawiają się codziennie).

**Uzasadnienie:** jedno publiczne źródło urzędowe, które oprócz danych rejestrowych zwraca status VAT i rachunki
(wykorzystane też do weryfikacji rachunków na fakturach otrzymanych), bez rejestracji i kluczy. Pamięć podręczna
i limity chronią wspólny limit MF w modelu wielu użytkowników.

---

## D29. Weryfikacja rachunku z faktury otrzymanej na białej liście

**Kontekst:** KSeF nie weryfikuje rachunku bankowego na fakturze. Za zapłatę powyżej 15 000 zł na rachunek spoza
wykazu podatników VAT odpowiada nabywca (koszty uzyskania przychodu, solidarna odpowiedzialność za VAT), a liczy się
stan wykazu z dnia zlecenia przelewu.

**Decyzja:**
- sprawdzenie wykonuje użytkownik przyciskiem w podglądzie faktury otrzymanej (najlepiej w dniu przelewu),
- używana jest metoda `check` API MF (NIP + rachunek), a nie porównanie z listą rachunków z wyszukiwania:
  metoda `check` uwzględnia rachunki wirtualne (maski), ma wyższy limit zapytań i zwraca jednoznaczną odpowiedź,
- wynik (tak/nie, dzień, identyfikator zapytania MF) jest zapisywany przy fakturze jako potwierdzenie sprawdzenia
  i widoczny na liście faktur; ponowne sprawdzenie tego samego dnia korzysta z pamięci podręcznej,
- sprawdzane są tylko polskie numery NRB (26 cyfr, opcjonalnie z prefiksem PL) - wykaz nie obejmuje innych,
- osobne dzienne limity dla sprawdzeń (`VatWhiteList:DailyCheckLimit`, `VatWhiteList:DailyCheckLimitPerUser`).

**Alternatywy:**
- automatyczne sprawdzanie każdej pobranej faktury podczas synchronizacji z KSeF: wygodne, ale wynik byłby
  aktualny na dzień pobrania, a nie zapłaty, i zużywałby limit MF także dla faktur opłacanych gotówką lub kartą,
- porównanie z listą `accountNumbers` z wyszukiwania podmiotu: zawodzi dla rachunków wirtualnych i zużywa
  niższy limit metod wyszukujących,
- pliki płaskie MF (codzienny wykaz skrótów NIP + rachunek do pobrania): sprawdzanie bez zapytań online
  i bez limitów, ale wymaga pobierania i przechowywania dużego pliku codziennie - zasadne przy dużej skali.

**Uzasadnienie:** sprawdzenie wykonywane świadomie w momencie istotnym prawnie (przed zapłatą), zapis
identyfikatora zapytania MF jako dowodu, oszczędne gospodarowanie wspólnym limitem MF.

---

## D30. Rozliczenia faktur jako lista płatności

**Decyzja:** płatności są osobną tabelą `Payments` (data, kwota, uwagi), powiązaną z fakturą wystawioną albo
otrzymaną (ograniczenie CHECK: dokładnie jedna). Stan rozliczenia (*nieopłacona*, *częściowo opłacona*, *opłacona*)
i zaległość (*po terminie*) nie są przechowywane, tylko wyliczane z sumy płatności, kwoty brutto i terminu
płatności - w zapytaniach SQL (filtry list, podsumowanie) i w jednej funkcji `PaymentRules` współdzielonej z klientem.
Rozliczeniu podlegają faktury przekazane do KSeF (nie szkice ani odrzucone). Nadpłata i data z przyszłości są
odrzucane. Dzień „dzisiaj” liczony jest w czasie polskim. Termin płatności faktur otrzymanych jest zapisywany
podczas pobierania z KSeF (z XML), a dla faktur pobranych wcześniej uzupełniony w migracji.

**Alternatywy:**
- pole „opłacona” (tak/nie) lub „data zapłaty” w fakturze: najprostsze, ale nie obsługuje wpłat częściowych
  (zaliczki, raty) ani historii,
- przechowywany status rozliczenia aktualizowany przy każdej płatności: szybsze filtry, ale ryzyko niespójności
  (status „po terminie” zmienia się z upływem czasu, bez żadnego zapisu),
- automatyczne rozliczanie na podstawie wyciągów bankowych (import MT940/CSV lub API banku): wygodne, ale wymaga
  dopasowywania przelewów do faktur i integracji z bankami - kierunek rozwoju,
- oznaczenie zapłaty w samej fakturze FA(3) (`Zaplacono`, `DataZaplaty`): dotyczy faktur opłaconych przed
  wystawieniem; nie zastępuje rozliczeń po wystawieniu.

**Uzasadnienie:** model odpowiada rzeczywistości (wpłaty częściowe, historia, możliwość korekty pomyłki), stan
zawsze zgodny z danymi (wyliczany), zaległość aktualna bez zadań w tle. Przy skali MVP wyliczanie sumy w podzapytaniu
jest wystarczająco szybkie (indeksy na kluczach obcych).

---

## D31. „Wystaw podobną” jako wypełnienie formularza w przeglądarce

**Decyzja:** kopiowanie faktury odbywa się w kliencie: formularz nowej faktury pobiera fakturę-wzór istniejącym
endpointem `GET /api/invoices/{id}` i wypełnia pola. Na serwerze nic nie powstaje, dopóki użytkownik nie zapisze
faktury zwykłym `POST /api/invoices` - z pełną walidacją, numeracją i kontrolą dostępu. Numer i data wystawienia są
nowe, termin płatności zachowuje odstęp dni z wzoru. Kontrahent jest wskazywany z książki adresowej, jeśli nadal
istnieje (aktualne dane), a w przeciwnym razie kopiowane są dane nabywcy z wzoru.

**Alternatywy:** endpoint `POST /api/invoices/{id}/copy` tworzący od razu szkic na serwerze - prostszy w użyciu
z innych klientów, ale zostawia niechciane szkice, gdy użytkownik zrezygnuje, i zajmuje kolejny numer faktury.

**Uzasadnienie:** brak nowego endpointu i reguł po stronie serwera (mniej kodu do zabezpieczenia), brak „śmieciowych”
szkiców i luk w numeracji, użytkownik widzi i poprawia dane przed zapisem.

---

## D32. Faktury korygujące metodą „przed i po”

**Kontekst:** w MVP korekty pominięto (D5). Bez nich nie da się poprawić faktury przyjętej w KSeF - faktury w KSeF
nie można zmienić ani usunąć, jedyną drogą jest faktura korygująca (RodzajFaktury = KOR).

**Decyzja:**
- korekta jest fakturą (ten sam model, `Kind = Correction`) z kopią danych faktury korygowanej (numer, data,
  numer KSeF) i nabywcy - jak dane stron na fakturze (D11), korekta jest kompletnym dokumentem,
- pozycje zapisywane są metodą „przed i po”: wiersze stanu przed korektą (`StanPrzed=1`) i stan po korekcie,
  a kwoty P_13_x / P_14_x / P_15 to różnica liczona per stawka tym samym kalkulatorem co faktura,
- stan przed korektą to pozycje faktury albo jej ostatniej korekty przyjętej w KSeF; kolejna korekta odnosi się
  do faktury pierwotnej; nowej korekty nie można wystawić, gdy poprzednia jest przetwarzana przez KSeF,
  a przed wysyłką sprawdzane jest, czy stan przed korektą jest nadal aktualny,
- korekty mają osobną serię numerów `KOR/n/MM/RRRR`,
- korekta nie przyjmuje wpłat - zmienia kwotę należną faktury korygowanej (rozliczenia, D30), co pozwala wykazać
  nadpłatę do zwrotu,
- poza zakresem: korekta danych nabywcy (Podmiot2K), korekty zbiorcze (OkresFaKorygowanej), korekty faktur
  zaliczkowych i faktur spoza KSeF.

**Alternatywy:**
- korekta przez wykazanie wyłącznie różnic w wierszach (np. ilość -2): krótszy dokument, ale trudniejszy w odbiorze
  i niejednoznaczny przy zmianie ceny i stawki jednocześnie; FA(3) wprost przewiduje znacznik StanPrzed dla metody
  „przed i po”,
- osobna encja „Korekta”: duplikacja logiki faktury (pozycje, wysyłka do KSeF, PDF, UPO) - korekta podlega tym
  samym procesom co faktura.

**Uzasadnienie:** czytelność dla nabywcy (widać, co się zmieniło), ponowne użycie całego procesu faktury (walidacja
XSD, wysyłka, UPO, PDF z kodem QR), kontrola spójności stanu przy kolejnych korektach. Zgodność potwierdzona
przyjęciem korekty przez testowy KSeF.

---

## D33. Oznaczenie „Zapłacono” w fakturze FA(3) przy wystawianiu

**Kontekst:** KSeF przechowuje faktury, ale nie obsługuje płatności - rozliczenia po wystawieniu są ewidencją
aplikacji (D30). Struktura FA(3) pozwala jednak zapisać w samej fakturze, że w chwili wystawienia jest już zapłacona
(`Platnosc/Zaplacono = 1`, `DataZaplaty`). Dotyczy to np. sprzedaży za gotówkę lub kartą oraz przedpłat. Po przyjęciu
faktury przez KSeF tej informacji nie da się już zmienić.

**Decyzja:**
- faktura ma opcjonalne pole `PaidOn` (data zapłaty), ustawiane w formularzu polem „Zapłacono przy wystawieniu”;
  data nie może być późniejsza niż data wystawienia, nie dotyczy faktur korygujących, a zaznaczenie zwalnia
  z obowiązku terminu płatności,
- generator zapisuje `Zaplacono` i `DataZaplaty` na początku elementu `Platnosc` (kolejność zgodna z XSD),
- przy wysyłce do KSeF aplikacja rejestruje w rozliczeniu płatność na kwotę brutto z flagą `FromInvoice` - faktura
  od razu jest *opłacona*, a filtry i podsumowania działają bez zmian; ponowna wysyłka po odrzuceniu zastępuje
  wcześniejszy zapis (bez duplikatu), a takiej płatności nie można usunąć (409),
- odczyt XML (podgląd faktur otrzymanych, PDF) rozpoznaje oznaczenie zapłaty i je pokazuje,
- poza zakresem: zapłata częściowa przy wystawieniu (`ZnacznikZaplatyCzesciowej`, `ZaplataCzesciowa`) - zaliczki
  rejestruje się w rozliczeniu po wystawieniu.

**Alternatywy:**
- wyliczanie „wirtualnej” wpłaty z pola `PaidOn` we wszystkich zapytaniach (bez rekordu `Payments`): brak
  powielenia danych, ale zmiana każdego filtra, podsumowania i widoku rozliczenia,
- rejestrowanie płatności już przy zapisie szkicu: szkic nie jest należnością (D30), a dane szkicu mogą się zmienić,
- automatyczne oznaczanie faktur otrzymanych z `Zaplacono` jako opłaconych: wygodne, ale oznaczenie pochodzi od
  sprzedawcy, a nie z ewidencji nabywcy - pokazywane jako informacja, decyzja należy do użytkownika.

**Uzasadnienie:** jedyny moment, w którym informacja o zapłacie trafia do KSeF, jest wykorzystany, a rozliczenie
pozostaje spójne z dokumentem w KSeF. Rekord płatności tworzony w chwili wysyłki (wystawienia) odpowiada zasadzie
z D30, że rozliczeniu podlegają faktury przekazane do KSeF. Zgodność potwierdzona przyjęciem faktury przez testowy KSeF.

---

## D34. Eksport dla księgowej: zestawienia CSV i paczka ZIP z XML i UPO

**Kontekst:** większość małych firm prowadzi księgowość w biurze rachunkowym. Księgowa potrzebuje co miesiąc listy
faktur sprzedaży i zakupu z kwotami per stawka VAT oraz samych dokumentów. Biura mogą pobierać faktury z KSeF
samodzielnie, ale wymaga to nadania im uprawnień w KSeF - eksport jest prostszą drogą dla przedsiębiorcy.

**Decyzja:**
- strona **Eksport dla księgowej** z wyborem miesiąca (domyślnie poprzedni) lub dowolnego okresu do 366 dni,
  podsumowaniem okresu i trzema plikami: zestawienie sprzedaży (CSV), zestawienie zakupów (CSV) i paczka ZIP
  (oba zestawienia, XML faktur sprzedaży z UPO, XML faktur zakupu, plik README z opisem),
- sprzedaż - faktury przyjęte w KSeF (z numerem KSeF) według daty wystawienia; szkice, faktury odrzucone
  i w trakcie wysyłki są pomijane (nie są fakturami w KSeF),
- zakupy - faktury otrzymane według daty otrzymania, czyli nadania numeru KSeF (czas polski): w KSeF to data
  otrzymania faktury, od której zależy prawo do odliczenia VAT,
- kwoty i dane w zestawieniach są odczytywane z dokumentów XML (dla sprzedaży - z dokładnie wysłanego dokumentu),
  w kolumnach per stawka (23%, 8%, 5%, 0%, zw, np, oo); korekty mają kwoty różnicy, tak jak w fakturze korygującej,
- CSV w formacie polskiego Excela: średnik, przecinek dziesiętny, UTF-8 ze znacznikiem BOM; pola tekstowe
  zaczynające się od `=`, `+`, `-`, `@` są poprzedzane apostrofem (ochrona przed wstrzyknięciem formuł - nazwy
  kontrahentów z faktur otrzymanych pochodzą od osób trzecich),
- pliki budowane w pamięci na żądanie (bez przechowywania), tylko z danych firmy zalogowanego użytkownika.

**Alternatywy:**
- format JPK_V7 (plik JPK dla urzędu skarbowego): wymaga danych spoza faktur (np. rozliczenie okresu, korekty
  odliczeń, oznaczenia GTU/procedur) i podpisu - to zadanie programu księgowego, a nie programu do faktur,
- eksport do formatów konkretnych programów księgowych (Symfonia, Optima, enova): wiele formatów i zmiennych
  wersji; CSV jest uniwersalny i importowany przez te programy,
- PDF faktur w paczce: wygodne do przeglądania, ale prawnie wiążący jest XML, a generowanie wielu PDF obciąża serwer;
  PDF dostępny pojedynczo w aplikacji,
- dostęp księgowej do aplikacji (funkcja 18) - większa zmiana modelu uprawnień, rozważana osobno.

**Uzasadnienie:** jeden przycisk raz w miesiącu wystarcza do przekazania kompletu dokumentów, a dane w zestawieniach
są zgodne z dokumentami w KSeF. CSV otwiera się w Excelu bez konfiguracji, a XML i UPO są dowodem wystawienia
i doręczenia faktury.

---

## D35. Katalog towarów i usług jako podpowiedź (kopiowanie do pozycji faktury)

**Kontekst:** przy powtarzalnej sprzedaży użytkownik wpisuje na każdej fakturze te same nazwy, ceny i stawki VAT.
Konkurencyjne programy mają katalog produktów i usług.

**Decyzja:**
- tabela `Products` (nazwa, jednostka, cena netto, stawka VAT) należąca do firmy; nazwa unikalna w katalogu firmy
  (indeks z warunkiem `IsDeleted = false`), miękkie usuwanie, ograniczenia pól jak dla pozycji faktury (P_7, P_8A, P_9A),
- katalog jest **podpowiedzią**: pole nazwy pozycji na fakturze podpowiada nazwy z katalogu (lista `datalist`
  przeglądarki), a wpisanie lub wybranie nazwy z katalogu (bez rozróżniania wielkości liter) uzupełnia jednostkę,
  cenę i stawkę; dane są kopiowane do pozycji i można je zmienić,
- faktura nie przechowuje odwołania do pozycji katalogu - zmiana ceny lub usunięcie pozycji nie wpływa na faktury
  (tak samo jak kopie danych nabywcy, D11),
- pozycję spoza katalogu można zapisać w katalogu jednym kliknięciem z formularza faktury („zapisz w katalogu”),
- katalog nie prowadzi stanów magazynowych ani cenników dla różnych kontrahentów (poza zakresem).

**Alternatywy:**
- pozycja faktury z kluczem obcym do produktu: umożliwia raporty sprzedaży per produkt, ale wiąże dokument podatkowy
  z danymi, które się zmieniają, i komplikuje usuwanie pozycji katalogu; nazwa na fakturze i tak musi być kopią,
- własna lista rozwijana z wyszukiwaniem (komponent JS lub Blazor): ładniejsza, ale więcej kodu i trudniejsza
  obsługa klawiatury i dostępności; `datalist` działa natywnie we wszystkich przeglądarkach,
- automatyczne tworzenie katalogu z historii faktur: wygodne, ale zaśmieca katalog jednorazowymi pozycjami.

**Uzasadnienie:** szybsze wystawianie faktur przy minimalnym kodzie, bez ryzyka zmiany wystawionych dokumentów.
Katalog jest tylko źródłem podpowiedzi, więc nie wpływa na reguły faktury, XML FA(3) ani wysyłkę do KSeF.

---

## D36. Wysyłka e-mail przez kolejkę w bazie (outbox) i serwer SMTP

**Kontekst:** funkcje etapu B (wysyłka faktur, przypomnienia o płatności, powiadomienia) wymagają poczty.
Serwer SMTP bywa chwilowo niedostępny, a wysyłka trwa kilka sekund - nie może blokować żądania ani gubić wiadomości.
Aplikacja w modelu SaaS wysyła pocztę w imieniu wielu firm, więc trzeba ją chronić przed użyciem do rozsyłania spamu.

**Decyzja:**
- **kolejka w bazie (wzorzec outbox):** endpoint zapisuje wiadomość w tabeli `EmailMessages` w tej samej transakcji
  co inne zmiany i zwraca 202; wysyła ją usługa w tle (`EmailWorker`), budzona sygnałem od razu po zapisie,
- ponawianie po błędzie z rosnącymi odstępami (1 min, 5 min, 15 min, 1 h); po wyczerpaniu prób stan *błąd wysyłki*
  z komunikatem widocznym w historii; stan i historia wiadomości są widoczne przy fakturze,
- załączniki (PDF z kodem QR i XML faktury w postaci wysłanej do KSeF) powstają w chwili wysyłki z faktury -
  nie są przechowywane w kolejce (faktura przyjęta w KSeF się nie zmienia),
- nadawcą technicznym jest adres aplikacji (`Email:From`), a nazwą wyświetlaną i adresem odpowiedzi - nazwa
  i e-mail firmy użytkownika; wysyłanie „z adresu” firmy użytkownika byłoby odrzucane przez SPF/DMARC jako podszywanie,
- treść wyłącznie tekstowa (bez HTML) i znaki sterujące usuwane z nagłówków - brak wstrzykiwania treści i nagłówków,
- **dzienny limit** wiadomości do kontrahentów na firmę (domyślnie 100, licznik z bazy - odporny na restart),
- fakturę można wysłać dopiero po przyjęciu przez KSeF (z numerem KSeF i kodem QR); każda wiadomość do kontrahenta
  zawiera ostrzeżenie, że pochodzi z wersji testowej i faktura z testowego KSeF nie ma mocy prawnej,
- MailKit jako klient SMTP; lokalnie skrzynka testowa **Mailpit** w `docker compose` (przechwytuje wszystkie
  wiadomości, podgląd w przeglądarce), w testach automatycznych atrapa `FakeEmailSender`; dane serwera SMTP
  produkcyjnie wyłącznie w zmiennych środowiskowych (`Email__SmtpHost`, `Email__SmtpPassword`…). Bez konfiguracji
  SMTP funkcje e-mail są niedostępne (409), a aplikacja działa dalej.

**Alternatywy:**
- wysyłka bezpośrednio w żądaniu HTTP: prostsza, ale błąd serwera poczty to błąd dla użytkownika i utrata wiadomości,
  a powolny serwer blokuje żądanie,
- zewnętrzna kolejka (RabbitMQ, Azure Service Bus): kolejny komponent do utrzymania - przy skali aplikacji kolejka
  w PostgreSQL wystarcza i daje atomowy zapis z danymi biznesowymi,
- API dostawcy poczty transakcyjnej (SendGrid, Mailgun, Amazon SES): lepsza dostarczalność i statystyki, ale
  uzależnienie od dostawcy; SMTP jest obsługiwany przez wszystkich dostawców, w tym te usługi,
- `System.Net.Mail.SmtpClient`: wbudowany, ale oznaczony przez Microsoft jako niezalecany dla nowych projektów
  (brak obsługi nowoczesnych protokołów) - zalecany jest MailKit,
- treść HTML: ładniejsza, ale wymaga kodowania danych i szablonów; tekst jest czytelny w każdym programie pocztowym.

**Uzasadnienie:** wiadomości nie giną przy awarii poczty, żądania są szybkie, a ten sam mechanizm obsługuje wszystkie
funkcje etapu B. Limity i ostrzeżenie o środowisku testowym ograniczają nadużycia. Wysyłka przez prawdziwy serwer SMTP
została sprawdzona z lokalnym serwerem testowym (nagłówki From/Reply-To, załącznik).

---

## D37. Przypomnienia o płatności: ręczne i automatyczne z ograniczeniami

**Kontekst:** rozliczenia (D30) pokazują faktury po terminie, ale użytkownik musi sam upominać nabywców. Konkurencja
wysyła przypomnienia automatycznie. Zbyt częste wiadomości szkodzą relacji z kontrahentem i mogą trafić do spamu.

**Decyzja:**
- przypomnienie ręczne: przycisk **Przypomnij o płatności** przy fakturze VAT przyjętej w KSeF z kwotą pozostałą
  do zapłaty; wiadomość zawiera pozostałą kwotę (po wpłatach częściowych i korektach), termin i liczbę dni po terminie,
  numer rachunku, tytuł przelewu i fakturę w załączniku; faktura opłacona - 409,
- przypomnienia automatyczne włączane w ustawieniach firmy (domyślnie wyłączone): pierwsze po N dniach od terminu
  (domyślnie 3), kolejne nie częściej niż co 7 dni, najwyżej 3 do jednej faktury; odstęp liczy także przypomnienia
  ręczne; po wpłacie przypomnienia ustają,
- adres nabywcy: e-mail kontrahenta z książki adresowej, a w drugiej kolejności - ostatni adres, na który wysłano
  fakturę; bez adresu przypomnienie automatyczne jest pomijane,
- usługa w tle uruchamiana co godzinę; reguła odstępu sprawia, że wielokrotne uruchomienie (np. po restarcie)
  nie dubluje wiadomości; przypomnienia korzystają z kolejki e-mail i dziennego limitu firmy (D36).

**Alternatywy:**
- przypomnienie przed terminem płatności: przydatne, ale część nabywców odbiera je jako nachalne; ręczne
  przypomnienie można wysłać w dowolnym momencie,
- konfigurowalny harmonogram (np. 3, 10, 30 dni) i własne szablony treści: większa elastyczność kosztem złożoności
  interfejsu - stałe reguły wystarczają małej firmie,
- osobna tabela przypomnień: historia jest już w kolejce e-mail (`EmailMessages` z rodzajem `PaymentReminder`),
  która wystarcza do wyznaczania odstępów i limitu.

**Uzasadnienie:** automatyzacja ściągania należności przy ochronie relacji z kontrahentem (rzadkie, ograniczone
wiadomości) i bez dodatkowych tabel - przypomnienia to wiadomości z kolejki D36 z regułami wyliczanymi z ich historii.

---

## D38. Powiadomienia o nowych fakturach kosztowych po synchronizacji z KSeF

**Kontekst:** w KSeF faktury zakupowe nie przychodzą e-mailem od sprzedawcy - trafiają do KSeF, a aplikacja pobiera
je cyklicznie (D26). Bez powiadomienia użytkownik musi sam zaglądać do listy faktur otrzymanych i może przeoczyć termin
płatności.

**Decyzja:**
- po synchronizacji, która pobrała nowe faktury, aplikacja dodaje do kolejki (D36) **jedną zbiorczą wiadomość**
  z listą: numer, sprzedawca z NIP, kwota, termin płatności (sortowanie po najbliższym terminie, najwyżej 20 pozycji),
- przy fakturach w PLN powyżej 15 000 zł wiadomość przypomina o sprawdzeniu rachunku sprzedawcy na białej liście
  przed zapłatą (D29),
- funkcja włączana w ustawieniach firmy (domyślnie wyłączona); adres - ustawiony w powiadomieniach albo adres konta,
- powiadomienie dotyczy wyłącznie faktur nowych w danej synchronizacji (cyklicznej i ręcznej); błąd dodania
  powiadomienia nie przerywa synchronizacji - faktury są już zapisane,
- wiadomość do użytkownika nie ma załączników ani adresu odpowiedzi i nie podlega dziennemu limitowi wiadomości
  do kontrahentów; opcjonalny link do aplikacji z konfiguracji `App:PublicUrl`.

**Alternatywy:**
- osobna wiadomość dla każdej faktury: przy większej liczbie faktur zasypuje skrzynkę,
- dzienne podsumowanie o stałej godzinie: mniej wiadomości, ale opóźnia informację o fakturze z krótkim terminem,
- powiadomienia w przeglądarce (Web Push) lub w aplikacji: wymagają otwartej aplikacji lub zgody przeglądarki
  i PWA (funkcja 14) - e-mail działa zawsze.

**Uzasadnienie:** użytkownik dowiaduje się o nowych kosztach bez logowania, a jedna wiadomość na synchronizację
(co najmniej 15 minut) ogranicza ich liczbę. Ostrzeżenie o białej liście łączy powiadomienie z obowiązkiem podatkowym.

---

## D39. Faktury cykliczne jako szkice tworzone według harmonogramu

**Kontekst:** firmy z powtarzalną sprzedażą (abonamenty, najem, obsługa serwisowa) wystawiają co okres tę samą fakturę.
Automatyczne wystawianie oszczędza czas, ale faktura przyjęta w KSeF jest ostateczna - pomyłki (np. zmieniona cena,
zakończona umowa) da się naprawić tylko korektą.

**Decyzja:**
- harmonogram (`RecurringInvoices`) wskazuje fakturę-wzór (faktura VAT firmy), okres (co 1, 3, 6 lub 12 miesięcy),
  datę najbliższego wystawienia i opcjonalną datę końcową; można go wstrzymać i wznowić,
- usługa w tle (co godzinę) tworzy w terminie **szkic** faktury - nie wysyła go do KSeF; użytkownik sprawdza szkic
  i wysyła go sam, a opcjonalnie dostaje e-mail z listą przygotowanych szkiców (D36),
- szkic powstaje tą samą ścieżką co faktura z formularza (`ApplyAsync`): aktualne dane kontrahenta z książki adresowej
  (a gdy kontrahenta usunięto - dane nabywcy ze wzoru), pozycje i warunki płatności wzoru, nowy numer, data wystawienia
  dzisiejsza, termin z tym samym odstępem co we wzorze (jak „Wystaw podobną”, D31),
- kolejne daty: ten sam dzień miesiąca, w krótszym miesiącu - ostatni dzień (31.01 → 28.02 → 31.03),
- po przestoju aplikacji (kilka pominiętych okresów) powstaje **jeden** szkic, a termin przesuwa się na najbliższy
  przyszły - bez lawiny zaległych faktur; ponowne uruchomienie tego samego dnia nie tworzy duplikatu (termin
  przesuwany w tym samym zapisie co utworzenie szkicu),
- faktury będącej wzorem nie można usunąć (409); usunięcie harmonogramu nie usuwa utworzonych faktur.

**Alternatywy:**
- automatyczna wysyłka do KSeF: pełna automatyzacja, ale każdy błąd wzoru staje się ostateczną fakturą wymagającą
  korekty; przy szkicu użytkownik ma kontrolę, a wysyłka to jedno kliknięcie - możliwy kierunek rozwoju jako opcja,
- osobna kopia danych faktury w harmonogramie (zamiast wzoru): niezależność od wzoru, ale zdublowany formularz
  pozycji i nabywcy; wzorem jest prawdziwa faktura, którą użytkownik zna,
- wyrównywanie zaległych okresów (szkic za każdy pominięty miesiąc): poprawne księgowo dla najmu, ale po dłuższym
  przestoju zaskakuje użytkownika wieloma dokumentami - zaległe faktury łatwo dodać przyciskiem „Wystaw podobną”.

**Uzasadnienie:** powtarzalne faktury przygotowują się same, a decyzja o wystawieniu dokumentu w KSeF pozostaje
po stronie użytkownika. Wykorzystanie ścieżki formularza gwarantuje te same reguły i obliczenia co przy ręcznym
wystawianiu.

---

## D40. Wykresy pulpitu rysowane w SVG bez biblioteki JavaScript

**Kontekst:** pulpit pokazywał liczniki faktur i sumy należności. Użytkownik nie widział trendu sprzedaży i kosztów
ani struktury zaległości, które konkurencyjne programy prezentują na wykresach.

**Decyzja:**
- endpoint `GET /api/dashboard` zwraca dane trzech wykresów liczone w bazie dla firmy zalogowanego użytkownika:
  sprzedaż i koszty netto w 12 ostatnich miesiącach (sprzedaż - faktury przekazane do KSeF z korektami, według daty
  wystawienia; koszty - faktury otrzymane), wiekowanie należności (w terminie, 1-30, 31-60, ponad 60 dni po terminie,
  kwota pozostała do zapłaty jak w rozliczeniach D30) i pięciu największych nabywców,
- wykres kolumnowy rysowany jako SVG w komponencie Razor, paski poziome w HTML/CSS - bez bibliotek JavaScript,
- zasady czytelności: jedna oś wartości, cienkie kolumny (do 24 px) z zaokrąglonym końcem i prostą podstawą,
  odstęp 2 px w parze kolumn, włosowa ciągła siatka, „ładne” podziałki osi (1, 2, 5 × 10ⁿ), tekst w kolorach tekstu
  (nie w kolorze serii), legenda dla dwóch serii; kolory serii z palety kategorycznej (niebieski, pomarańczowy)
  sprawdzone walidatorem pod kątem rozróżnialności przy zaburzeniach widzenia barw i kontrastu,
- dostępność: każda kolumna ma podpowiedź (`title`) i jest osiągalna klawiaturą, wykres ma opis dla czytników
  ekranu, a wszystkie wartości są w tabeli „Dane w tabeli” pod wykresem; paski poziome mają wartości wypisane obok,
- kwoty w PLN (faktury w walutach obcych - funkcja 12 - liczone osobno).

**Alternatywy:**
- biblioteka Chart.js lub ApexCharts przez JS interop: więcej typów wykresów i animacje, ale dodatkowa zależność
  JavaScript, kod w dwóch językach i trudniejsze testy; trzy proste wykresy nie wymagają biblioteki,
- biblioteki wykresów dla Blazora (np. komponenty komercyjne): wygodne, ale często płatne lub duże,
- wykres kołowy struktury należności: trudniejszy do porównania niż paski (kąty zamiast długości).

**Uzasadnienie:** brak nowych zależności, pełna kontrola nad wyglądem i dostępnością, wykresy renderowane
po stronie przeglądarki z danych jednego zapytania. Proporcje pasków sprawdzono na zrzucie ekranu (pierwsza wersja
skracała paski o szerokość etykiety kwoty - poprawione).

---

## D41. Logowanie dwuskładnikowe (TOTP) z ochroną przed wyłączeniem przez przejętą sesję

**Kontekst:** konto daje dostęp do faktur, danych kontrahentów i tokenu KSeF (wystawianie faktur w imieniu firmy).
Samo hasło chroni słabo (wycieki, powtarzane hasła). W MVP 2FA było odłożone (rozdział „Ograniczenia” w opisie
bezpieczeństwa).

**Decyzja:**
- drugi składnik: kod TOTP (RFC 6238, 6 cyfr co 30 s) z dowolnej aplikacji uwierzytelniającej; klucz i weryfikacja -
  ASP.NET Core Identity (`AuthenticatorTokenProvider`), kod QR z URI `otpauth://` generowany na serwerze (QRCoder,
  już używany do kodów KSeF) i przesyłany jako obraz PNG,
- włączenie wymaga potwierdzenia kodem z aplikacji (sprawdza, że telefon jest poprawnie skonfigurowany); po włączeniu
  10 jednorazowych kodów odzyskiwania pokazywanych tylko raz,
- logowanie dwuetapowe w interfejsie: po haśle (odpowiedź `RequiresTwoFactor`) pole na kod albo kod odzyskiwania;
  wbudowany endpoint `/api/auth/login` przyjmuje oba; blokada konta po 5 nieudanych próbach działa także dla kodów,
- **wyłączenie 2FA i nowe kody odzyskiwania wymagają aktualnego kodu** - własne endpointy `/api/account/2fa`;
  wbudowany endpoint Identity `/api/auth/manage/2fa` (pozwala wyłączyć 2FA samą sesją) jest zablokowany (404);
  po wyłączeniu klucz jest resetowany, więc stary wpis w telefonie przestaje działać,
- w testach kody liczy niezależna implementacja TOTP (HMAC-SHA1) - potwierdza zgodność ze standardem, a nie tylko
  z biblioteką.

**Alternatywy:**
- kody SMS: wymagają bramki SMS (koszt) i są podatne na przejęcie numeru (SIM swap),
- kody e-mail: prostsze dla użytkownika, ale skrzynka pocztowa często jest chroniona tym samym hasłem,
- klucze sprzętowe / passkeys (WebAuthn): najbezpieczniejsze, ale wymagają osobnej biblioteki i obsługi w przeglądarce -
  kierunek rozwoju,
- wbudowany endpoint `manage/2fa` bez zmian: mniej kodu, ale przejęta sesja (np. niezablokowany komputer) wystarcza
  do zdjęcia zabezpieczenia.

**Uzasadnienie:** standard obsługiwany przez wszystkie popularne aplikacje, bez kosztów i zewnętrznych usług;
mechanizmy kryptograficzne z Identity, a własny kod dotyczy tylko przepływu i interfejsu.

---

## D42. Faktura proforma jako rodzaj dokumentu poza KSeF, zamieniany na fakturę VAT

**Kontekst:** proforma to dokument handlowy (oferta, wezwanie do przedpłaty) - nie jest fakturą w rozumieniu ustawy
o VAT, nie podlega przesłaniu do KSeF (struktura FA(3) nie ma takiego rodzaju) i nie daje prawa do odliczenia podatku.
Małe firmy wystawiają ją przed realizacją zamówienia, a po zapłacie - fakturę VAT.

**Decyzja:**
- proforma jest rodzajem dokumentu w tej samej tabeli co faktury (`Kind = Proforma`), z własną serią numerów
  `PRO/n/MM/RRRR`; formularz i pozycje są te same co faktury (przełącznik „Faktura VAT / Faktura proforma”),
- nie jest wysyłana do KSeF (409 także przy wywołaniu API), nie ma postaci XML FA(3), nie podlega rozliczeniom,
  przypomnieniom, eksportowi, wykresom sprzedaży ani nie może być wzorem korekty czy faktury cyklicznej; nie można
  oznaczyć jej jako zapłaconej,
- PDF ma tytuł „Faktura proforma” i wyraźne oznaczenie, że nie jest fakturą VAT; proformę można wysłać e-mailem
  (tylko PDF, w treści informacja, że faktura VAT zostanie wystawiona w KSeF),
- **Wystaw fakturę VAT** tworzy szkic faktury VAT z nabywcą (aktualne dane z książki adresowej), pozycjami i warunkami
  proformy - tą samą ścieżką co formularz (wspólny kod z fakturami cyklicznymi, D39); termin płatności z proformy,
  jeśli nie minął; faktura VAT zapamiętuje proformę (`ProformaId`), a proformę można zamienić tylko raz.

**Alternatywy:**
- osobna tabela proform: pełne rozdzielenie od faktur, ale duplikacja pozycji, obliczeń, PDF i formularza,
- proforma jako status szkicu faktury: prostsze, ale szkic dostaje numer z serii FV - luka w numeracji faktur,
  gdy klient nie zapłaci,
- automatyczne wystawienie faktury VAT po zarejestrowaniu wpłaty do proformy: wygodne, ale faktura w KSeF jest
  ostateczna - użytkownik powinien sprawdzić szkic (np. datę sprzedaży).

**Uzasadnienie:** proforma korzysta z całego mechanizmu dokumentów (pozycje, PDF, e-mail), a reguły po stronie
serwera gwarantują, że nigdy nie trafi do KSeF ani do rozliczeń VAT. Zamiana na fakturę jednym przyciskiem eliminuje
przepisywanie danych.

---

## D43. Faktury w walutach obcych z kursem średnim NBP pobieranym przy zapisie

**Kontekst:** w MVP obsługiwane były tylko faktury w PLN (D21). Firmy sprzedające za granicę wystawiają faktury
w EUR, USD itd. Ustawa o VAT wymaga wtedy kwot podatku w złotych (art. 106e ust. 11), przeliczonych kursem średnim
NBP z ostatniego dnia roboczego poprzedzającego dzień powstania obowiązku podatkowego (art. 31a). FA(3) ma na to
pola `P_14_xW` (podatek w PLN dla stawek 23/22, 8/7, 5%) i `KursWaluty` w wierszach faktury.

**Decyzja:**
- waluty: PLN oraz EUR, USD, GBP, CHF, CZK, SEK, NOK, DKK, JPY, CAD (tabela A kursów średnich NBP),
- kurs ustala **serwer przy zapisie faktury**: kurs średni NBP z ostatniego dnia notowań przed dniem sprzedaży
  (w aplikacji dzień sprzedaży, a gdy nie podano - dzień wystawienia); zapis w fakturze: kurs, data i numer tabeli NBP;
  zmiana daty przy edycji szkicu pobiera kurs ponownie; formularz pokazuje podgląd kursu i podatku w PLN,
- klient API NBP (`api.nbp.pl`, publiczne, bez klucza) z zapytaniem o przedział 14 dni - wybór ostatniego notowania
  omija weekendy i święta bez kalendarza świąt w aplikacji; wyniki w pamięci podręcznej; niedostępność NBP blokuje
  zapis faktury walutowej czytelnym komunikatem (bez zgadywania kursu),
- XML: kwoty w walucie faktury, `P_14_xW` = podatek × kurs zaokrąglony do grosza (osobno dla każdej stawki),
  `KursWaluty` w każdym wierszu; PDF i podgląd pokazują podatek w PLN i kurs; faktura korygująca przyjmuje kurs
  faktury korygowanej (uproszczenie - korekty z innym kursem poza zakresem),
- kwoty w różnych walutach nie są sumowane: należności na pulpicie i w podsumowaniu rozliczeń obejmują PLN,
  eksport liczy faktury walutowe osobno (w zestawieniu z kursem i VAT w PLN), a wykres sprzedaży przelicza je kursem
  z faktury; płatności rejestruje się w walucie faktury,
- przy okazji: klient Białej listy rozpoznaje stronę zabezpieczenia przed botami (HTML zamiast JSON) - patrz niżej.

**Alternatywy:**
- kurs wpisywany ręcznie przez użytkownika: elastyczne (np. kurs z innej daty), ale podatne na pomyłki; możliwe
  rozszerzenie jako nadpisanie kursu z NBP,
- kurs pobierany przy wysyłce do KSeF zamiast przy zapisie: użytkownik nie widziałby podatku w PLN przed wysyłką,
- kalendarz dni roboczych w aplikacji: zbędny - API NBP zwraca notowania tylko z dni, w których tabela była publikowana,
- przeliczanie należności na PLN bieżącym kursem: wymaga kursów dziennych i różnic kursowych - zadanie księgowości.

**Uzasadnienie:** zgodność z art. 31a i 106e ust. 11 ustawy o VAT oraz strukturą FA(3) bez ręcznego przepisywania
kursów. Faktura w EUR z kursem pobranym na żywo z API NBP została przyjęta przez testowy KSeF.

**Obserwacja (ważna przy wdrożeniu):** zapytania do wykazu podatników VAT (`wl-api.mf.gov.pl`) wysłane z adresu IP
centrum danych w chmurze zostały zablokowane przez zabezpieczenie MF przed automatycznymi zapytaniami (strona HTML
zamiast danych). API NBP działało z tego samego adresu. Na serwerze produkcyjnym trzeba to sprawdzić; aplikacja
pokazuje wtedy czytelny komunikat, a dane kontrahenta można wpisać ręcznie (D28).

---

## D44. Import faktur sprzedażowych wystawionych poza aplikacją (z KSeF)

**Kontekst:** firma może wystawiać faktury także w innym programie, w bezpłatnej aplikacji MF albo mieć je z okresu
sprzed korzystania z aplikacji. Bez nich rozliczenia, eksport dla księgowej i wykresy są niekompletne. KSeF udostępnia
metadane faktur także w roli sprzedawcy (`Podmiot1`), więc można je pobrać tak jak faktury zakupowe.

**Decyzja:**
- przyrostowe pobieranie do HWM z osobnym punktem kontrolnym (`Company.IssuedInvoicesSyncedUntil`), w tym samym cyklu
  usługi w tle co faktury otrzymane (D26) i ręcznie przyciskiem na liście faktur (najwyżej raz na minutę),
- zaimportowana faktura jest zwykłą fakturą przyjętą w KSeF z `Origin = KsefImport`: dane i pozycje odczytane z XML,
  kwoty sum z dokumentu (`P_13_x`, `P_14_x`, `P_15`), XML zapisany jak dokument wysłany (bez UPO - jest u wystawcy);
  dzięki temu działają rozliczenia, przypomnienia, eksport, wykresy, PDF z kodem QR, e-mail i korekty,
- **deduplikacja**: pomijane są faktury o znanym numerze KSeF oraz dokumenty wysłane przez aplikację rozpoznane po
  skrócie SHA-256 (także zanim usługa wysyłki zapisze numer KSeF),
- obsługiwane rodzaje: VAT i KOR (korekta łączona z fakturą korygowaną po numerze KSeF); inne (zaliczkowe,
  rozliczeniowe, uproszczone) i dokumenty z nieobsługiwaną stawką są pomijane i zliczane,
- unikalność numeru faktury dotyczy faktur wystawionych w aplikacji - inny program mógł użyć tej samej numeracji,
- zaimportowane faktury są tylko do odczytu (nie można ich edytować, usunąć ani wysłać ponownie).

**Alternatywy:**
- osobna tabela „faktury sprzedaży z KSeF”: czystsze rozdzielenie, ale rozliczenia, eksport, wykresy i korekty musiałyby
  obsługiwać dwa źródła,
- import plików XML z dysku: nie wymaga KSeF, ale użytkownik musi je pobrać ręcznie; w KSeF i tak wszystkie faktury są,
- import wszystkich rodzajów faktur: zaliczki i rozliczenia wymagają osobnej logiki rozliczeń - poza zakresem.

**Uzasadnienie:** kompletny obraz sprzedaży niezależnie od narzędzia, którym wystawiono fakturę, z ponownym użyciem
mechanizmu odbioru (HWM, limity) i bez ryzyka zdublowania faktur wysłanych z aplikacji.

---

## D45. Aplikacja mobilna jako PWA (ta sama aplikacja, instalowana na telefonie)

**Kontekst:** konkurencyjne programy mają aplikacje mobilne. Użytkownik chce sprawdzić fakturę, zarejestrować wpłatę
albo wystawić fakturę z telefonu. Osobna aplikacja natywna oznaczałaby drugi kod (Android, iOS) i publikację w sklepach.

**Decyzja:**
- Progressive Web App: manifest (`manifest.webmanifest` - nazwa, ikony w tym „maskable”, kolor motywu, tryb
  `standalone`, skróty „Nowa faktura” i „Faktury otrzymane”) i service worker; aplikację instaluje się z przeglądarki
  („Dodaj do ekranu głównego”) - ma ikonę i otwiera się w osobnym oknie bez paska adresu,
- **pełna funkcjonalność** - to ta sama aplikacja Blazor co w przeglądarce, z układem dostosowanym do wąskich ekranów
  (menu zwijane, formularze w jednej kolumnie, tabele przewijane we własnym kontenerze),
- service worker przechowuje **wyłącznie pliki aplikacji** z listy generowanej przy publikacji (z kontrolą skrótów
  SHA-256) i usuwa stare wersje po aktualizacji; **odpowiedzi API (dane faktur, PDF, XML) nigdy nie trafiają do pamięci
  podręcznej** - dane zawsze pochodzą z serwera i nie zostają na telefonie po wylogowaniu; w wersji deweloperskiej
  service worker nic nie przechowuje (zmiany widoczne od razu),
- bez trybu offline dla danych i bez powiadomień push (powiadomienia działają e-mailem - D36-D38).

**Alternatywy:**
- aplikacja natywna (Kotlin/Swift) lub wieloplatformowa (.NET MAUI, Flutter): dostęp do funkcji telefonu i obecność
  w sklepach, ale drugi interfejs do utrzymania, osobne wydania i uwierzytelnianie tokenami zamiast ciasteczka,
- Blazor Hybrid (MAUI z komponentami Razor): współdzieli komponenty, ale wymaga publikacji w sklepach,
- przechowywanie danych offline (IndexedDB) z synchronizacją: praca bez sieci, ale dane finansowe na urządzeniu
  (ryzyko przy utracie telefonu) i konflikty synchronizacji; wystawienie faktury i tak wymaga połączenia z KSeF,
- powiadomienia Web Push: wymagają kluczy VAPID, subskrypcji per urządzenie, a na iOS - instalacji PWA.

**Uzasadnienie:** aplikacja „na telefon” bez drugiego kodu i bez sklepów, z bezpiecznym zachowaniem pamięci
podręcznej (tylko pliki aplikacji). Działanie produkcyjnego service workera sprawdzono w przeglądarce: 70 plików
aplikacji w pamięci podręcznej, żadnej odpowiedzi API, strona ładuje się po przeładowaniu z pamięci.

---

## D46. Dostęp pracowników i księgowej do firmy - role i zaproszenia

**Kontekst:** w MVP konto i firma były w relacji 1:1 (D4). W praktyce z danymi firmy pracuje też księgowa (biuro
rachunkowe obsługuje wiele firm) i pracownicy wystawiający faktury. Udostępnianie hasła właściciela byłoby niebezpieczne
i uniemożliwiałoby rozróżnienie uprawnień. Zmiana dotyka podstaw bezpieczeństwa - izolacji danych (D15).

**Decyzja:**
- właściciel firmy pozostaje w `Company.OwnerId`; dostęp innych osób to wpisy `CompanyMembers` (firma, użytkownik, rola);
  jedno konto może należeć do wielu firm i mieć własną,
- trzy role: **właściciel** (wszystko), **pracownik** (wystawianie i wysyłka faktur, kontrahenci, katalog, faktury
  cykliczne, e-maile, płatności; bez danych firmy, tokenu KSeF, powiadomień i użytkowników), **księgowa** (odczyt
  wszystkich danych, eksport, PDF/XML, rejestrowanie płatności; bez wystawiania i zmian),
- uprawnienia sprawdza **serwer** filtrem endpointów (`RequireRoleForChanges`, `RequireRole`) na grupach API - operacje
  zmieniające dane bez uprawnień zwracają 403 z nazwą roli; interfejs tylko ukrywa niedostępne przyciski,
- **bieżąca firma**: `CurrentCompany` ustala firmę z wyboru użytkownika (ciasteczko HttpOnly `ksef.company` albo nagłówek
  `X-Company-Id` dla innych klientów API), ale **wyłącznie spośród firm, do których użytkownik ma dostęp** (własnej lub
  z członkostwa) - wskazanie cudzej firmy jest ignorowane; bez wyboru - firma własna, a gdy jej nie ma, pierwsza
  z członkostwa. Zasada z D15 pozostaje: identyfikator firmy w zapytaniach pochodzi z `CurrentCompany`, a żądanie może
  tylko wybrać jedną z firm uprawnionych dla tożsamości,
- **zaproszenia**: właściciel zaprasza na adres e-mail z rolą; wiadomość zawiera jednorazowy link z losowym tokenem
  (256 bitów) ważnym 7 dni; w bazie jest tylko skrót SHA-256 tokenu; przyjęcie wymaga linku **i** zalogowania na konto
  z zapraszanym adresem - aplikacja nie potwierdza adresów e-mail przy rejestracji, więc sam adres konta nie wystarcza
  (ktoś mógłby założyć konto na cudzy adres), a link dowodzi dostępu do skrzynki; nowe zaproszenie unieważnia poprzednie,
  zaproszenie można odwołać; wiadomości podlegają dziennemu limitowi firmy (D36),
- właściciel zmienia role i odbiera dostęp - skutek od następnego żądania danej osoby,
- przełącznik firm w górnym pasku (gdy użytkownik ma dostęp do więcej niż jednej) - po zmianie aplikacja przeładowuje
  dane; usługi w tle działają na poziomie firmy i nie zależą od ról.

**Alternatywy:**
- role w ASP.NET Core Identity (`AspNetRoles`): role są globalne dla konta, a tu rola zależy od firmy (ta sama osoba
  jest właścicielem swojej firmy i księgową u klienta),
- identyfikator firmy w adresie URL (`/api/companies/{id}/invoices`): jawne, ale wymaga zmiany wszystkich endpointów
  i adresów klienta; wybór w ciasteczku działa też dla linków do pobierania plików (PDF, ZIP), które nie wysyłają nagłówków,
- przyjmowanie zaproszeń tylko po adresie konta (bez tokenu): prostsze, ale bez potwierdzania adresów e-mail
  pozwalałoby przejąć zaproszenie zakładając konto na cudzy adres,
- dostęp księgowej przez uprawnienia w KSeF (nadawane w KSeF przez właściciela): daje dostęp do faktur w KSeF,
  ale nie do danych aplikacji (rozliczenia, eksport) - uzupełnia, a nie zastępuje role aplikacji.

**Uzasadnienie:** współpraca z księgową i pracownikami bez dzielenia się hasłem, z najmniejszymi potrzebnymi
uprawnieniami. Izolacja danych pozostaje scentralizowana w `CurrentCompany`, a testy sprawdzają, że wskazanie cudzej
firmy (nagłówek, wybór) nie daje dostępu do jej danych i że odebranie dostępu działa natychmiast.

---

## D47. Utwardzenie aplikacji: nagłówki bezpieczeństwa (CSP) i limity liczby żądań

**Kontekst:** końcowy przegląd bezpieczeństwa przed wdrożeniem. Aplikacja miała ochronę na poziomie danych (izolacja,
role, walidacja), ale brakowało zabezpieczeń na poziomie przeglądarki i ochrony przed masowymi żądaniami. Blokada
konta po 5 nieudanych logowaniach (D3) chroni pojedyncze konto, ale nie zatrzymuje prób logowania na wiele kont
z jednego adresu ani masowej rejestracji. Link zaproszenia (D46) zawiera token w adresie strony.

**Decyzja:**
- nagłówki każdej odpowiedzi: **Content-Security-Policy** (`script-src 'self' 'wasm-unsafe-eval'` - tylko skrypty
  i WebAssembly z serwera aplikacji, bez skryptów wstawionych w stronę i bez `unsafe-eval`; `frame-ancestors 'none'`,
  `object-src 'none'`, `img-src 'self' data:` dla kodu QR, `connect-src 'self'`), `Referrer-Policy: no-referrer`
  (adres z tokenem zaproszenia nie wycieka), `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`,
  `Permissions-Policy` (bez kamery, mikrofonu, lokalizacji, płatności), `Cross-Origin-Opener-Policy`;
  jedyny skrypt w `index.html` (rejestracja service workera) przeniesiony do pliku,
- `style-src` dopuszcza `'unsafe-inline'` - komponenty używają atrybutów `style` (np. szerokość pasków wykresów);
  wstrzyknięcie stylu nie wykonuje kodu, a skrypty pozostają zablokowane,
- **limity żądań** (wbudowany `RateLimiter` ASP.NET Core): logowanie, rejestracja, odświeżanie tokenu, podgląd
  i przyjęcie zaproszenia - 10 na minutę z adresu IP; całe API - 600 na minutę na użytkownika (lub adres IP
  niezalogowanego); przekroczenie - 429 z nagłówkiem `Retry-After`; wartości w konfiguracji (`RateLimiting:*`),
- weryfikacja: test E2E przechodzi przez główne strony i nasłuchuje zdarzeń `securitypolicyviolation` - brak naruszeń;
  wszystkie scenariusze E2E działają z włączoną polityką.

**Alternatywy:**
- CSP z `nonce` dla skryptów w stronie: niepotrzebne po przeniesieniu jedynego skryptu do pliku,
- limity w odwrotnym proxy (nginx, Caddy): działają przed aplikacją, ale nie znają użytkownika (tylko adres IP)
  i są poza kodem testowanym w CI - limity w aplikacji można je uzupełnić,
- CAPTCHA przy rejestracji: skuteczna przeciw botom, ale wymaga zewnętrznej usługi i utrudnia dostępność.

**Uzasadnienie:** obrona wielowarstwowa - nawet jeśli błąd pozwoliłby wstrzyknąć treść do strony, przeglądarka nie wykona
obcego skryptu; zgadywanie haseł i tokenów z jednego adresu jest spowolnione niezależnie od kont.

---

## D48. Wdrożenie w kontenerach: obraz aplikacji, PostgreSQL i Caddy z automatycznym HTTPS

**Kontekst:** aplikacja ma działać w modelu SaaS na serwerze, z wymaganym HTTPS, bez sekretów w repozytorium.
Faktyczne wdrożenie odłożono, ale konfiguracja ma być gotowa, powtarzalna i sprawdzona.

**Decyzja:**
- jeden obraz aplikacji (API serwuje też klienta Blazor WebAssembly - D6): budowanie w obrazie SDK, uruchamianie
  w obrazie ASP.NET jako użytkownik bez uprawnień administratora; kontrola stanu `/api/health`,
- `docker-compose.prod.yml`: PostgreSQL (wolumen, bez opublikowanego portu), aplikacja (bez opublikowanego portu),
  **Caddy** jako odwrotne proxy z automatycznym certyfikatem Let's Encrypt i przekierowaniem HTTP → HTTPS,
- konfiguracja i sekrety wyłącznie w zmiennych środowiskowych z pliku `.env` spoza repozytorium; aplikacja w trybie
  produkcyjnym **odmawia startu z hasłem deweloperskim do bazy**,
- za proxy: nagłówki `X-Forwarded-For/Proto` (adres klienta dla limitów żądań, schemat dla ciasteczek `Secure`),
  `AllowedHosts` ograniczone do domeny, HSTS,
- klucze Data Protection w bazie dodatkowo szyfrowane certyfikatem z pliku montowanego do kontenera (rozwiązuje
  ograniczenie z D23: kopia bazy bez certyfikatu nie pozwala odszyfrować tokenów KSeF),
- migracje przy starcie z ponawianiem; kopie zapasowe skryptem `pg_dump` z rotacją,
- CI buduje obraz przy każdej zmianie i sprawdza blokadę hasła deweloperskiego.

**Alternatywy:**
- nginx + certbot: popularne, ale certyfikat i jego odnawianie konfiguruje się osobno; Caddy robi to automatycznie,
- usługa PaaS (Azure App Service, Railway) i zarządzana baza: mniej administracji, ale koszt i uzależnienie od dostawcy;
  kontenery można przenieść na dowolny serwer lub do takiej usługi,
- Kubernetes: nadmiarowy dla jednej aplikacji i jednej bazy,
- certyfikat TLS w samej aplikacji (Kestrel): wymaga obsługi odnawiania w aplikacji; proxy oddziela TLS od kodu.

**Uzasadnienie:** cały serwer uruchamia się jednym poleceniem z jednego pliku konfiguracji, z HTTPS bez ręcznej obsługi
certyfikatów i z sekretami poza repozytorium. Konfigurację uruchomiono w kontenerach i sprawdzono (docs/wdrozenie.md).

---

## D49. Dane demonstracyjne dla środowiska deweloperskiego (konta testowe z fakturami)

**Kontekst:** do prezentacji (np. na obronie) i ręcznych testów potrzebne są gotowe konta z kompletnymi danymi:
firmy, kontrahenci, faktury w różnych stanach i faktury wymieniane między kontami. Ręczne przygotowanie tego po każdym
wyczyszczeniu bazy zajmuje dużo czasu, a pusta aplikacja nie pokazuje pulpitu, rozliczeń ani wykresów.

**Decyzja:**
- usługa `DemoDataSeeder` uruchamiana przy starcie, gdy `DemoData:Seed=true` - **wyłącznie w środowisku Development**
  (w innym środowisku aplikacja odmawia startu z tym ustawieniem); dane powstają raz (istnienie konta TestUser1),
- cztery konta `TestUser1-4@example.com` (domena zarezerwowana - poczta nie trafi do przypadkowych osób) z firmami
  o różnych profilach, kontrahentami (pozostałe firmy i osoba prywatna) i katalogiem; opcjonalnie konto autora
  z adresem i hasłem w user-secrets (`DemoData:Owner:*`), z włączonymi powiadomieniami - poza repozytorium,
- **dwa rodzaje faktur:** historia z ostatnich miesięcy oznaczona jako przyjęta w KSeF z przykładowym numerem KSeF
  (bez wysyłki; opis w statusie wysyłki), z lustrzaną fakturą otrzymaną u nabywcy, płatnościami, korektą, proformami,
  fakturą walutową, cykliczną i odrzuconą - oraz **szkice z bieżącą datą**, które użytkownik wysyła sam do testowego
  KSeF; tokeny testowego KSeF generowane przy starcie mechanizmem D24 (gdy KSeF nie odpowiada - pomijane),
- faktury powstają tą samą ścieżką co z formularza (`ApplyAsync`, numeracja, generator FA(3) z walidacją XSD),
  więc PDF, podgląd, eksport i wykresy działają na danych demonstracyjnych tak jak na prawdziwych,
- NIP-y firm i rachunki losowe (z poprawnymi sumami kontrolnymi) przy każdym wgraniu - po wyczyszczeniu bazy
  wysłanie do testowego KSeF faktur o tych samych numerach nie koliduje z wcześniej wysłanymi,
- daty liczone względem dnia uruchomienia - zawsze są należności po terminie i dane na wykresie.

**Alternatywy:**
- skrypt SQL z danymi: szybki, ale omija logikę aplikacji (kwoty, XML, numeracja) i rozjeżdża się z modelem przy
  każdej migracji,
- dane w migracji EF (`HasData`): trafiłyby także na produkcję,
- wysyłanie całej historii do testowego KSeF przy starcie: prawdziwe numery i UPO, ale kilka minut oczekiwania,
  zależność od dostępności KSeF i obciążanie środowiska testowego MF przy każdym resecie bazy; dlatego prawdziwa
  wysyłka dotyczy tylko szkiców wysyłanych świadomie przez użytkownika.

**Uzasadnienie:** jedno uruchomienie daje aplikację gotową do pokazania wszystkich funkcji, a szkice pozwalają
pokazać pełny obieg z prawdziwym (testowym) KSeF: wysyłkę, numer KSeF, UPO, kod QR i odbiór faktury przez nabywcę.
Dane i konta nie mogą trafić na produkcję.

---

## D50. Nowy interfejs: własny design system na Bootstrap 5.3, kolor „Szmaragd”, tryb ciemny

**Kontekst:** interfejs powstał na szablonie Blazora (fioletowy gradient, Bootstrap 5.1) - działał, ale wyglądał jak
szkic. Cel autora: nowoczesny, a zarazem bardzo prosty w obsłudze program, z trybem ciemnym i pełnoprawną wersją na
telefon. Kierunek wybrano na klikalnych makietach (pulpit, lista faktur, formularz, telefon) w trzech wariantach koloru
(Szmaragd, Fiolet, Turkus) - wybrany Szmaragd, bo dobrze wygląda w obu trybach i kojarzy się z „w porządku / zapłacone”.

**Decyzja:**
- **design system jako zmienne CSS** (`wwwroot/css/app.css`): kolory, powierzchnie, cienie, promienie, kolory statusów
  i serii wykresów - osobno dla trybu jasnego i ciemnego (ciemny ma własne, sprawdzone odcienie, a nie odwrócenie);
  te same zmienne zasilają Bootstrapa 5.3 (`--bs-*`), więc wszystkie istniejące komponenty przyjmują nowy wygląd,
- **Bootstrap 5.3** zamiast 5.1 - natywny tryb ciemny (`data-bs-theme`), bez JavaScriptu Bootstrapa,
- **tryb ciemny**: wybór użytkownika w `localStorage`, bez wyboru - ustawienie systemu; motyw ustawia mały skrypt
  `js/theme.js` w `<head>` przed narysowaniem strony (brak mignięcia jasnego tła), przełącznik w górnym pasku,
- **czcionka Inter** serwowana lokalnie (licencja SIL OFL) - CSP nie dopuszcza zewnętrznych czcionek (D47),
- **ikony** jako komponent `Icon` z liniowymi ścieżkami SVG w kodzie - bez biblioteki ikon i dodatkowych plików,
- **układ**: menu boczne pogrupowane według zadań (Sprzedaż / Zakupy / Raporty / Ustawienia) z wyróżnionym przyciskiem
  „Nowa faktura”, karta aktywnej firmy, górny pasek z wyszukiwarką faktur, oznaczeniem testowego KSeF, przełącznikiem
  motywu i kontem; na telefonie menu wysuwane z boku,
- **zasady**: jeden główny przycisk na ekranie, statusy zawsze tekstem i kolorem (plakietki `pill-*` o kontraście
  ≥ 4,5:1), cele dotykowe ≥ 40 px, widoczny fokus klawiatury, kolory wykresów sprawdzone walidatorem palety (D40).

**Alternatywy:**
- biblioteki komponentów Blazora (MudBlazor, Fluent UI Blazor): gotowy wygląd, ale własne style w atrybutach i skrypty
  wymagają poluzowania CSP, dużo kodu do pobrania przez przeglądarkę i trudniejsze dopasowanie do własnego stylu,
- Tailwind CSS: wymaga etapu budowania (Node.js) w projekcie .NET i przepisania wszystkich szablonów naraz,
- pozostanie przy wyglądzie szablonu: najmniej pracy, ale słabe wrażenie i brak trybu ciemnego.

**Realizacja (5 etapów, osobne gałęzie):** fundament (tokeny, układ, menu, tryb ciemny); pulpit z kartą „Do zrobienia”
(aplikacja podpowiada następny krok: szkice do wysłania, odrzucone, po terminie, brak tokenu KSeF) i listy z zakładkami
statusów, na telefonie jako karty; formularz faktury w trzech krokach z podsumowaniem przyklejonym z boku; strona faktury
jako dokument z panelem bocznym (rozliczenie, e-mail, dokumenty i dalsze kroki); logowanie z panelem opisu aplikacji,
dolny pasek nawigacji na telefonie z przyciskiem „+”, link „Przejdź do treści”, nowe ikony PWA oraz czytelny komunikat
przy przekroczeniu limitu prób logowania (429) zamiast mylącego „Nieprawidłowy e-mail lub hasło”.

**Poprawka po wdrożeniu u autora:** po aktualizacji przeglądarka użyła starego `index.html` i `app.css` z pamięci
podręcznej (serwer nie wysyłał nagłówka `Cache-Control`, więc przeglądarka sama uznała pliki za aktualne) razem
z nowym kodem Blazora - strona bez stylów i błąd skryptu motywu. Serwer wysyła teraz dla plików bez odcisku w nazwie
(`index.html`, CSS, JS, manifest) `Cache-Control: no-cache` - przeglądarka zawsze sprawdza ETag (tania odpowiedź 304),
a przełącznik motywu nie przerywa działania aplikacji, gdy skrypt motywu jest niedostępny.

**Uzasadnienie:** nadpisanie zmiennych Bootstrapa daje spójny wygląd całej aplikacji od razu, a strony można
przebudowywać etapami (listy i pulpit, formularze, podglądy, konto i telefon) bez okresu „pół na pół”. Rozwiązanie
nie dodaje zależności uruchomieniowych ani wyjątków w polityce bezpieczeństwa treści.

---

## D51. NIP firmy a token KSeF: zmiana NIP tylko przed pierwszą wymianą z KSeF, odrzucenie tokenu bez ponawiania

**Kontekst:** w testach u autora wysyłka kończyła się ponawianiem z komunikatem klienta MF „Token nie może być użyty
w kontekście nip-…” (status 450). Token KSeF jest wydawany dla konkretnego NIP (kontekstu), a aplikacja pozwalała
zmienić NIP firmy (np. przez „Pobierz z wykazu VAT”) bez żadnych konsekwencji dla zapisanego tokenu. Klient MF zgłasza
ostateczne odrzucenie tokenu jako przekroczenie czasu oczekiwania, więc usługa w tle traktowała je jak błąd sieci
i ponawiała wysyłkę do 10 razy.

**Decyzja:**
- zmiana NIP firmy **usuwa zapisany token KSeF** oraz punkty kontrolne pobierania faktur (otrzymanych i wystawionych
  poza aplikacją) - użytkownik generuje lub wkleja token dla nowego NIP,
- zmiana NIP jest **niedozwolona (409)**, gdy firma ma już faktury wysłane do KSeF lub pobrane z KSeF - NIP jest
  tożsamością firmy w KSeF (sprzedawca na wysłanych fakturach, nabywca faktur otrzymanych); dla innego NIP zakłada się
  nową firmę (aplikacja obsługuje wiele firm na jednym koncie, D46),
- odrzucenie tokenu przez KSeF (status 4xx w komunikacie klienta MF) jest zamieniane na błąd uwierzytelnienia:
  wysyłka jest przerywana od razu z czytelnym opisem - bezpiecznie, bo faktura nie została jeszcze przyjęta przez KSeF
  (zasada z D25 dotyczy błędów po przyjęciu).

**Alternatywy:**
- automatyczne generowanie nowego tokenu testowego po zmianie NIP: wygodne, ale ukrywa ważne zdarzenie i nie działa
  dla tokenów wklejonych z Aplikacji Podatnika,
- dowolna zmiana NIP z zachowaniem historii: dane dwóch podmiotów w jednej firmie (faktury z innym sprzedawcą,
  synchronizacja z punktem kontrolnym innego NIP),
- pozostawienie ponawiania: dziesięć bezcelowych prób uwierzytelnienia i niezrozumiały komunikat dla użytkownika.

**Uzasadnienie:** token i dane w KSeF są związane z NIP, więc spójność wymaga, aby zmiana NIP unieważniała token,
a po pierwszej wymianie dokumentów była niemożliwa. Ostateczny błąd KSeF powinien od razu dawać użytkownikowi
wskazówkę, co zrobić.

