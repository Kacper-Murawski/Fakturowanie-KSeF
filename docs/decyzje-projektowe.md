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
