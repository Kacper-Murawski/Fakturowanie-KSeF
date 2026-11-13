# Raport: Obszar 06 - frontend (Blazor WebAssembly)
Status: ZAKOŃCZONY
Ostatnia aktualizacja: 2026-11-12

## Postęp
- [x] Kontekst: zasady, decyzje D13, D18-D22, D40, D45, D50 (D45 i D22 w całości, pozostałe w zakresie potrzebnym do oceny znalezisk)
- [x] Services: ApiClient, AuthService, CompanyContext, Format; Program.cs
- [x] Components: wszystkie (`*.razor`, `DeepValidator.cs`, `InputTextLive.cs`)
- [x] Layout: MainLayout, NavMenu; App.razor
- [x] Pages: wszystkie 19 plików
- [x] wwwroot: index.html, js/theme.js, js/pwa.js, service-worker.js, service-worker.published.js, manifest.webmanifest; csproj
- [x] app.css: martwe reguły (skrypt porównujący klasy z plikami .razor), kontrast (pomiar w przeglądarce)
- [x] Testy E2E: pokrycie (lista scenariuszy `UserScenariosE2ETests.cs`)
- [x] Weryfikacja w przeglądarce: UI-1, UI-2, UI-3, UI-4, UI-5, UI-6, UI-8 (Chromium + Playwright, kopia robocza zbudowana poza repozytorium, baza `ksef_review06` - usunięta, pliki tymczasowe usunięte)
Następny krok: brak - przegląd zakończony.

## Podsumowanie

Frontend jest spójny i w „szczęśliwej ścieżce” działa poprawnie: błędy walidacji serwera trafiają do pól (także pozycji
faktury), przyciski zapisu są blokowane na czas żądania, kultura pl-PL nie psuje liczb, kontrast w obu motywach spełnia
4,5:1, a po stronie klienta nie ma nowych luk bezpieczeństwa (jedyny `MarkupString` to stałe ikony, w `localStorage`
tylko motyw). Słabe są natomiast ścieżki nietypowe: **wygaśnięcie sesji** (pętla logowania przez zagnieżdżony
`returnUrl`), **brak sieci** (nieobsłużony wyjątek przy logowaniu, znikająca faktura podczas odpytywania statusu),
**wyścigi** (wyniki starszego wyszukiwania nadpisują nowsze, odpytywanie KSeF trwające po opuszczeniu strony).
Najbardziej dotkliwy w codziennym użyciu jest błąd podpowiedzi z katalogu, który podczas pisania nazwy pozycji zjada
spację i nadpisuje ręcznie wpisaną cenę. Znaleziska: **0 krytycznych, 2 wysokie, 9 średnich, 9 niskich**.
Siedem z nich potwierdzono w przeglądarce.

## Znaleziska

### UI-1. Wygaśnięcie sesji: zagnieżdżony `returnUrl` i formularz logowania wewnątrz aplikacji - po zalogowaniu znów „Logowanie”
- **Waga:** wysokie
- **Rodzaj:** błąd
- **Pewność:** potwierdzone (przeglądarka: usunięcie ciasteczka sesji i klik w menu)
- **Miejsce:** `FakturowanieKSeF.Client/Services/ApiClient.cs:60-67`, `FakturowanieKSeF.Client/Services/AuthService.cs:17-22` (`_cached`), `FakturowanieKSeF.Client/Pages/Login.razor:91-97`
- **Problem:** Każde żądanie zakończone 401 woła `NavigateTo("login?returnUrl=" + bieżący adres)`. Strona, która wykonuje kilka żądań po kolei, przy drugim 401 jest już pod adresem `login?returnUrl=X`, więc `returnUrl` zagnieżdża się. Dodatkowo `ApiClient` nie unieważnia stanu w `AuthService` - aplikacja nadal uważa użytkownika za zalogowanego i rysuje formularz logowania w środku powłoki (menu boczne, wyszukiwarka, e-mail użytkownika).
- **Scenariusz:** sesja wygasa (8 h bezczynności) → klik „Faktury otrzymane” (2 żądania) → adres `/login?returnUrl=login%3FreturnUrl%3Dfaktury-otrzymane` → poprawne logowanie → adres `/login?returnUrl=faktury-otrzymane` i nadal strona „Logowanie” (użytkownik jest zalogowany, ale widzi formularz). Pulpit działa tylko przypadkiem (`returnUrl` puste).
- **Propozycja poprawki:** w `ApiClient` po 401: `auth.Refresh()` (stan → niezalogowany, `RedirectToLogin` zrobi resztę) i pojedyncze przekierowanie; nie przekierowywać, gdy bieżący adres to już `login`; w `Login.razor` przekierować dalej, gdy użytkownik jest już zalogowany.
- **Test, który to wykryje:** E2E `SesjaWygasla_PrzekierowanieNaLogowanie_IPowrotNaStrone`: zaloguj, `context.ClearCookiesAsync()`, klik „Faktury otrzymane”, zaloguj ponownie → adres `/faktury-otrzymane`, brak menu bocznego na stronie logowania.

### UI-2. Podpowiedź z katalogu przy każdym znaku: zjada spację w nazwie pozycji i nadpisuje ręcznie wpisaną cenę
- **Waga:** wysokie
- **Rodzaj:** błąd
- **Pewność:** potwierdzone (przeglądarka)
- **Miejsce:** `FakturowanieKSeF.Client/Pages/InvoiceEdit.razor:269` (`@bind-Value:after="() => FillFromCatalog(item)"`), `:676-688`
- **Problem:** `FillFromCatalog` działa po każdym naciśnięciu klawisza (`InputTextLive` = `oninput`). Gdy tekst po `Trim()` równa się nazwie z katalogu (bez rozróżniania wielkości liter), nazwa jest zastępowana nazwą katalogową, a jednostka, cena i stawka VAT - wartościami z katalogu.
- **Scenariusz:** (1) katalog ma „Druk ulotek A5”; wpisanie „Druk ulotek A5 premium” daje „Druk ulotek A5premium” (spacja znika w chwili wpisania, bo „…A5 ” po `Trim()` pasuje do katalogu). (2) Pozycja z katalogu z ręcznie zmienioną ceną 999: dopisanie i skasowanie jednego znaku w nazwie przywraca cenę katalogową 0,45 bez ostrzeżenia - na fakturze ląduje inna cena, niż wpisał użytkownik. Dotyczy też edycji szkicu i „Wystaw podobną”.
- **Propozycja poprawki:** uzupełniać tylko przy wyborze podpowiedzi (zdarzenie `onchange` pola albo porównanie bez `Trim()` i tylko gdy cena/jednostka są jeszcze domyślne); nigdy nie nadpisywać nazwy wpisywanej przez użytkownika.
- **Test, który to wykryje:** E2E w `KatalogProduktow_…`: wpisz znak po znaku „<nazwa z katalogu> premium” → wartość pola identyczna; ustaw cenę ręcznie, edytuj nazwę → cena bez zmian.

### UI-3. Odpytywanie statusu faktury: trwa po opuszczeniu strony, a jeden błąd sieci usuwa fakturę z ekranu
- **Waga:** średnie
- **Rodzaj:** błąd / wydajność
- **Pewność:** potwierdzone (przeglądarka, przechwycone żądania)
- **Miejsce:** `FakturowanieKSeF.Client/Pages/InvoiceDetails.razor:335-351, 369`; ten sam wzorzec `FakturowanieKSeF.Client/Components/InvoiceEmailPanel.razor:97-106, 135`
- **Problem:** (a) `Dispose` usuwa tylko bieżący timer; jeśli żądanie odświeżające jest w locie, `LoadAsync` po powrocie wywołuje `ScheduleRefresh()` i tworzy nowy timer na usuniętym komponencie - pętla trwa do zmiany statusu (dla `Accepted` bez UPO: bez końca). (b) `LoadAsync` przy każdym błędzie ustawia `invoice = null`: pojedynczy błąd sieci podczas odpytywania zastępuje całą fakturę komunikatem „Brak połączenia z serwerem” i kończy odpytywanie; nadpisuje też `problem` z wcześniejszej akcji. (c) Dla `Accepted` bez UPO odpytywanie co 3 s nie ma limitu ani rosnącego odstępu, a każde żądanie pobiera pełny DTO (por. PERF-4, KSEF-5).
- **Scenariusz:** (a) faktura `Pending`, opuszczenie strony w trakcie żądania → na stronie „Kontrahenci” zarejestrowano 4 kolejne `GET /api/invoices/{id}` w 15 s; (b) jedno przerwane żądanie → brak nagłówka i arkusza faktury, 0 kolejnych żądań.
- **Propozycja poprawki:** flaga `disposed` (lub `CancellationTokenSource`) sprawdzana przed `ScheduleRefresh`; w odświeżaniu w tle zachować dotychczasowe dane przy błędzie i ponowić; odstęp rosnący (3 s → 30 s) i limit czasu; lekki endpoint statusu.
- **Test, który to wykryje:** test komponentu (bUnit) albo E2E z `page.RouteAsync`: przerwij jedno żądanie odświeżające → arkusz faktury nadal widoczny; opuść stronę w trakcie żądania → brak dalszych żądań `api/invoices/{id}`.

### UI-4. `step="0.01"` na cenie netto blokuje zapis cen z więcej niż 2 miejscami - bez komunikatu aplikacji
- **Waga:** średnie
- **Rodzaj:** błąd
- **Pewność:** potwierdzone (przeglądarka + API: `POST /api/invoices` z ceną 5.1234 → 201)
- **Miejsce:** `FakturowanieKSeF.Client/Pages/InvoiceEdit.razor:283`, `FakturowanieKSeF.Client/Pages/Products.razor:77`
- **Problem:** Serwer i baza przyjmują ceny jednostkowe z większą precyzją (FA(3) dopuszcza do 8 miejsc), ale atrybut `step="0.01"` uruchamia natywną walidację przeglądarki: formularz się nie wysyła, pojawia się dymek przeglądarki (w Chromium po angielsku: „Please enter a valid value. The two nearest valid values are 5.12 and 5.13.”), a aplikacja nie pokazuje nic.
- **Scenariusz:** szkic z ceną 5,1234 (paliwo, cena przeliczona, faktura utworzona przez API, „Wystaw podobną” lub korekta faktury zaimportowanej z KSeF - D44) → „Edytuj” → „Zapisz” bez żadnej zmiany: nic się nie dzieje.
- **Propozycja poprawki:** `step="any"` (jak przy ilości) i ewentualne ograniczenie precyzji atrybutem walidacji w `Contracts` (wspólnym dla klienta i API - zob. kandydat FAK-B z obszaru 04).
- **Test, który to wykryje:** E2E: pozycja z ceną `5.1234` → zapis → szczegóły faktury pokazują cenę; edycja i ponowny zapis działają.

### UI-5. Logowanie, rejestracja i wylogowanie bez obsługi braku sieci - nieobsłużony wyjątek i zablokowany przycisk
- **Waga:** średnie
- **Rodzaj:** błąd
- **Pewność:** potwierdzone (przeglądarka: przerwane żądanie `api/auth/login`)
- **Miejsce:** `FakturowanieKSeF.Client/Services/AuthService.cs:52, 87, 97`; `FakturowanieKSeF.Client/Pages/Login.razor:84-87`, `FakturowanieKSeF.Client/Pages/Register.razor:60-70`, `FakturowanieKSeF.Client/Layout/MainLayout.razor:123-128`
- **Problem:** `AuthService` woła `HttpClient` bezpośrednio, bez `try/catch` (inaczej niż `ApiClient`). `HttpRequestException` przerywa obsługę zdarzenia po `busy = true`.
- **Scenariusz:** telefon bez zasięgu / restart serwera → „Zaloguj się” → pasek „Wystąpił nieoczekiwany błąd”, przycisk na stałe „Logowanie…” (disabled), brak komunikatu; to samo przy rejestracji; „Wyloguj” bez sieci nie wylogowuje i nie informuje. Dodatkowo `GetAuthenticationStateAsync` (`:26-40`) traktuje błąd sieci i każdy status ≠ 2xx jak „niezalogowany” i zapamiętuje to (por. SEC-3 dla 429).
- **Propozycja poprawki:** przenieść wywołania do wspólnej ścieżki z obsługą wyjątków (zwracać `LoginResult` z komunikatem „Brak połączenia…”), `busy` zwalniać w `finally`; w `ApiClient.SendAsync` łapać też `TaskCanceledException`.
- **Test, który to wykryje:** E2E z `page.RouteAsync("**/api/auth/login**", r => r.AbortAsync())` → widoczny komunikat, przycisk aktywny, brak `#blazor-error-ui`.

### UI-6. Listy z filtrami: wynik starszego zapytania nadpisuje nowszy; opóźnione wyszukiwanie zmienia adres innej strony
- **Waga:** średnie
- **Rodzaj:** błąd
- **Pewność:** potwierdzone (przeglądarka, opóźniona odpowiedź dla pierwszej frazy)
- **Miejsce:** `FakturowanieKSeF.Client/Pages/Invoices.razor:169, 186-206, 241-253`; `FakturowanieKSeF.Client/Pages/ReceivedInvoices.razor:159-176, 195-216`; `FakturowanieKSeF.Client/Pages/Customers.razor` (`SearchAsync`/`LoadAsync`); `FakturowanieKSeF.Client/Pages/InvoiceEdit.razor:589-595, 631-640`
- **Problem:** (a) Każda zmiana filtra uruchamia nowe `LoadAsync` bez anulowania poprzedniego i bez sprawdzenia, czy odpowiedź dotyczy bieżących parametrów. (b) `CancellationTokenSource` opóźnienia nie jest anulowany przy opuszczeniu strony (brak `IDisposable`), a `UpdateUrl` używa bieżącego adresu. (c) Każda fraza to nowy wpis historii (brak `replace: true`). (d) To samo dla podglądu numeru i kursu NBP w formularzu faktury przy szybkiej zmianie dat/waluty.
- **Scenariusz:** (a) wpisanie „FV”, po chwili „zzzz”; odpowiedź dla „FV” wraca później → pole „FVzzzz”, adres `?q=FVzzzz`, a tabela pokazuje 18 faktur zamiast „Brak faktur”. (b) wpisanie „ab” i natychmiastowy klik „Katalog” → adres `/katalog?q=ab`.
- **Propozycja poprawki:** licznik wersji lub `CancellationToken` na żądanie (odrzucać nieaktualne odpowiedzi), `Dispose` anulujący opóźnienie, `NavigateTo(url, replace: true)` dla wyszukiwania.
- **Test, który to wykryje:** E2E z opóźnieniem odpowiedzi dla pierwszej frazy → po ustaniu ruchu tabela odpowiada frazie w polu.

### UI-7. Zaproszona osoba bez konta po rejestracji trafia do zakładania własnej firmy - zaproszenie „znika”
- **Waga:** średnie
- **Rodzaj:** błąd
- **Pewność:** potwierdzone (analiza kodu; scenariusz E2E `DostepKsiegowej_…` używa konta już istniejącego)
- **Miejsce:** `FakturowanieKSeF.Client/Pages/Login.razor:63` (link `register` bez `returnUrl`), `FakturowanieKSeF.Client/Pages/Register.razor:65-68` (zawsze `NavigateTo("firma")`)
- **Problem:** Link z zaproszenia prowadzi niezalogowanego na `login?returnUrl=zaproszenie?token=…`, ale przejście do rejestracji gubi `returnUrl`, a po rejestracji aplikacja zawsze otwiera formularz „Moja firma”.
- **Scenariusz:** księgowa bez konta klika link → „Zarejestruj się” → po rejestracji widzi formularz danych własnej firmy (z rolą „właściciel”), nic o zaproszeniu; musi wrócić do e-maila i kliknąć link ponownie. Jeśli najpierw założy firmę „dla świętego spokoju”, powstaje zbędna firma; w odwrotnej kolejności działa ISO-3.
- **Propozycja poprawki:** przekazywać `returnUrl` do `register` i po rejestracji nawigować do niego (z tą samą kontrolą adresu względnego co przy logowaniu - SEC-2).
- **Test, który to wykryje:** E2E: link zaproszenia w czystym kontekście → rejestracja nowego konta → strona „Zaproszenie do firmy” z przyciskiem „Przyjmij zaproszenie”.

### UI-8. Wiersz listy jako `<tr role="button">`: klik w numer faktury dodaje dwa wpisy historii („wstecz” nie wraca do listy)
- **Waga:** średnie
- **Rodzaj:** błąd / dostępność
- **Pewność:** potwierdzone (przeglądarka: `history.length` +2, „wstecz” zostaje na `/faktury/59`)
- **Miejsce:** `FakturowanieKSeF.Client/Pages/Invoices.razor:97-99, 239`; `FakturowanieKSeF.Client/Pages/ReceivedInvoices.razor:107-109, 207`
- **Problem:** Klik w link numeru wykonuje nawigację linku i - przez propagację do `<tr @onclick>` - drugą nawigację `Open(id)` pod ten sam adres. Ponadto `role="button"` na `<tr>` usuwa semantykę wiersza tabeli (czytnik ekranu traci komórki i nagłówki), a wiersz nie jest osiągalny z klawiatury.
- **Scenariusz:** lista faktur → klik w numer → „wstecz” w przeglądarce: nadal ta sama faktura; do listy trzeba cofnąć dwa razy.
- **Propozycja poprawki:** usunąć `role="button"` i `@onclick` z wiersza; rozciągnąć link na cały wiersz w CSS (`.row-link::after { position:absolute; inset:0 }`) albo dodać `@onclick:stopPropagation` na linku.
- **Test, który to wykryje:** E2E: klik w numer → `page.GoBackAsync()` → adres `/faktury`.

### UI-9. Błąd pobierania danych wygląda jak „brak danych”
- **Waga:** średnie
- **Rodzaj:** błąd
- **Pewność:** potwierdzone (analiza kodu: `ApiResult.Value` jest `null` zarówno dla 404, jak i dla błędu sieci/500/403)
- **Miejsce:** `FakturowanieKSeF.Client/Pages/InvoiceEdit.razor:431-436` (`companyMissing`), `FakturowanieKSeF.Client/Pages/Company.razor:98-106`, `FakturowanieKSeF.Client/Pages/Home.razor` (`OnInitializedAsync`: `company is null` → ekran powitalny; `payments`/`dashboard` bez `Problem`), `FakturowanieKSeF.Client/Pages/Invoices.razor:197`, `FakturowanieKSeF.Client/Pages/ReceivedInvoices.razor:175`, `FakturowanieKSeF.Client/Services/CompanyContext.cs:28-33`
- **Problem:** Strony rozróżniają tylko „jest wartość / nie ma wartości”. Przejściowy błąd `GET /api/company` daje: w formularzu faktury „uzupełnij dane swojej firmy”, na pulpicie ekran pierwszej konfiguracji, a w „Moja firma” **pusty formularz nowej firmy** (bez komunikatu). Listy pokazują „Brak faktur”. `CompanyContext` po błędzie zapamiętuje pustą listę firm na cały czas życia aplikacji (rola = właściciel, przyciski widoczne dla księgowej, brak przełącznika firm).
- **Scenariusz:** chwilowy błąd 500/sieci przy wejściu na „Moja firma” → właściciel widzi puste pola nazwy, NIP i adresu swojej istniejącej firmy.
- **Propozycja poprawki:** używać `result.Problem` (pokazać `ProblemAlert` i przycisk „Spróbuj ponownie”), „brak firmy” rozpoznawać po statusie; `CompanyContext` nie powinien zapamiętywać nieudanego wczytania.
- **Test, który to wykryje:** E2E/bUnit: `GET api/company` → 500 → widoczny komunikat błędu, brak formularza zakładania firmy.

### UI-10. Utrata wypełnionego formularza faktury (wygaśnięcie sesji, przypadkowa nawigacja)
- **Waga:** średnie
- **Rodzaj:** błąd
- **Pewność:** potwierdzone (analiza kodu) - skutek 401 jak w UI-1
- **Miejsce:** `FakturowanieKSeF.Client/Pages/InvoiceEdit.razor:652-663` (i podglądy `:589-595`, `:631-640`), `FakturowanieKSeF.Client/Services/ApiClient.cs:60-67`; brak `NavigationLock` w całym kliencie
- **Problem:** Formularz faktury (wiele pozycji) nie ma żadnej ochrony stanu. Po wygaśnięciu sesji kliknięcie „Zapisz” - a nawet sama zmiana daty lub waluty (podgląd numeru/kursu) - kończy się 401 i natychmiastowym przejściem do logowania; po zalogowaniu `returnUrl` otwiera pusty formularz. Tak samo działa klik w menu, „Enter” w globalnej wyszukiwarce i „wstecz”.
- **Scenariusz:** użytkownik wraca po przerwie do otwartej karty z rozpoczętą fakturą na 15 pozycji, dopisuje ostatnią, klika „Zapisz” → logowanie → pusta „Nowa faktura”.
- **Propozycja poprawki:** `NavigationLock` z potwierdzeniem przy niezapisanych zmianach; przy 401 w trakcie zapisu nie nawigować, tylko pokazać logowanie w oknie/poprosić o ponowne zalogowanie w nowej karcie; ewentualnie kopia robocza w `sessionStorage`.
- **Test, który to wykryje:** E2E: wypełnij formularz, usuń ciasteczko, „Zapisz” → po ponownym zalogowaniu dane formularza są dostępne.

### UI-11. Service worker: pliki z `?v=` nigdy nie pochodzą z pamięci SW; brak informacji o nowej wersji
- **Waga:** średnie
- **Rodzaj:** błąd / wydajność
- **Pewność:** do weryfikacji (analiza kodu; potrzebny build publikacyjny `dotnet publish` i dwa kolejne wdrożenia - nie wykonywano, by nie budować rozwiązania równolegle z innymi)
- **Miejsce:** `FakturowanieKSeF.Client/wwwroot/service-worker.published.js:36-47`, `FakturowanieKSeF.Client/wwwroot/index.html:11-13`, `FakturowanieKSeF.Client/wwwroot/js/pwa.js`
- **Problem:** `cache.match(request)` porównuje pełny adres, a `index.html` ładuje `js/theme.js?v=51`, `css/app.css?v=51` i `bootstrap.min.css?v=5.3.3`; w pamięci SW są one zapisane bez parametru, więc te trzy pliki zawsze idą do sieci. Po wdrożeniu nowej wersji stary SW nadal podaje stary `index.html` i stare biblioteki, ale arkusz stylów przychodzi już nowy - mieszanie wersji, przed którym miał chronić D50. Nie ma też żadnego mechanizmu „dostępna nowa wersja - odśwież”: zainstalowana PWA używa starego klienta z nowym API do zamknięcia wszystkich okien (nowy enum/pole w DTO → błędy deserializacji).
- **Scenariusz:** wdrożenie ze zmianą `app.css` i układu strony → użytkownicy z otwartą aplikacją widzą stary układ z nowymi stylami do czasu drugiego uruchomienia.
- **Propozycja poprawki:** `cache.match(request, { ignoreSearch: true })` dla zasobów z manifestu albo usunięcie `?v=` (wersjonuje manifest SW); obsługa `registration.onupdatefound` z komunikatem i `skipWaiting` po zgodzie użytkownika.
- **Test, który to wykryje:** rozszerzyć `Pwa_AplikacjaDoInstalacji_…` (build publikacyjny): po przeładowaniu z aktywnym SW żądanie `css/app.css?v=…` jest obsłużone z SW (`response.FromServiceWorker`).

### UI-12. Przypomnienie o płatności: kwota „pozostało” zawsze z dopiskiem PLN
- **Waga:** niskie
- **Rodzaj:** błąd
- **Pewność:** potwierdzone (analiza kodu: `Format.Money(value, currency = "PLN")`, komponent nie ma parametru waluty)
- **Miejsce:** `FakturowanieKSeF.Client/Components/InvoiceEmailPanel.razor:24`
- **Problem/Scenariusz:** faktura w EUR z saldem 100 EUR → nagłówek formularza „Przypomnienie o płatności - pozostało 100,00 PLN”.
- **Propozycja poprawki:** parametr `Currency` przekazywany z `InvoiceDetails`.
- **Test, który to wykryje:** bUnit: panel z `Remaining=100` i walutą EUR → tekst zawiera „100,00 EUR”.

### UI-13. Zapytania sekwencyjne i zbędne przy wczytywaniu stron
- **Waga:** niskie
- **Rodzaj:** wydajność
- **Pewność:** potwierdzone (analiza kodu)
- **Miejsce:** `FakturowanieKSeF.Client/Pages/InvoiceEdit.razor:429-504` (firma → kontrahenci → katalog → wzór/korekta → numer → kurs: 5-7 żądań jedno po drugim przy każdym ustawieniu parametrów); `FakturowanieKSeF.Client/Pages/ReceivedInvoices.razor:159-163` (`sync-status` przed listą przy każdej zmianie filtra i każdej frazie); `FakturowanieKSeF.Client/Pages/Home.razor` (`OnInitializedAsync`: firma osobno, potem 4 żądania listy z `pageSize=1` wyłącznie po liczniki statusów, potem 3 kolejne - razem 8); `FakturowanieKSeF.Client/Pages/InvoiceDetails.razor:331` + `InvoiceEmailPanel` (historia e-maili osobnym żądaniem)
- **Problem:** czas otwarcia formularza faktury to suma opóźnień kilku niezależnych żądań; pełne listy kontrahentów i katalogu są pobierane przy każdym otwarciu formularza (por. PERF-9).
- **Propozycja poprawki:** `Task.WhenAll` dla niezależnych żądań; liczniki statusów w `GET /api/dashboard`; `sync-status` tylko w `OnInitializedAsync` i po synchronizacji.
- **Test, który to wykryje:** E2E liczący żądania `api/**` przy wejściu na pulpit (próg np. ≤ 4).

### UI-14. Formularz faktury: kosztowny render przy każdym naciśnięciu klawisza
- **Waga:** niskie
- **Rodzaj:** wydajność
- **Pewność:** do weryfikacji (analiza kodu; brak pomiaru na dużym katalogu)
- **Miejsce:** `FakturowanieKSeF.Client/Pages/InvoiceEdit.razor:99-105` (`<select>` wszystkich kontrahentów), `:272` (`FindProduct` w pętli pozycji), `:305-310` (`<datalist>` całego katalogu), `:676-677`
- **Problem:** `InputTextLive` powoduje render całego formularza po każdym znaku w dowolnym polu. W każdym renderze dla każdej pozycji wykonywane jest liniowe przeszukanie katalogu z porównaniem kulturowym (ICU w WebAssembly), a listy do 500 kontrahentów i 1000 pozycji katalogu są budowane i porównywane od nowa.
- **Propozycja poprawki:** słownik nazw katalogu (`OrdinalIgnoreCase`) budowany raz; wydzielić wiersz pozycji i listy wyboru do osobnych komponentów z `ShouldRender`; wyszukiwarka kontrahenta zamiast pełnej listy.
- **Test, który to wykryje:** pomiar czasu wpisania 20 znaków przy 1000 pozycjach katalogu i 20 pozycjach faktury.

### UI-15. Stan paneli płatności i e-maili zostaje po przejściu do innej faktury w tym samym widoku
- **Waga:** niskie
- **Rodzaj:** błąd
- **Pewność:** do weryfikacji (analiza kodu; zależy od tego, czy obie faktury mają panel - np. faktura VAT → inna faktura VAT z listy korekt/harmonogramu)
- **Miejsce:** `FakturowanieKSeF.Client/Components/PaymentPanel.razor:109-114`, `FakturowanieKSeF.Client/Components/InvoiceEmailPanel.razor:88-97, 111-119`
- **Problem:** Blazor zachowuje instancję komponentu przy zmianie parametru trasy. `PaymentPanel` resetuje model tylko gdy `PaidOn` jest puste, więc podpowiedziana kwota, uwagi i komunikat błędu pochodzą z poprzedniej faktury; `InvoiceEmailPanel` zostawia otwarty formularz z adresatem poprzedniej faktury. Odwrotnie: wyczyszczenie pola daty w panelu płatności kasuje wpisaną kwotę i uwagi przy najbliższym odświeżeniu (co 3 s podczas oczekiwania na UPO).
- **Propozycja poprawki:** `@key="invoice.Id"` na obu panelach w `InvoiceDetails`/`ReceivedInvoiceDetails`; reset modelu tylko przy zmianie `InvoiceUrl`.
- **Test, który to wykryje:** bUnit: zmiana `InvoiceUrl` i `Info` → `model.Amount == Info.Remaining`.

### UI-16. Usuwanie bez potwierdzenia i akcje bez blokady podwójnego kliknięcia
- **Waga:** niskie
- **Rodzaj:** błąd
- **Pewność:** potwierdzone (analiza kodu)
- **Miejsce:** bez `confirm`: `FakturowanieKSeF.Client/Components/CompanyMembers.razor:129-139` (odebranie dostępu, odwołanie zaproszenia), `FakturowanieKSeF.Client/Pages/Products.razor:148-156`, `FakturowanieKSeF.Client/Pages/RecurringInvoices.razor` (`DeleteAsync`); bez flagi `busy`: `CompanyMembers.InviteAsync` (`:109`), `ChangeRoleAsync`, `FakturowanieKSeF.Client/Pages/InvoiceEdit.razor:690-698` (`AddToCatalogAsync`), `FakturowanieKSeF.Client/Components/CompanySwitcher.razor:24-30` (ignoruje błąd `POST` i przeładowuje stronę)
- **Problem/Scenariusz:** jedno przypadkowe kliknięcie ikony kosza odbiera księgowej dostęp albo usuwa harmonogram (faktura, kontrahent i token KSeF mają potwierdzenie); podwójne kliknięcie „Zaproś” tworzy dwa zaproszenia, a „zapisz w katalogu” - dwie pozycje lub błąd 409.
- **Propozycja poprawki:** `confirm` jak w pozostałych miejscach; wspólna flaga `busy`.
- **Test, który to wykryje:** E2E: klik „Odbierz dostęp” → okno potwierdzenia; anulowanie nie usuwa członka.

### UI-17. Drobne przypadki brzegowe
- **Waga:** niskie
- **Rodzaj:** błąd
- **Pewność:** potwierdzone (analiza kodu)
- **Miejsce i problem:**
  - `FakturowanieKSeF.Client/Pages/RecurringInvoiceEdit.razor:86-112` - `/cykliczne/nowa` bez `?wzor=` (np. z zakładki) pokazuje sam nagłówek, bez komunikatu i bez linku do listy faktur.
  - `FakturowanieKSeF.Client/Components/MonthlyChart.razor` (`Column`: `if (value <= 0) return ""`) - miesiąc z ujemną sprzedażą netto (przewaga korekt) nie ma kolumny i wygląda jak miesiąc bez sprzedaży; wartość jest tylko w tabeli.
  - `FakturowanieKSeF.Client/Pages/Invitation.razor:26` i `FakturowanieKSeF.Client/Components/CompanyMembers.razor:50` - `DateOnly.FromDateTime(ExpiresAt)` bez `ToLocalTime()`: dzień ważności liczony w UTC (o północy różni się o 1 dzień od czasu polskiego).
  - `FakturowanieKSeF.Client/Pages/ReceivedInvoices.razor:37` - „Pobieranie automatyczne co 15 min” wpisane na stałe, także gdy `Ksef:Receive:Enabled=false` lub ustawiono inny odstęp.
  - `FakturowanieKSeF.Client/Layout/MainLayout.razor:102, 108-112` - globalna wyszukiwarka nie pokazuje bieżącej frazy z adresu i nie czyści się po przejściu na inną stronę.
- **Propozycja poprawki:** komunikaty dla pustych stanów; kolumna ujemna poniżej osi; `ToLocalTime()`; odstęp z `sync-status`.
- **Test, który to wykryje:** E2E: `/cykliczne/nowa` bez parametru → widoczna wskazówka.

### UI-18. Martwe reguły w `app.css`
- **Waga:** niskie
- **Rodzaj:** uproszczenie
- **Pewność:** potwierdzone (skrypt: klasy z `app.css` nieobecne w żadnym `.razor`, `.cs` ani `index.html`)
- **Miejsce:** `FakturowanieKSeF.Client/wwwroot/css/app.css`: `.card`, `.card-header`, `.card-title` (268-279, 315-318), `.list-group` (281-288), `.badge` (320), `.nav-pills`, `.nav-tabs` (346-347), `.pagination` (350), `.app-nav-count` (415), `.cell-mono` (656), `.icon-info` (691), `.summary-table tr.fw-bold` (738), `.form-actions` (748), `.form-switch` (259), `.form-control-sm` (252), `.tabular` (161), `.btn-light`, `.btn-secondary` (208)
- **Problem:** pozostałości po przebudowie interfejsu (D50) - nadpisania komponentów Bootstrapa, których aplikacja już nie używa (karty zastąpione przez `surface`, plakietki przez `pill`, stronicowanie przez własny pasek).
- **Propozycja poprawki:** usunąć (ok. 40 wierszy) i podbić `?v=`. Uwaga: `.valid`, `.invalid`, `.modified`, `.validation-message` nadaje Blazor - nie są martwe.
- **Test, który to wykryje:** skrypt w CI porównujący selektory klas z użyciem w `.razor`.

### UI-19. Powtórzony kod stron - kandydaci na wspólne komponenty
- **Waga:** niskie
- **Rodzaj:** uproszczenie
- **Pewność:** potwierdzone (analiza kodu)
- **Miejsce:**
  - pasek filtrów, opóźnione wyszukiwanie, `UpdateUrl` i stopka stronicowania: `Invoices.razor:45-66, 144-150, 200-253` ≈ `ReceivedInvoices.razor:59-79, 135-141, 195-216` (wspólny komponent `PagedTable`/`Pager` usunąłby też UI-6 i UI-8 w jednym miejscu),
  - odmiana „faktura/faktury/faktur”: `Home.razor` (`Faktur`, `Szkic`, `Spraw`) i `Export.razor` (`Faktur`) → `Format.Plural`,
  - `NavMenu.FormatNip` (`:91-92`) dubluje `Format.Nip`; `Home.Pl` i `MonthlyChart.Pl` dublują `Format.Pl`,
  - kopiowanie adresu i pozycji `InvoiceDto` → `SaveInvoiceRequest`: trzy razy w `InvoiceEdit.razor` (`:478-488`, `:541-544`, `:576-586`) oraz w `Company.razor`/`CustomerEdit.razor`,
  - nazwa rodzaju dokumentu (`Kind switch …`) dwa razy w `InvoiceDetails.razor` (`:24`, `:159`),
  - stany „Wczytywanie… / błąd / pusto” i wzorzec `busy/problem/saved` w każdej stronie - komponent `Loader` z `ApiResult<T>` rozwiązałby też UI-9,
  - `KsefSettings.razor` i `NotificationSettings.razor` leżą w `Pages/`, choć nie mają `@page` (są komponentami strony „Moja firma”).
- **Propozycja poprawki:** wydzielić `Pager`, `FilterBar`, `Loader`, metody mapujące w `Contracts` lub w kliencie.
- **Test, który to wykryje:** nie dotyczy (refaktoryzacja pod istniejącymi testami E2E).

### UI-20. Luki w pokryciu testami E2E
- **Waga:** niskie
- **Rodzaj:** błąd (brak testu)
- **Pewność:** potwierdzone (przegląd `UserScenariosE2ETests.cs`, 26 scenariuszy)
- **Problem:** brak scenariuszy dla: wygaśnięcia sesji w trakcie pracy (UI-1, UI-10), błędu sieci i odpowiedzi 5xx (UI-3, UI-5, UI-9), przełącznika firm (`company-switcher` nie występuje w testach), stronicowania i filtrów w adresie wraz z „wstecz” (UI-6, UI-8), widoku faktury odrzuconej przez KSeF, usuwania (kontrahent, pozycja katalogu, harmonogram, członek firmy), rejestracji z linku zaproszenia (UI-7), logowania kodem odzyskiwania, edycji nazwy pozycji zgodnej z katalogiem (UI-2), ceny z więcej niż 2 miejscami (UI-4).
- **Propozycja poprawki:** scenariusze wskazane przy poszczególnych znaleziskach; błędy sieci symulować przez `page.RouteAsync`.

## Sprawdzone bez uwag

- **Bezpieczeństwo po stronie klienta - brak nowych znalezisk.** `MarkupString` tylko w `Icon.razor` (stałe ścieżki SVG, słownik w kodzie); dane użytkownika wszędzie przez kodowanie Razor. W `localStorage` wyłącznie motyw (`ksef.theme`). Brak tokenów w JS (D18). `returnUrl` - zob. SEC-2; token zaproszenia w adresie - zob. SEC-9. Kod QR 2FA jako `data:` z serwera.
- `DeepValidator`: ścieżki `Items[1].Quantity`, `Buyer.Address.PostalCode` trafiają do właściwych pól; błąd serwera bez pola trafia do podsumowania; w przeglądarce potwierdzono komunikat „Dla płatności przelewem podaj termin płatności.” przy polu i w podsumowaniu.
- `ApiClient`: 400/403/404/409/429/500 dają `ProblemAlert`; odpowiedź nie-JSON (502 `text/html`) → „Błąd serwera (502).”, przycisk odblokowany (potwierdzone w przeglądarce).
- Kultura pl-PL: `InputNumber` (kultura niezmienna + `type=number`), `Format.*`, `[Range]` z `ParseLimitsInInvariantCulture`, współrzędne SVG w kulturze niezmiennej.
- Przełączenie motywu, otwarcie menu i wpisywanie w globalną wyszukiwarkę nie resetują formularza faktury (potwierdzone w przeglądarce).
- Kontrast (pomiar `getComputedStyle` na pulpicie, liście i formularzu faktury): tryb jasny min. 5,35:1, tryb ciemny min. 6,62:1 dla `.muted`, `.text-muted`, `.cell-sub`, `.form-text`, `.kpi-note`, wszystkich `pill`, nagłówków tabel i etykiet menu.
- Etykiety kontrolek: pola formularzy mają `<label for>` lub `aria-label`; przyciski-ikony mają `aria-label`; `FocusOnNavigate` na `h1`; link „Przejdź do treści”.
- `service-worker.published.js`: `/api/` i `/swagger` zawsze z sieci (także pobieranie PDF/XML/UPO), nic z danych nie trafia do pamięci podręcznej; wersja deweloperska niczego nie przechowuje.
- `js/theme.js`, `js/pwa.js`, `manifest.webmanifest`, `index.html` (zgodne z CSP, brak zasobów zewnętrznych).
- `Account.razor` (2FA), `KsefSettings`, `NotificationSettings`, `Export.razor`, `Sparkline`, `BarList`, `AddressFields`, `VatRegistryLookup`, `ThemeToggle`, `InputTextLive` - bez uwag poza wymienionymi.
- Rozmiar aplikacji: brak zbędnych pakietów w `Client.csproj` (3 pakiety Microsoft + `Contracts`); pełne dane ICU (`icudt.dat` 1,5 MB, 0,46 MB po gzip) - koszt świadomy (D20), ewentualna oszczędność przez `applicationCulture: "pl-PL"` jest marginalna.

## Poza obszarem

- Brak nowych krytycznych. Przy okazji potwierdzono z perspektywy klienta: przycisk „Pobierz z KSeF” na liście faktur otrzymanych jest widoczny dla księgowej (`ReceivedInvoices.razor:13`, brak `Ctx.CanIssue`) - to interfejs do ISO-4.
- Uwaga środowiskowa: klaster PostgreSQL w kontenerze był wyłączony - uruchomiono go (`service postgresql start`) i pozostawiono włączony. Kopii roboczej w `bin/` nie dało się użyć do testów w przeglądarce (pliki `_framework` zmieniały się w trakcie - równoległe budowanie), dlatego weryfikację wykonano na kopii zbudowanej poza repozytorium (usuniętej).

## Pytania do autora

1. UI-2: czy podpowiedź z katalogu ma działać także przy ręcznym wpisaniu pełnej nazwy (obecnie tak), czy tylko po wybraniu z listy? Od tego zależy sposób poprawki.
2. UI-4: jaka precyzja ceny jednostkowej jest zamierzona w interfejsie - 2 miejsca (wtedy serwer powinien odrzucać więcej) czy do 8 jak w FA(3)?
3. UI-3: czy odpytywanie faktury `Accepted` bez UPO ma trwać bez limitu? Ile realnie czeka się na UPO w testowym KSeF?
4. UI-11: czy mieszanie wersji po wdrożeniu było obserwowane po wprowadzeniu `?v=` (D50)? Czy wymagane jest powiadomienie o nowej wersji w zainstalowanej PWA?
5. UI-7: czy osoba zaproszona ma w ogóle widzieć formularz zakładania własnej firmy po rejestracji (por. ISO-3)?
