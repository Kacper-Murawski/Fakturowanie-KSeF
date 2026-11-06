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

## D4. Jedno konto użytkownika = jedna firma (w MVP)

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

## D15. Izolacja danych przez firmę ustalaną z tożsamości użytkownika

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

## D21. Generowanie XML FA(3): własny generator na `XmlWriter` + walidacja XSD przed każdym wydaniem

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

