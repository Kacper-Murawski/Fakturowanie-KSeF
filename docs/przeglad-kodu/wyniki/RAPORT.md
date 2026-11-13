# Raport zbiorczy przeglądu kodu
Status: ZAKOŃCZONY
Ostatnia aktualizacja: 2026-11-13 (krok 99 zakończony)

## Postęp
Legenda werdyktu: P = potwierdzone, O = odrzucone, N = niepewne, `-` = nieweryfikowane (średnie/niskie poza próbą).
Krytyczne i wysokie - wszystkie do weryfikacji; średnie i niskie - co najmniej co trzecie.

- [x] wczytanie raportów 01-07 (101 znalezisk)
- [x] SEC-1 [P: kod: AccountEndpoints /disable,/recovery-codes bez AccessFailedAsync i bez polityki limitu; test w raporcie 01] (wysokie) Brak limitu prób kodu TOTP przy wyłączaniu 2FA i generowaniu kodów odzyskiwania
- [x] SEC-2 [P: próba; kod: Login.razor:95 odrzuca tylko „://” i „//”] (średnie) Przekierowanie po logowaniu na obcą domenę (`returnUrl`)
- [x] SEC-3 [P: próba; kod: Program.cs:236 polityka Auth na całej grupie /auth (z manage/info), AuthService woła manage/info przy starcie] (średnie) Limit „auth” (10/min na IP) obejmuje sprawdzenie sesji przy każdym starcie aplikacji - blokada użytkowników za wspólnym adresem
- [x] SEC-4 [-: poza próbą, status z raportu obszaru] (średnie) Aplikacja jako przekaźnik wiadomości: limit tylko „na firmę”, liczony bez blokady, konta bez potwierdzenia adresu
- [x] SEC-5 [-: poza próbą, status z raportu obszaru] (średnie) Wspólny dzienny limit wykazu VAT (90 wyszukiwań) może wyczerpać kilka świeżo założonych kont
- [x] SEC-6 [P: próba; test Tmp99: date=0001-01-05 → 500] (niskie) Podgląd kursu NBP: dowolna data → nieograniczone zapytania wychodzące, rosnąca pamięć podręczna, błąd 500
- [x] SEC-7 [-: poza próbą, status z raportu obszaru] (niskie) Weryfikacja i generowanie tokenu KSeF bez ograniczenia częstotliwości
- [x] SEC-8 [-: poza próbą, status z raportu obszaru] (niskie) Surowe komunikaty wyjątków infrastruktury w odpowiedziach API
- [x] SEC-9 [P: próba; deploy/Caddyfile loguje żądania (log → stdout), token w query] (niskie) Token zaproszenia w adresie URL trafia do logów proxy
- [x] SEC-10 [P: próba; brak rejestracji IEmailSender<ApplicationUser> w Api] (niskie) Endpointy Identity wystawione bez działającej poczty: brak resetu hasła, a po podpięciu nadawcy - zmiana e-maila bez hasła
- [x] SEC-11 [P: próba; Program.cs:206 UseHsts bez AddHsts] (niskie) HSTS z ustawieniami domyślnymi (30 dni, bez subdomen)
- [x] ISO-1 [P: kod: indeks globalny AppDbContext:140, SaveChanges poza try (Processor:60); test w raporcie 02; = KSEF-10] (krytyczne) Globalnie unikalny numer KSeF: jedna firma blokuje wysyłkę do KSeF wszystkich firm i import faktur drugiej firmy
- [x] ISO-2 [P: kod: RecurringInvoices:49-83 bez try/catch; test w raporcie 02; = KSEF-14, wspólny wzorzec z KSEF-6] (wysokie) Usługi w tle bez izolacji błędów per firma: harmonogram jednej firmy zatrzymuje faktury cykliczne pozostałych
- [x] ISO-3 [P: kod: CompanyEndpoints:15 + CurrentCompany:52-54,116] (wysokie) Konto będące członkiem cudzej firmy nie może założyć własnej firmy (403 na `PUT /api/company`)
- [x] ISO-4 [P: próba; kod: grupa /received-invoices bez RequireRoleForChanges (ReceivedInvoiceEndpoints:23)] (średnie) Księgowa może uruchomić synchronizację z KSeF i zapisać wynik sprawdzenia rachunku (grupa `/received-invoices` bez filtra ról)
- [x] ISO-5 [P: próba; kod serwera: CurrentCompany:38-55 cichy powrót do firmy domyślnej; zachowanie klienta nieweryfikowane] (średnie) Po odebraniu dostępu wybór firmy „po cichu” wraca do firmy własnej - kolejne zapisy trafiają do innej firmy
- [x] ISO-6 [P: próba; kod: InvoiceEndpoints:577-581, :595-596, PaymentReminders:67-68 bez CompanyId - niewykorzystywalne] (niskie) Zapytania pomocnicze korekt bez warunku `CompanyId` (obrona w głąb)
- [x] ISO-7 [-: poza próbą, status z raportu obszaru] (niskie) Luki w testach ról i izolacji (endpointy bez przypadku testowego)
- [x] KSEF-1 [P: test Tmp99 (usunięty): utracona odpowiedź → drugi SendInvoice tego samego XML; 10 utrat → Rejected przy 10 przyjęciach w atrapie] (krytyczne) Faktura przyjęta przez KSeF bez zapisanego numeru referencyjnego jest wysyłana ponownie lub oznaczana `Rejected`
- [x] KSEF-2 [O: świadome ograniczenie: jedna instancja (docs/podsumowanie.md „Ograniczenia”); w jednej instancji pętla jest pojedyncza - zostaje zalecenie dopisania do decyzji] (wysokie) Brak ochrony przed równoległym przetwarzaniem tej samej wysyłki (dwie instancje usługi)
- [x] KSEF-3 [P: test Tmp99: DELETE tokenu przy 50 wysyłkach w toku → 204, faktury Pending bez końca] (wysokie) `Pending` bez wyjścia po przyjęciu faktury do przetwarzania
- [x] KSEF-4 [P: test Tmp99: 50 zablokowanych wysyłek firmy A → faktura firmy B nigdy nie wysłana (ref=null po 6 przebiegach)] (wysokie) Kolejka wysyłek ograniczona do 50 najstarszych - zablokowane lub czekające na UPO głodzą nowe
- [x] KSEF-5 [P: kod: MaxSendAttempts=10, interwał 3 s (Worker:37), brak NextAttemptAt; istniejący test AwariaSieciPrzedWyslaniem utrwala Rejected po 10 próbach] (wysokie) Brak odstępów między ponowieniami: `Rejected` po ok. 30 s niedostępności, odpytywanie KSeF co 3 s bez końca
- [x] KSEF-6 [P: test Tmp99: duplikat w trakcie synchronizacji → DbUpdateException z SyncAsync, LastSyncAt niezapisany; pętla workera bez try per firma] (wysokie) Błąd zapisu jednej faktury przerywa synchronizację wszystkich firm i wymusza ponawianie co minutę
- [x] KSEF-7 [-: poza próbą, status z raportu obszaru] (średnie) Trwale wadliwa faktura zatrzymuje punkt kontrolny na zawsze
- [x] KSEF-8 [P: próba; kod: LastSyncAt porównywany przed synchronizacją, zapis po zapytaniach (ReceivedInvoiceEndpoints:193, InvoiceEndpoints:289)] (średnie) Limit „raz na minutę” dla ręcznej synchronizacji i importu nie jest atomowy
- [x] KSEF-9 [-: poza próbą, status z raportu obszaru] (średnie) Budżet zapytań jest liczony na synchronizację, a limity KSeF są godzinowe
- [x] KSEF-10 [P: duplikat ISO-1] (średnie) Globalnie unikalny numer KSeF w `Invoices` przy braku unikalności NIP firm
- [x] KSEF-11 [P: próba; kod: KsefGateway catch (KsefApiException) → KsefAuthenticationException bez rozróżnienia 429/5xx] (średnie) Błędy przejściowe przy uwierzytelnianiu są zgłaszane jako odrzucenie tokenu
- [x] KSEF-12 [-: poza próbą, status z raportu obszaru] (średnie) Odrzucony token blokuje pętlę wysyłek wszystkich firm na ok. 60 s w każdym przebiegu
- [x] KSEF-13 [P: próba; test Tmp99: awaria NBP → IsActive=false, LastError „sprawdź fakturę-wzór”] (średnie) Przejściowa niedostępność NBP trwale wyłącza harmonogram faktury cyklicznej w walucie obcej
- [x] KSEF-14 [P: duplikat ISO-2] (średnie) Wyjątek dla jednego rekordu przerywa przebieg faktur cyklicznych i przypomnień dla wszystkich firm
- [x] KSEF-15 [P: próba; test Tmp99: 200 bez numeru → Rejected (czy KSeF tak odpowiada - nieznane, waga niska utrzymana)] (niskie) Kod 200 bez numeru KSeF jest traktowany jak odrzucenie
- [x] KSEF-16 [P: próba; kod: PaymentReminders:58-71] (niskie) Automatyczne przypomnienia co godzinę wczytują całą historię faktur po terminie i wykonują zapytania N+1
- [x] KSEF-17 [N: próba; brak AllowAutoRedirect=false potwierdzony, ale wykonania przekierowania nie testowano (wymaga atrapy handlera podstawowego)] (niskie) `KsefTestOnlyHandler` nie widzi przekierowań HTTP
- [x] KSEF-18 [-: poza próbą, status z raportu obszaru] (niskie) Bufor tokenów dostępu nie jest unieważniany po odpowiedzi 401
- [x] KSEF-19 [-: poza próbą, status z raportu obszaru] (niskie) Powtórzony kod synchronizacji odbioru i importu oraz drobne nieefektywności
- [x] FAK-1 [P: kod: InvoiceEndpoints:557-558, Import:144-151; test w raporcie 04] (krytyczne) Korekta faktury zaimportowanej z KSeF wysyła błędne kwoty różnicy (stan „przed” liczony ponownie z ilość × cena)
- [x] FAK-2 [P: kod: OrderByDescending(Id) InvoiceEndpoints:580-583; test w raporcie 04] (wysokie) „Ostatnia przyjęta korekta” wybierana po `Id`, a nie po chwili przyjęcia - zły stan „przed” kolejnej korekty
- [x] FAK-3 [P: kod + test Tmp99: wystawienie 13.11, sprzedaż 19.11 → kurs z 18.11 (po dacie wystawienia); zachowanie prawdziwego NBP dla przedziału w przyszłości nieweryfikowane (bez sieci)] (wysokie) Kurs NBP dla faktury walutowej z datą sprzedaży późniejszą niż data wystawienia: zła data kursu, kurs zależny od chwili zapisu, błąd przy dacie odległej
- [x] FAK-4 [-: poza próbą, status z raportu obszaru] (średnie) Ilość i cena z większą liczbą miejsc niż w bazie: `TotalGross` w bazie ≠ `P_15` w XML (różnica 0,01)
- [x] FAK-5 [P: próba; VatInPln = round(różnica × kurs)] (średnie) Podatek w PLN (`P_14_xW`) korekty walutowej liczony z różnicy, nie jako różnica stanów - 0,01 zł po serii korekt
- [x] FAK-6 [P: próba; kod: CheckCorrectionStateAsync przed zapisem, Version tylko własnej korekty - wyścigu nie odtwarzano] (średnie) Dwie różne korekty tej samej faktury wysłane równolegle przechodzą kontrolę stanu
- [x] FAK-7 [-: poza próbą, status z raportu obszaru] (średnie) Stawki 0% WDT, 0% eksport, „np I/II” dostępne, ale nabywca zagraniczny trafia do XML jako `BrakID` (konsument)
- [x] FAK-8 [P: próba; kod: IssuedInvoiceImport:169 tylko PaidOn] (średnie) Import faktury oznaczonej „Zapłacono” nie tworzy płatności - opłacona faktura widnieje jako należność
- [x] FAK-9 [P: próba; kod: InvoiceEndpoints:304 DateTime.Today] (niskie) Daty „dzisiaj” według zegara serwera zamiast czasu polskiego
- [x] FAK-10 [P: próba; N2 dla ceny] (niskie) PDF: cena jednostkowa zawsze z dwoma miejscami, choć `P_9A` dopuszcza osiem
- [x] FAK-11 [-: poza próbą, status z raportu obszaru] (niskie) Czytnik FA pomija elementy używane przez innych wystawców (PDF i eksport faktur otrzymanych/importowanych)
- [x] FAK-12 [P: próba; kod: NbpExchangeRates:50-52 bez catch JsonException; = TST-3 (część NBP)] (niskie) Odpowiedź NBP niebędąca poprawnym JSON-em kończy się błędem 500 zamiast komunikatu walidacji
- [x] FAK-13 [-: poza próbą, status z raportu obszaru] (niskie) Eksport CSV: kolumny per stawka nie sumują się do „Razem”, a podsumowanie pomija VAT w PLN faktur walutowych
- [x] FAK-14 [P: próba; test Tmp99: ilość 999999999999 × cena 99999999999999 → 500] (niskie) Brak górnych granic kwot i dat: przepełnienie → 500, daty spoza zakresu FA(3) wykrywane dopiero przy wysyłce
- [x] FAK-15 [-: poza próbą, status z raportu obszaru] (niskie) Sumy faktury liczone ponownie w czterech miejscach - brak jednego źródła prawdy
- [x] PERF-1 [P: kod: AccountingExport.SalesAsync Include(KsefSubmissions)+Read(XML) także dla /summary; pomiar w raporcie 05; waga zależna od skali danych] (wysokie) Podsumowanie eksportu i pliki CSV wczytują i parsują wszystkie XML-e okresu (także UPO), choć potrzebują kilku liczb
- [x] PERF-6 [P: kod: PaymentEndpoints:29-41,148-157; test w raporcie 05] (wysokie) Wyścig przy rejestracji wpłat: dwie równoległe wpłaty mijają kontrolę „pozostało do zapłaty” (nadpłata ponad kwotę należną)
- [x] PERF-2 [-: poza próbą, status z raportu obszaru] (średnie) Paczka ZIP i CSV budowane w całości w pamięci (kilka kopii każdego dokumentu)
- [x] PERF-3 [-: poza próbą, status z raportu obszaru] (średnie) Dwa `Include` kolekcji w jednym zapytaniu (pozycje × wysyłki z XML) - iloczyn kartezjański z dużymi kolumnami
- [x] PERF-4 [-: poza próbą, status z raportu obszaru] (średnie) Szczegóły faktury pobierają treść XML i UPO wszystkich wysyłek tylko po to, by pokazać status ostatniej
- [x] PERF-5 [-: poza próbą, status z raportu obszaru] (średnie) Pulpit i podsumowanie płatności wczytują do pamięci wszystkie nierozliczone dokumenty i sumują je w aplikacji; to samo zapytanie wykonywane dwa razy
- [x] PERF-7 [P: próba; kod: InvoiceEndpoints:256 odczyt przed zapisem, indeks ProformaId nieunikalny] (średnie) Zamiana proformy na fakturę VAT bez ochrony przed równoległym wywołaniem - dwie faktury z jednej proformy
- [x] PERF-8 [P: próba; kod: RecurringInvoices:109 i :76 - dwa SaveChanges] (średnie) Faktury cykliczne: szkic i przesunięcie harmonogramu zapisywane w dwóch osobnych `SaveChanges` - przerwanie między nimi tworzy duplikat szkicu
- [x] PERF-9 [P: próba; Take(500)/Take(1000) bez total] (średnie) Listy kontrahentów i katalogu obcinane po cichu do 500/1000 rekordów
- [x] PERF-10 [N: próba; zdublowany indeks (AppDbContext:138 i :140) potwierdzony; wpływ na plany zapytań bez EXPLAIN na dużym zbiorze - niepewny] (średnie) Indeksy niedopasowane do zapytań: lista i eksport faktur otrzymanych, kolejka wysyłek KSeF, indeks zdublowany
- [x] PERF-11 [P: próba; test Tmp99: q=FV_1 znajduje FV/1/11/2026, q=% zwraca wszystko] (niskie) Wyszukiwanie `q`: `ILIKE '%…%'` po czterech kolumnach bez limitu długości i bez neutralizacji znaków `%`/`_`
- [x] PERF-12 [-: poza próbą, status z raportu obszaru] (niskie) Stan faktury korygowanej: wczytywane wszystkie korekty z pozycjami i dwukrotnie faktura pierwotna
- [x] PERF-13 [P: kod: attempt<3 w trzech miejscach; test w raporcie 05] (niskie) Numeracja automatyczna: trzy próby nie wystarczają przy kilku równoległych zapisach - 409 „Faktura o tym numerze już istnieje” mimo pustego pola numeru
- [x] PERF-14 [-: poza próbą, status z raportu obszaru] (niskie) Kolejka e-mail: limit dobowy sprawdzany przed zapisem, brak „zajęcia” wiadomości przez instancję, historia bez retencji
- [x] PERF-15 [-: poza próbą, status z raportu obszaru] (niskie) Drobne koszty stałe: `DbContext` bez puli, firma śledzona i czytana 1-3 zapytaniami w każdym żądaniu, pełna encja faktury tam, gdzie wystarczy kilka pól
- [x] PERF-16 [-: poza próbą, status z raportu obszaru] (niskie) Uproszczenia: powtórzony kod endpointów, wyrażenie „kwota należna”, bliźniacze klasy usług w tle; propozycja podziału `InvoiceEndpoints.cs`
- [x] PERF-17 [P: próba; appsettings.json: Default=Information, brak wyciszenia EF] (niskie) Produkcyjnie każde polecenie SQL trafia do logu (poziom `Information`), także z bezczynnych usług w tle
- [x] UI-1 [P: kod: ApiClient:60-64 (returnUrl z bieżącego adresu, brak unieważnienia AuthService); przeglądarka w raporcie 06] (wysokie) Wygaśnięcie sesji: zagnieżdżony `returnUrl` i formularz logowania wewnątrz aplikacji - po zalogowaniu znów „Logowanie”
- [x] UI-2 [P: kod: InvoiceEdit:269 (bind:after przy oninput) + FindProduct z Trim(); przeglądarka w raporcie 06] (wysokie) Podpowiedź z katalogu przy każdym znaku: zjada spację w nazwie pozycji i nadpisuje ręcznie wpisaną cenę
- [x] UI-3 [-: poza próbą, status z raportu obszaru] (średnie) Odpytywanie statusu faktury: trwa po opuszczeniu strony, a jeden błąd sieci usuwa fakturę z ekranu
- [x] UI-4 [P: próba; step=0.01 w InvoiceEdit:283 i Products:77] (średnie) `step="0.01"` na cenie netto blokuje zapis cen z więcej niż 2 miejscami - bez komunikatu aplikacji
- [x] UI-5 [-: poza próbą, status z raportu obszaru] (średnie) Logowanie, rejestracja i wylogowanie bez obsługi braku sieci - nieobsłużony wyjątek i zablokowany przycisk
- [x] UI-6 [-: poza próbą, status z raportu obszaru] (średnie) Listy z filtrami: wynik starszego zapytania nadpisuje nowszy; opóźnione wyszukiwanie zmienia adres innej strony
- [x] UI-7 [P: próba; Register.razor:67 zawsze NavigateTo(firma), link bez returnUrl] (średnie) Zaproszona osoba bez konta po rejestracji trafia do zakładania własnej firmy - zaproszenie „znika”
- [x] UI-8 [P: próba; Invoices.razor:97-99 tr @onclick + link] (średnie) Wiersz listy jako `<tr role="button">`: klik w numer faktury dodaje dwa wpisy historii („wstecz” nie wraca do listy)
- [x] UI-9 [-: poza próbą, status z raportu obszaru] (średnie) Błąd pobierania danych wygląda jak „brak danych”
- [x] UI-10 [-: poza próbą, status z raportu obszaru] (średnie) Utrata wypełnionego formularza faktury (wygaśnięcie sesji, przypadkowa nawigacja)
- [x] UI-11 [N: wymaga builda publikacyjnego i dwóch wdrożeń - nieweryfikowane] (średnie) Service worker: pliki z `?v=` nigdy nie pochodzą z pamięci SW; brak informacji o nowej wersji
- [x] UI-12 [P: próba; Format.Money(Remaining) bez waluty] (niskie) Przypomnienie o płatności: kwota „pozostało” zawsze z dopiskiem PLN
- [x] UI-13 [-: poza próbą, status z raportu obszaru] (niskie) Zapytania sekwencyjne i zbędne przy wczytywaniu stron
- [x] UI-14 [-: poza próbą, status z raportu obszaru] (niskie) Formularz faktury: kosztowny render przy każdym naciśnięciu klawisza
- [x] UI-15 [-: poza próbą, status z raportu obszaru] (niskie) Stan paneli płatności i e-maili zostaje po przejściu do innej faktury w tym samym widoku
- [x] UI-16 [-: poza próbą, status z raportu obszaru] (niskie) Usuwanie bez potwierdzenia i akcje bez blokady podwójnego kliknięcia
- [x] UI-17 [-: poza próbą, status z raportu obszaru] (niskie) Drobne przypadki brzegowe
- [x] UI-18 [-: poza próbą, status z raportu obszaru] (niskie) Martwe reguły w `app.css`
- [x] UI-19 [-: poza próbą, status z raportu obszaru] (niskie) Powtórzony kod stron - kandydaci na wspólne komponenty
- [x] UI-20 [-: poza próbą, status z raportu obszaru] (niskie) Luki w pokryciu testami E2E
- [x] TST-1 [P: kod: TestUsers:86-87 DueDate 15.11.2026, ReceivedInvoicesTests T0=30.10.2026, brak FakeTimeProvider; dziś 13.11 - pierwszy test padnie za 3 dni] (wysokie) Testy z datami na sztywno zaczną zawodzić 16.11.2026 i 29.11.2026 („bomba zegarowa”)
- [x] TST-2 [-: poza próbą, status z raportu obszaru] (średnie) Brak sterowania czasem w testach: reguły czasowe testowane obejściami, mieszanie dnia UTC z dniem polskim
- [x] TST-3 [P: próba; kod jw. + KsefSettingsEndpoints - niżej] (średnie) Odpowiedź NBP inna niż JSON oraz niedostępny KSeF przy zapisie tokenu kończą się błędem 500
- [x] TST-4 [-: poza próbą, status z raportu obszaru] (średnie) Atrapa KSeF jest „zbyt uprzejma” - ukrywa klasy błędów znalezione w obszarze 03
- [x] TST-5 [-: poza próbą, status z raportu obszaru] (średnie) Testy działają wyłącznie w konfiguracji deweloperskiej: ścieżki produkcyjne i pętle usług w tle bez testów
- [x] TST-6 [-: poza próbą, status z raportu obszaru] (średnie) Brak testów współbieżności i testu konwencji endpointów - błędy z obszarów 02, 03, 05 przechodzą zestaw
- [x] TST-7 [P: próba; AuthTests:48 bez sprawdzenia rejestracji] (niskie) Testy, które przechodzą niezależnie od sprawdzanego zachowania (niesprawdzony krok przygotowania)
- [x] TST-8 [-: poza próbą, status z raportu obszaru] (niskie) Wspólna baza i wspólna atrapa na klasę testów - wynik zależy od kolejności testów w klasie
- [x] TST-9 [-: poza próbą, status z raportu obszaru] (niskie) E2E: jedna kolekcja 26 scenariuszy (3 minuty sekwencyjnie) i część selektorów zależna od struktury i stylu
- [x] TST-10 [-: poza próbą, status z raportu obszaru] (niskie) CI: brak limitu czasu, artefaktów diagnostycznych i pomiaru pokrycia; ostrzeżenia nie blokują
- [x] TST-11 [-: poza próbą, status z raportu obszaru] (niskie) Opcje konfiguracji jako literały rozsiane po kodzie, bez klas opcji i walidacji przy starcie
- [x] TST-12 [P: próba; docs/testy.md: „E2E (9)”, „na żywo (3)”; KsefSpike w .sln] (niskie) Drobne niespójności: dokument testów, prototyp w rozwiązaniu, powtórzone kroki testów

- [x] raport zbiorczy (sekcje 1-7)

Następny krok: brak - krok końcowy zakończony.

## Jak weryfikowano

- Wczytano siedem raportów obszarów (101 znalezisk: 3 krytyczne, 15 wysokich, 41 średnich, 42 niskie).
- **Krytyczne i wysokie (18): wszystkie sprawdzone** - lektura wskazanego kodu i kodu wywołującego, porównanie
  z `docs/decyzje-projektowe.md` i `docs/podsumowanie.md`. Znaleziska potwierdzone testem już w raporcie obszaru
  (SEC-1, ISO-1, ISO-2, ISO-3, FAK-1, FAK-2, PERF-1, PERF-6, UI-1, UI-2, TST-1) sprawdzono tylko w kodzie. Pozostałe
  (KSEF-1, KSEF-3, KSEF-4, KSEF-6, FAK-3) potwierdzono tymczasowymi testami integracyjnymi `Tmp99*` na atrapach
  (uruchomione filtrem, 7 testów, plik usunięty; bez połączeń z KSeF, NBP, wykazem VAT i SMTP).
- **Średnie i niskie (83): sprawdzono 39 (47%)** - 7 testem tymczasowym (KSEF-13, KSEF-15, SEC-6, FAK-14, PERF-11
  i przy okazji KSEF-3/4), pozostałe lekturą kodu. Reszta (44) ma status przejęty z raportu obszaru i oznaczenie „nw.”.
- Wyniki testów tymczasowych: utracona odpowiedź po przyjęciu → ten sam XML wysłany drugi raz, a po 10 utratach faktura
  `Rejected` przy 10 przyjęciach w atrapie (KSEF-1); usunięcie tokenu przy 50 wysyłkach w toku → 204, 50 faktur `Pending`
  bez końca, a faktura innej firmy nie została wysłana w 6 przebiegach (KSEF-3, KSEF-4); duplikat wiersza w trakcie
  synchronizacji → `DbUpdateException` z `SyncAsync`, `LastSyncAt` niezapisany (KSEF-6); faktura w EUR wystawiona 13.11
  ze sprzedażą 19.11 → kurs z 18.11, czyli z dnia po wystawieniu (FAK-3); awaria NBP → harmonogram `IsActive = false`
  (KSEF-13); status 200 bez numeru → `Rejected` (KSEF-15); `date=0001-01-05` → 500 (SEC-6); ilość 10^12 × cena 10^14 → 500
  (FAK-14); `q=FV_1` znajduje `FV/1/11/2026`, `q=%` zwraca wszystko (PERF-11).
- Czego nie sprawdzono: zachowania prawdziwego KSeF (status 440 przy ponownej wysyłce, limity), prawdziwego NBP dla
  przedziału dat w przyszłości, przeglądarki (znaleziska UI przejęte z raportu 06), builda publikacyjnego (UI-11),
  planów zapytań na dużych danych (PERF-10), kontenera (kontrola stanu a `AllowedHosts`).
- Uwaga: w PostgreSQL są dwie bazy `ksef_test_*` (`…667d43b7…`, `…3d6032fc…`). Nie mogłem ustalić, czy pochodzą z tego
  przebiegu, więc ich nie usuwałem.

## 1. Podsumowanie

Kod jest w dobrym stanie jak na projekt tej wielkości: nie znaleziono żadnej ścieżki odczytu ani zmiany cudzych danych
przez API, rdzeń obliczeń (kalkulator, stawki, XML FA(3) z walidacją XSD) jest poprawny, kod własny kompiluje się bez
ostrzeżeń, a 290 testów zwykłych i 26 scenariuszy E2E przechodzi. Słabe miejsca leżą poza „szczęśliwą ścieżką”:
(1) usługi w tle nie izolują błędów między firmami - jeden rekord jednej firmy zatrzymuje wysyłkę do KSeF, synchronizację
i faktury cykliczne wszystkim (ISO-1, ISO-2, KSEF-4, KSEF-6); (2) maszyna stanów wysyłki uznaje brak numeru
referencyjnego za dowód, że faktura nie trafiła do KSeF, co łamie własną regułę D25 (KSEF-1); (3) korekty faktur
importowanych i kolejność korekt dają błędne kwoty w KSeF (FAK-1, FAK-2); (4) operacje „sprawdź, potem zapisz” nie są
chronione przed równoległym wywołaniem (wpłaty, proforma, korekty, limity). Najpilniejsze jest jednak TST-1: od
**16.11.2026** (za 3 dni) zestaw testów zacznie zawodzić bez zmiany kodu.

| Kategoria | Ocena | Uzasadnienie |
|---|---|---|
| Poprawność | **3/5** | Obliczenia i typowe przepływy są poprawne i pokryte testami, ale są 3 błędy krytyczne w ścieżkach brzegowych (utracona odpowiedź KSeF, korekty faktur importowanych, blokada kolejki) i kilka wyścigów. |
| Bezpieczeństwo | **4/5** | Izolacja danych, sesja, CSP, szyfrowanie tokenu i XML bez XXE są solidne; brakuje izolacji *dostępności* między firmami, limitu prób kodu 2FA i szczelnego `returnUrl`. |
| Wydajność | **4/5** | Liczba zapytań na ekran jest stała (brak N+1), listy stronicowane w SQL; eksport i szczegóły faktury niepotrzebnie ładują XML, a pulpit sumuje w pamięci - dotkliwe dopiero przy tysiącach faktur. |
| Utrzymywalność | **4/5** | Spójna architektura, 0 ostrzeżeń, dokumentacja decyzji, 323 testy; minusy to powtórzony kod pętli usług i synchronizacji, 616-wierszowy `InvoiceEndpoints.cs`, opcje jako literały i testy zależne od zegara. |

**Bilans weryfikacji** (P = potwierdzone, O = odrzucone, N = niepewne, nw. = nieweryfikowane ponownie):

| Waga | Zgłoszone | P | O | N | nw. |
|---|---|---|---|---|---|
| Krytyczne | 3 | 3 | 0 | 0 | 0 |
| Wysokie | 15 | 14 | 1 | 0 | 0 |
| Średnie | 41 | 19 | 0 | 2 | 20 |
| Niskie | 42 | 17 | 0 | 1 | 24 |
| **Razem** | **101** | **53** | **1** | **3** | **44** |

Po połączeniu duplikatów (ISO-1 = KSEF-10, ISO-2 = KSEF-14, FAK-12 = część TST-3) zostaje 98 odrębnych problemów.
Żadne znalezisko nie zmieniło wagi; przy SEC-3 i TST-1 zalecam wyższy priorytet wykonania niż wynika z samej wagi.

## 2. Tabela znalezisk

Kolejność: waga → koszt poprawki (S do 0,5 dnia, M do 2 dni, L powyżej) → ryzyko poprawki. Status: **P** potwierdzone
(t = testem w tym kroku, r = testem/pomiarem/przeglądarką w raporcie obszaru + kod, k = lekturą kodu), **O** odrzucone,
**N** niepewne, **nw.** przejęte z raportu obszaru bez ponownej weryfikacji. Kolumna „Gałąź” odsyła do planu w sekcji 3.

| ID | Tytuł | Waga | Rodzaj | Status | Miejsce | Koszt | Gałąź |
|---|---|---|---|---|---|---|---|
| ISO-1 = KSEF-10 | Globalnie unikalny numer KSeF: jedna firma (para kont z tym samym NIP) trwale zatrzymuje wysyłkę wszystkich firm i import drugiej | krytyczne | bezpieczeństwo / błąd | P (r) | `Data/AppDbContext.cs:140`, `Services/IssuedInvoiceImport.cs:119`, `Services/KsefSubmissionProcessor.cs:60` | S | 2 |
| FAK-1 | Korekta faktury zaimportowanej z KSeF: stan „przed” liczony z ilość × cena → błędne kwoty różnicy w KSeF | krytyczne | błąd | P (r) | `Endpoints/InvoiceEndpoints.cs:557`, `Ksef/Fa3/Fa3XmlGenerator.cs:161`, `Services/IssuedInvoiceImport.cs:141` | M (blokada) / L (pełna) | 5 |
| KSEF-1 | Faktura przyjęta przez KSeF bez zapisanego numeru referencyjnego jest wysyłana ponownie albo oznaczana `Rejected` | krytyczne | błąd | P (t) | `Services/KsefSubmissionProcessor.cs:77-86, 143-158`, `Ksef/KsefGateway.cs:148-177` | L | 4 |
| TST-1 | Testy ze stałymi datami zawodzą od 16.11.2026 i 29.11.2026 | wysokie | błąd (testy) | P (r) | `Tests/Infrastructure/TestUsers.cs:86`, `Tests/ReceivedInvoicesTests.cs:67`, `Tests/Infrastructure/ApiFactory.cs` | S | 1 |
| ISO-3 | Członek cudzej firmy nie może założyć własnej (403 na `PUT /api/company`) | wysokie | błąd | P (r) | `Endpoints/CompanyEndpoints.cs:14-32`, `Infrastructure/CurrentCompany.cs:52, 116` | S | 9 |
| SEC-1 | Brak limitu prób kodu TOTP przy wyłączaniu 2FA i nowych kodach odzyskiwania | wysokie | bezpieczeństwo | P (r) | `Endpoints/AccountEndpoints.cs:60-83` | S | 8 |
| FAK-2 | „Ostatnia przyjęta korekta” wybierana po `Id`, nie po chwili przyjęcia | wysokie | błąd | P (r) | `Endpoints/InvoiceEndpoints.cs:577-586` | S | 5 |
| UI-2 | Podpowiedź z katalogu przy każdym znaku zjada spację i nadpisuje ręczną cenę | wysokie | błąd | P (r) | `Client/Pages/InvoiceEdit.razor:269, 676-688` | S | 11 |
| PERF-6 | Wyścig wpłat: równoległe wpłaty mijają kontrolę „pozostało do zapłaty” | wysokie | błąd (wyścig) | P (r) | `Endpoints/PaymentEndpoints.cs:29-42, 73-82, 148` | S-M | 6 |
| ISO-2 = KSEF-14 | Usługi w tle bez izolacji błędów per firma (faktury cykliczne, przypomnienia, odbiór) | wysokie | bezpieczeństwo / błąd | P (r) | `Services/RecurringInvoices.cs:49-83`, `Services/Email/PaymentReminders.cs:55-85`, `Services/ReceivedInvoiceSync.cs:188-194`, `Services/InvoiceNumbering.cs:256` | M | 3 |
| KSEF-6 | Błąd zapisu jednej faktury przerywa synchronizację wszystkich firm i wymusza ponawianie co minutę | wysokie | błąd | P (t) | `Services/ReceivedInvoiceSync.cs:93-95, 125`, `Services/IssuedInvoiceImport.cs:78-79, 111` | M | 3 |
| UI-1 | Wygaśnięcie sesji: zagnieżdżony `returnUrl`, formularz logowania w powłoce, po zalogowaniu znów „Logowanie” | wysokie | błąd | P (r) | `Client/Services/ApiClient.cs:60-67`, `Client/Services/AuthService.cs`, `Client/Pages/Login.razor:91` | M | 10 |
| FAK-3 | Kurs NBP przy dacie sprzedaży późniejszej niż wystawienia: zły dzień odniesienia (art. 31a ust. 2), kurs zależny od chwili zapisu | wysokie | błąd | P (t; prawdziwy NBP nw.) | `Endpoints/InvoiceEndpoints.cs:541`, `Integrations/Nbp/NbpExchangeRates.cs:69-81` | M | 7 |
| PERF-1 | Podsumowanie eksportu i CSV wczytują i parsują wszystkie XML-e okresu (także UPO) | wysokie | wydajność | P (r) | `Services/AccountingExport.cs:45-69`, `Endpoints/ExportEndpoints.cs:16-48` | M | 16 |
| KSEF-4 | Kolejka wysyłek: 50 najstarszych, zablokowane i czekające na UPO głodzą nowe (także innych firm) | wysokie | błąd | P (t) | `Services/KsefSubmissionProcessor.cs:40-47, 60` | M | 4 |
| KSEF-3 | `Pending` bez wyjścia po przyjęciu do przetwarzania (usunięty/odrzucony token, brak sesji) | wysokie | błąd | P (t) | `Services/KsefSubmissionProcessor.cs:72-74, 143-158`, `Endpoints/KsefSettingsEndpoints.cs:71-79` | L | 4 |
| KSEF-5 | Brak odstępów między ponowieniami: `Rejected` po ok. 30 s, odpytywanie co 3 s bez końca | wysokie | błąd / wydajność | P (k) | `Services/KsefSubmissionProcessor.cs:31, 151`, `Services/KsefSubmissionWorker.cs:37-47` | L | 4 |
| KSEF-2 | Brak ochrony przed równoległym przetwarzaniem tej samej wysyłki (dwie instancje) | wysokie | błąd | **O** | `Services/KsefSubmissionProcessor.cs:40-61` | - | - |
| SEC-3 | Limit „auth” 10/min na IP obejmuje sprawdzenie sesji przy każdym starcie aplikacji | średnie | błąd / bezpieczeństwo | P (k) | `Program.cs:236`, `Infrastructure/RateLimits.cs:40`, `Client/Services/AuthService.cs:27` | S | 8 |
| SEC-2 | Przekierowanie po logowaniu na obcą domenę (`returnUrl`) | średnie | bezpieczeństwo | P (k) | `Client/Pages/Login.razor:95` | S | 8 |
| ISO-4 | Księgowa może uruchomić synchronizację z KSeF i zapisać wynik sprawdzenia rachunku | średnie | bezpieczeństwo | P (k) | `Endpoints/ReceivedInvoiceEndpoints.cs:23, 99, 186` | S | 9 |
| FAK-8 | Import faktury „Zapłacono” nie tworzy płatności | średnie | błąd | P (k) | `Services/IssuedInvoiceImport.cs:169` | S | 13 |
| PERF-7 | Zamiana proformy bez ochrony przed równoległym wywołaniem - dwie faktury z jednej proformy | średnie | błąd (wyścig) | P (r) | `Endpoints/InvoiceEndpoints.cs:256, 265-280`, `Data/AppDbContext.cs:171` | S | 6 |
| KSEF-8 | Limit „raz na minutę” ręcznej synchronizacji i importu nie jest atomowy | średnie | błąd (wyścig) | P (k) | `Endpoints/ReceivedInvoiceEndpoints.cs:193`, `Endpoints/InvoiceEndpoints.cs:289` | S | 6 |
| FAK-6 | Dwie korekty tej samej faktury wysłane równolegle przechodzą kontrolę stanu | średnie | błąd (wyścig) | P (k; wyścigu nie odtwarzano) | `Endpoints/InvoiceEndpoints.cs:166-200, 593-607` | S | 6 |
| KSEF-13 | Przejściowa niedostępność NBP trwale wyłącza harmonogram faktury cyklicznej w walucie | średnie | błąd | P (t) | `Services/RecurringInvoices.cs:58-67, 103-104` | S | 7 |
| KSEF-11 | Błędy przejściowe (429, 5xx) przy uwierzytelnianiu zgłaszane jako odrzucenie tokenu | średnie | błąd | P (k) | `Ksef/KsefGateway.cs:279-282` | S | 4 |
| TST-3 (= FAK-12 dla NBP) | Odpowiedź NBP inna niż JSON oraz niedostępny KSeF przy zapisie tokenu → 500 | średnie | błąd | P (k; NBP r) | `Integrations/Nbp/NbpExchangeRates.cs:50-52`, `Endpoints/KsefSettingsEndpoints.cs:37, 59` | S | 7, 14 |
| FAK-5 | `P_14_xW` korekty walutowej liczony z różnicy, nie jako różnica stanów (0,01 zł) | średnie | błąd | P (k; r) | `Contracts/Calculation/InvoiceCalculator.cs:51-54` | S | 5 |
| UI-4 | `step="0.01"` blokuje zapis cen z więcej niż 2 miejscami, bez komunikatu | średnie | błąd | P (k) | `Client/Pages/InvoiceEdit.razor:283`, `Client/Pages/Products.razor:77` | S | 11 |
| UI-7 | Zaproszona osoba bez konta po rejestracji trafia do zakładania firmy | średnie | błąd | P (k) | `Client/Pages/Login.razor:63`, `Client/Pages/Register.razor:65-68` | S | 9 |
| UI-8 | Wiersz listy `<tr role="button">`: dwa wpisy historii, brak semantyki tabeli | średnie | błąd / dostępność | P (k) | `Client/Pages/Invoices.razor:97-99`, `Client/Pages/ReceivedInvoices.razor:107` | S | 12 |
| PERF-8 | Faktury cykliczne: szkic i przesunięcie harmonogramu w dwóch `SaveChanges` | średnie | błąd | P (k) | `Services/RecurringInvoices.cs:109, 69-76` | S | 3 |
| ISO-5 | Po odebraniu dostępu wybór firmy po cichu wraca do własnej - zapisy trafiają do innej firmy | średnie | błąd | P (k, serwer; klient nw.) | `Infrastructure/CurrentCompany.cs:38-57` | M | 9 |
| PERF-9 | Listy kontrahentów i katalogu obcinane po cichu do 500/1000 | średnie | błąd / wydajność | P (k) | `Endpoints/CustomerEndpoints.cs:31`, `Endpoints/ProductEndpoints.cs:28` | M | 12 |
| FAK-4 | Ilość i cena z większą skalą niż w bazie: `TotalGross` ≠ `P_15` (0,01) | średnie | błąd | nw. (r) | `Contracts/Dtos/InvoiceDtos.cs:114-120`, `Endpoints/InvoiceEndpoints.cs:503` | S | 11 |
| UI-5 | Logowanie, rejestracja, wylogowanie bez obsługi braku sieci | średnie | błąd | nw. (r) | `Client/Services/AuthService.cs:52, 87, 97` | S | 10 |
| UI-3 | Odpytywanie statusu trwa po opuszczeniu strony; jeden błąd sieci usuwa fakturę z ekranu | średnie | błąd / wydajność | nw. (r) | `Client/Pages/InvoiceDetails.razor:335-369` | S-M | 10 |
| UI-6 | Listy z filtrami: starsza odpowiedź nadpisuje nowszą; opóźnione wyszukiwanie zmienia adres innej strony | średnie | błąd | nw. (r) | `Client/Pages/Invoices.razor:169-253`, `Client/Pages/ReceivedInvoices.razor:159-216` | M | 12 |
| UI-9 | Błąd pobierania danych wygląda jak „brak danych” (pusty formularz firmy) | średnie | błąd | nw. | `Client/Pages/Company.razor:98`, `Client/Pages/InvoiceEdit.razor:431`, `Client/Services/CompanyContext.cs:28` | M | 10 |
| UI-10 | Utrata wypełnionego formularza faktury (wygaśnięcie sesji, nawigacja) | średnie | błąd | nw. | `Client/Pages/InvoiceEdit.razor:652-663` | M | 10 |
| KSEF-12 | Odrzucony token blokuje pętlę wysyłek wszystkich firm na ok. 60 s w każdym przebiegu | średnie | wydajność | nw. | `Ksef/KsefGateway.cs:276-288` | M | 4 |
| KSEF-7 | Trwale wadliwa faktura zatrzymuje punkt kontrolny na zawsze | średnie | błąd | nw. | `Services/ReceivedInvoiceSync.cs:74-99`, `Services/IssuedInvoiceImport.cs:75-88` | M | 13 |
| KSEF-9 | Budżet zapytań liczony na synchronizację, limity KSeF są godzinowe | średnie | błąd | nw. (limity MF nieznane) | `Services/ReceivedInvoiceSync.cs:30-32`, `Services/IssuedInvoiceImport.cs:28-30` | M | 14 |
| SEC-4 | Aplikacja jako przekaźnik wiadomości: limit tylko na firmę, liczony bez blokady, konta bez potwierdzenia adresu | średnie | bezpieczeństwo | nw. | `Services/Email/EmailOutbox.cs:52-70`, `Endpoints/EmailEndpoints.cs:18-43` | M | 14 (+6) |
| SEC-5 | Wspólny dzienny limit wykazu VAT wyczerpie kilka świeżych kont | średnie | bezpieczeństwo | nw. | `Integrations/VatWhiteList/VatRegistryLookup.cs:92-120` | M | 14 |
| FAK-7 | Stawki WDT/eksport/„np” dostępne, ale nabywca zagraniczny trafia do XML jako `BrakID` | średnie | błąd | nw. | `Ksef/Fa3/Fa3XmlGenerator.cs:122-127`, `Contracts/Enums/VatRate.cs:39` | S (blokada) / L (pełna) | 15 |
| PERF-3 | Dwa `Include` kolekcji w jednym zapytaniu (pozycje × wysyłki z XML) | średnie | wydajność | nw. (r) | `Endpoints/InvoiceEndpoints.cs:113, 153, 228, 345` | S | 16 |
| PERF-4 | Szczegóły faktury pobierają XML i UPO wszystkich wysyłek | średnie | wydajność | nw. | `Endpoints/InvoiceEndpoints.cs:89-92`, `Infrastructure/Mapping.cs:107` | M | 16 |
| PERF-5 | Pulpit i podsumowanie płatności sumują nierozliczone dokumenty w pamięci, dwa razy | średnie | wydajność | nw. | `Endpoints/PaymentEndpoints.cs:111-139`, `Endpoints/DashboardEndpoints.cs:48-72` | M | 16 |
| PERF-2 | Paczka ZIP i CSV budowane w całości w pamięci | średnie | wydajność | nw. | `Services/AccountingExport.cs:143-174` | M | 16 |
| PERF-10 | Indeksy niedopasowane do zapytań; indeks zdublowany | średnie | wydajność | N (dublet potwierdzony; plany bez `EXPLAIN`) | `Data/AppDbContext.cs:138-141, 194, 208` | M | 16 (+2) |
| UI-11 | Service worker: pliki z `?v=` nigdy z pamięci SW; brak informacji o nowej wersji | średnie | błąd / wydajność | N (wymaga builda publikacyjnego) | `Client/wwwroot/service-worker.published.js:36-47` | M | 19 |
| TST-2 | Brak sterowania czasem w testach; dzień UTC mieszany z polskim | średnie | testy | nw. (r) | `Tests/Infrastructure/ApiFactory.cs:84-90` | M | 1 |
| TST-4 | Atrapa KSeF „zbyt uprzejma” - ukrywa klasy błędów z obszaru 03 | średnie | testy | nw. (potwierdza to potrzeba opakowania atrapy w tym kroku) | `Tests/Infrastructure/FakeKsefGateway.cs` | M | 4, 18 |
| TST-5 | Testy tylko w konfiguracji deweloperskiej; pętle usług w tle bez testów | średnie | testy | nw. | `Tests/Infrastructure/ApiFactory.cs:62-78` | M | 18 |
| TST-6 | Brak testów współbieżności i testu konwencji endpointów | średnie | testy | nw. | `Tests/KsefSendingTests.cs:236-248` | M | 6, 9 |
| FAK-9 | „Dzisiaj” według zegara serwera zamiast czasu polskiego | niskie | błąd | P (k) | `Endpoints/InvoiceEndpoints.cs:304`, `Contracts/Dtos/InvoiceDtos.cs:150` | S | 11 |
| SEC-6 | Podgląd kursu NBP: dowolna data → zapytania wychodzące bez ograniczeń, 500 dla skrajnej daty | niskie | bezpieczeństwo / błąd | P (t) | `Endpoints/ExchangeRateEndpoints.cs:14-28`, `Integrations/Nbp/NbpExchangeRates.cs:72-86` | S | 7 |
| SEC-11 | HSTS z ustawieniami domyślnymi | niskie | bezpieczeństwo | P (k) | `Program.cs:206` | S | 8 |
| SEC-10 | Endpointy Identity bez działającej poczty: brak resetu hasła; zmiana e-maila bez hasła | niskie | bezpieczeństwo / błąd | P (k) | `Program.cs:236-237` | S (blokada) / M (reset) | 8 |
| SEC-9 | Token zaproszenia w adresie URL trafia do logów proxy | niskie | bezpieczeństwo | P (k) | `deploy/Caddyfile:6-9`, `Endpoints/MemberEndpoints.cs:62, 158` | S | 14 |
| ISO-6 | Zapytania pomocnicze korekt bez warunku `CompanyId` (obrona w głąb) | niskie | bezpieczeństwo | P (k) | `Endpoints/InvoiceEndpoints.cs:577-581, 595`, `Services/Email/PaymentReminders.cs:67` | S | 5 |
| KSEF-15 | Kod 200 bez numeru KSeF traktowany jak odrzucenie | niskie | błąd | P (t; czy KSeF tak odpowiada - nieznane) | `Ksef/KsefGateway.cs:27-28` | S | 4 |
| FAK-14 | Brak górnych granic kwot i dat: przepełnienie → 500 | niskie | błąd | P (t) | `Contracts/Dtos/InvoiceDtos.cs:114-155` | S | 11 |
| FAK-10 | PDF: cena jednostkowa zawsze z dwoma miejscami | niskie | błąd | P (k) | `Services/InvoicePdfRenderer.cs:209` | S | 11 |
| UI-12 | Przypomnienie o płatności: kwota zawsze z dopiskiem PLN | niskie | błąd | P (k) | `Client/Components/InvoiceEmailPanel.razor:24` | S | 11 |
| PERF-11 | Wyszukiwanie `q`: `ILIKE '%…%'` bez limitu długości i neutralizacji `%`/`_` | niskie | wydajność / błąd | P (t) | `Endpoints/InvoiceEndpoints.cs:56-63` i 3 inne | S | 12 |
| PERF-13 | Numeracja: 3 próby nie wystarczają przy równoległych zapisach (409 bez podanego numeru) | niskie | błąd (wyścig) | P (r) - skutek świadomej decyzji D17 | `Endpoints/InvoiceEndpoints.cs:265-280, 318-336`, `Services/InvoiceNumbering.cs:27-43` | M | 6 |
| PERF-17 | Produkcyjnie każde polecenie SQL trafia do logu | niskie | wydajność | P (k) | `appsettings.json:5-10` | S | 16 |
| KSEF-16 | Automatyczne przypomnienia co godzinę wczytują całą historię faktur po terminie, N+1 | niskie | wydajność | P (k) | `Services/Email/PaymentReminders.cs:54-71` | M | 16 |
| TST-7 | Testy przechodzące niezależnie od sprawdzanego zachowania | niskie | testy | P (k) | `Tests/AuthTests.cs:48`, `Tests/ProductTests.cs:78`, `Tests/PaymentTests.cs:150` | S | 18 |
| TST-12 | Nieaktualne liczby w `docs/testy.md`, prototyp w rozwiązaniu, powtórzone kroki testów | niskie | uproszczenie | P (k) | `docs/testy.md:11-14`, `FakturowanieKSeF.sln:18` | S | 18 |
| KSEF-17 | `KsefTestOnlyHandler` nie widzi przekierowań HTTP | niskie | bezpieczeństwo (w głąb) | N (brak wyłączenia przekierowań potwierdzony; wykonania nie testowano) | `Ksef/KsefTestOnlyHandler.cs:15-26`, `Ksef/KsefServiceCollectionExtensions.cs:22` | S | 14 |
| SEC-7 | Weryfikacja i generowanie tokenu KSeF bez ograniczenia częstotliwości | niskie | bezpieczeństwo | nw. | `Endpoints/KsefSettingsEndpoints.cs:27-71` | S | 14 |
| SEC-8 | Surowe komunikaty wyjątków infrastruktury w odpowiedziach API | niskie | bezpieczeństwo | nw. | `Services/Email/EmailOutbox.cs:98`, `Endpoints/KsefSettingsEndpoints.cs:60` | S | 14 |
| ISO-7 | Luki w testach ról i izolacji | niskie | testy | nw. | `Tests/CompanyRoleTests.cs:52-91` | M | 9 |
| KSEF-18 | Bufor tokenów dostępu nie jest unieważniany po 401 | niskie | błąd | nw. | `Ksef/KsefGateway.cs:266-296` | S | 4 |
| FAK-11 | Czytnik FA pomija elementy innych wystawców (`P_9B`, `P_10`, `P_11A`, zaliczkowe) | niskie | błąd | nw. | `Ksef/Fa3/Fa3InvoiceReader.cs:35-72` | M | 13 |
| FAK-13 | Eksport CSV: kolumny stawek nie sumują się do „Razem”; podsumowanie pomija VAT w PLN faktur walutowych | niskie | błąd | nw. | `Services/AccountingExport.cs:30-39, 72-83` | S | 13 |
| UI-15 | Stan paneli płatności i e-maili zostaje po przejściu do innej faktury | niskie | błąd | nw. (w raporcie „do weryfikacji”) | `Client/Components/PaymentPanel.razor:109`, `Client/Components/InvoiceEmailPanel.razor:88` | S | 12 |
| UI-16 | Usuwanie bez potwierdzenia, akcje bez blokady podwójnego kliknięcia | niskie | błąd | nw. | `Client/Components/CompanyMembers.razor:109-139` i inne | S | 12 |
| UI-17 | Drobne przypadki brzegowe (pusta strona `/cykliczne/nowa`, wykres ujemny, dzień w UTC) | niskie | błąd | nw. | `Client/Pages/RecurringInvoiceEdit.razor:86` i inne | S | 12 |
| PERF-14 | Kolejka e-mail: limit przed zapisem, brak zajęcia wiadomości, historia bez retencji | niskie | błąd / wydajność | nw.; pkt 1 = SEC-4, pkt 2 zob. odrzucone KSEF-2 | `Services/Email/EmailOutbox.cs:59-106` | M | 6, 19 |
| PERF-12 | Stan faktury korygowanej: wczytywane wszystkie korekty z pozycjami | niskie | wydajność | nw. | `Endpoints/InvoiceEndpoints.cs:574-597` | S | 5 |
| PERF-15 | Koszty stałe: `DbContext` bez puli, firma śledzona, pełne encje | niskie | wydajność | nw. | `Program.cs:21`, `Infrastructure/CurrentCompany.cs:32-67` | M | 16 |
| UI-13 | Zapytania sekwencyjne i zbędne przy wczytywaniu stron | niskie | wydajność | nw. | `Client/Pages/InvoiceEdit.razor:429-504`, `Client/Pages/Home.razor` | M | 16 |
| UI-14 | Formularz faktury: kosztowny render przy każdym klawiszu | niskie | wydajność | nw. (w raporcie „do weryfikacji”) | `Client/Pages/InvoiceEdit.razor:99-105, 272, 305` | M | 17 |
| KSEF-19 | Powtórzony kod synchronizacji odbioru i importu | niskie | uproszczenie | nw. | `Services/ReceivedInvoiceSync.cs:37-125`, `Services/IssuedInvoiceImport.cs:40-112` | M | 17 |
| FAK-15 | Sumy faktury liczone ponownie w czterech miejscach | niskie | uproszczenie | nw. | `Endpoints/InvoiceEndpoints.cs:503-568`, `Infrastructure/Mapping.cs:80-118`, `Ksef/Fa3/Fa3XmlGenerator.cs:157` | L | 17 |
| PERF-16 | Powtórzony kod endpointów, wyrażenie „kwota należna”, bliźniacze klasy usług | niskie | uproszczenie | nw. | `Endpoints/*.cs` | L | 17 |
| UI-18 | Martwe reguły w `app.css` | niskie | uproszczenie | nw. | `Client/wwwroot/css/app.css` | S | 17 |
| UI-19 | Powtórzony kod stron - kandydaci na komponenty | niskie | uproszczenie | nw. | `Client/Pages/Invoices.razor`, `Client/Pages/ReceivedInvoices.razor` i inne | L | 17 |
| TST-11 | Opcje konfiguracji jako literały, bez klas opcji i walidacji przy starcie | niskie | uproszczenie | nw. | `Program.cs:117-180`, usługi w tle | M | 17 |
| TST-8 | Wspólna baza i atrapa na klasę testów - wynik zależy od kolejności | niskie | testy | nw. | `Tests/Infrastructure/ApiFactory.cs:42-46` | M | 18 |
| TST-9 | E2E: jedna kolekcja (3 min), selektory zależne od stylu | niskie | testy | nw. | `Tests/E2E/E2EFixture.cs:98` | M | 18 |
| TST-10 | CI: brak limitu czasu, artefaktów i pokrycia | niskie | jakość procesu | nw. | `.github/workflows/ci.yml` | S | 18 |
| UI-20 | Luki w pokryciu E2E | niskie | testy | nw. | `Tests/E2E/UserScenariosE2ETests.cs` | (z poprawkami) | przy gałęziach 9-12 |

Spoza numeracji (sekcje „Poza obszarem”): **kontrola stanu kontenera a `AllowedHosts`** (`Dockerfile:20-21` pyta
z `Host: localhost`, `deploy/docker-compose.prod.yml:35` dopuszcza tylko domenę) - niepewne, zgodne z kodem, wymaga
uruchomienia kontenera; gałąź 19. Przepełnienie `int` w `InvoiceNumbering` (numer `FV/-2147483648/…`) - potwierdzone
testem w raporcie 02, naprawiane w gałęzi 3. Przycisk „Pobierz z KSeF” widoczny dla księgowej - interfejs do ISO-4, gałąź 9.

## 3. Plan poprawek

Jedna gałąź = jeden PR do `main` (merge commit), nazwy po polsku zgodnie z `CLAUDE.md`. Kolejność uwzględnia wagę,
zależności (ta sama część kodu, migracje) i to, że część gałęzi czeka na odpowiedź autora (sekcja 7 - numer pytania
w kolumnie „Decyzja”). Każda gałąź aktualizuje właściwe dokumenty (`docs/decyzje-projektowe.md`, a przy zmianie
endpointów `docs/api.md`, przy migracji `docs/model-danych.md`).

| Nr | Gałąź | Znaleziska | Zakres | Testy do dopisania | Decyzja | Koszt |
|---|---|---|---|---|---|---|
| 1 | `fix/testy-zegar-i-stale-daty` | TST-1, TST-2 | `FakeTimeProvider` w `ApiFactory` (stała chwila, np. 10.11.2026 12:00 czasu polskiego), „dziś” w testach z `PolishDate.Today(factory.Time)`. **Do scalenia przed 16.11.2026.** Warunek dla testów czasowych w gałęziach 4, 6, 7. | `PaymentApiTests.Filtry_…` jako `[Theory]` dla dwóch dat zegara; `RecurringInvoiceApiTests.TerminLiczonyWedlugCzasuPolskiego`; `PaymentReminderTests.KolejnePrzypomnienie_DopieroPo7Dniach` z `Advance` | P26 | M |
| 2 | `fix/numer-ksef-unikalny-w-firmie` | ISO-1 = KSEF-10, PERF-10 (pkt 3) | Migracja: unikalny `(CompanyId, KsefNumber)` zamiast globalnego `IX_Invoices_KsefNumber`; naruszenie unikalności w imporcie = „już znana”; ewentualnie unikalny `Companies.Nip` | `IssuedImportTests.DwieFirmyOTymSamymNip_ImportujaTeSameFakturyNiezaleznie`; `KsefSendingTests.NumerKsefZajetyPrzezInnaFirme_NieBlokujeWysylekPozostalychFirm` | P1 | S |
| 3 | `fix/izolacja-bledow-uslug-w-tle` | ISO-2 = KSEF-14, KSEF-6, PERF-8, część KSEF-4 (zapis poza `try`), przepełnienie numeracji | `try/catch` i osobny zakres DI (lub `ChangeTracker.Clear()`) na firmę/harmonogram/wysyłkę we wszystkich pięciu pętlach; odłączanie encji po nieudanym zapisie; szkic + harmonogram w jednej transakcji; `LastSyncAt` zapisywany na początku; numeracja w `long` z pominięciem wartości spoza zakresu | `RecurringInvoiceApiTests.BladHarmonogramuJednejFirmy_NieWstrzymujeHarmonogramowInnychFirm`; `ReminderTests.BladJednejFirmy_NieZatrzymujePrzypomnienInnych`; `ReceivedInvoicesTests.RownoleglaSynchronizacja_NieKonczySieBledemAniDuplikatem`; `ReceivedInvoicesWorkerTests.BladJednejFirmy_NiePrzerywaSynchronizacjiPozostalych`; `RecurringInvoices_FailureAfterDraftDoesNotDuplicate` | - | M |
| 4 | `fix/wysylka-ksef-odpornosc` | KSEF-1, KSEF-3, KSEF-4, KSEF-5, KSEF-11, KSEF-12, KSEF-15, KSEF-18, TST-4 (część) | Dwa utrwalane kroki (sesja, potem dokument) i sprawdzenie w KSeF po skrócie przed ponowieniem; 440 = „już przyjęta”; `NextAttemptAt` z rosnącym odstępem i `Retry-After`; stan „wymaga wyjaśnienia” zamiast wiecznego `Pending`; blokada usunięcia tokenu przy wysyłkach w toku; 429/5xx z uwierzytelniania jako błąd przejściowy; rozbudowa `FakeKsefGateway` (`FailAfterAccept`, `ProcessingPolls`, liczniki). Migracja + `docs/integracja-ksef.md`. Można podzielić na dwa PR: (a) KSEF-1/11/15/18, (b) KSEF-3/4/5/12. Wynik sprawdzić testem na żywo uruchamianym ręcznie przez autora. | `KsefSendingTests.OdpowiedzUtraconaPoPrzyjeciu_NieWysylaDrugiRaz_INieOznaczaOdrzuconej`; `…ZablokowaneWysylki_NieGlodzaNowych`; `…UsuniecieTokenuPodczasPrzetwarzania_NieZostawiaFakturyWPendingBezWyjscia`; `…KrotkaNiedostepnoscKsef_NiePrzerywaWysylki`; `…LimitZadanPrzyUwierzytelnianiu_JestPonawiany`; `…Status200BezNumeruKsef_NieOznaczaOdrzuconej`; `…UpoNiegotowe_FakturaPrzyjeta_UpoPobraneWKolejnymPrzebiegu` | P2, P6, P7, P11 | L |
| 5 | `fix/korekty-stan-przed-korekta` | FAK-1, FAK-2, FAK-5, ISO-6, PERF-12 | Stan „przed” z kwot zapisanych/dokumentu albo blokada korekty faktury importowanej, której aplikacja nie odtwarza; sortowanie po `KsefAcceptedAt`; `P_14_xW` korekty jako różnica stanów; warunek `CompanyId` w zapytaniach pomocniczych | `IssuedImportTests.KorektaFakturyImportowanejZRabatem_RoznicaZgodnaZDokumentem`; `CorrectionTests.KolejnaKorekta_StanPrzedZKorektyPrzyjetejJakoOstatnia_NieZNajwiekszymId`; `CurrencyTests.KorektyWalutowe_PodatekWPlnSumujeSieDoZeraPoKorekcieDoZera`; `DataModelTests.KorektaZFakturaKorygowanaInnejFirmy_JestIgnorowana` | P12, P16 | M-L |
| 6 | `fix/wyscigi-sprawdz-potem-zapisz` | PERF-6, PERF-7, FAK-6, KSEF-8, PERF-13, SEC-4 (licznik) = PERF-14 (pkt 1), TST-6 (część) | Jeden wzorzec, jedna gałąź: wersja/blokada wiersza nadrzędnego przy wpłacie i przy wysyłce korekty; unikalny indeks częściowy `ProformaId`; atomowa rezerwacja `LastSyncAt` (`ExecuteUpdate`); numeracja pod `pg_advisory_xact_lock(companyId)`; atomowy licznik e-mail. Pomocnik testowy `Concurrently(n, action)`. Po gałęzi 5 (ten sam plik). | `Payments_ConcurrentPostsCannotOverpay`; `ConvertProforma_ConcurrentCallsCreateSingleInvoice`; `CorrectionTests.DwieKorektyWyslaneRownolegle_JednaDostaje409`; `ReceivedInvoicesTests.RownolegleReczneSynchronizacje_TylkoJednaLaczySieZKsef`; `CreateInvoice_ParallelAutoNumbering_AllSucceedWithDistinctNumbers`; `EmailApiTests.LimitDzienny_RownolegleZadania_NiePrzekraczajaLimitu` | P17 | M |
| 7 | `fix/kurs-nbp-dzien-odniesienia` | FAK-3, TST-3 (NBP) = FAK-12, SEC-6, KSEF-13 | Dzień odniesienia `min(sprzedaż, wystawienie)`; brak buforowania dla dat późniejszych niż dziś; zakres dat w podglądzie kursu; `JsonException` → `NbpException`; błąd NBP nie wyłącza harmonogramu. Uzupełnienie D43 o art. 31a ust. 2. | `CurrencyTests.FakturaWystawionaPrzedSprzedaza_KursSprzedDniaWystawienia`; `…KursDlaDatySpozaZakresu_Zwraca400`; `ExchangeRateLookupTests.KlientNbp_OdpowiedzHtml_ToNbpException`; `RecurringInvoiceTests.NiedostepnyNbp_NieWylaczaHarmonogramu_ISzkicPowstajePoPowrocie` | - | M |
| 8 | `fix/logowanie-2fa-i-limity-sesji` | SEC-1, SEC-2, SEC-3, SEC-10, SEC-11 | Licznik nieudanych kodów + limit na `/account/2fa`; `SafeReturnUrl`; polityka `Auth` tylko na logowaniu/rejestracji/odświeżaniu; HSTS 365 dni; blokada nieużywanych ścieżek Identity albo reset hasła | `TwoFactorTests.Wylaczenie_PoKilkuZlychKodach_BlokujeKolejneProby`; test jednostkowy `SafeReturnUrl`; `RateLimitTests.SprawdzenieSesji_NieZuzywaLimituLogowania`; `SecurityHeadersTests.Hsts_MaRocznyOkres`; `AuthTests.ZmianaEmaila_BezHasla_JestOdrzucana` | P20, P21, P23 | M |
| 9 | `fix/role-i-zakladanie-firmy` | ISO-3, ISO-4, ISO-5, UI-7, ISO-7, TST-6 (konwencja) | Rozdzielenie „utwórz własną firmę” od „edytuj bieżącą”; filtr ról (lub zapis w D46) dla `/received-invoices`; 409 przy utracie dostępu do wybranej firmy; `returnUrl` w rejestracji; ukrycie przycisku synchronizacji dla księgowej | `CompanyRoleTests.CzlonekCudzejFirmy_BezWlasnej_MozeZalozycWlasnaFirme`; `…Ksiegowa_NieUruchamiaSynchronizacjiZKsef`; `…OdebranieDostepu_PrzyWlasnejFirmie_NiePrzenosiZapisowDoWlasnejFirmy`; `…MacierzRol_KazdyEndpointZmieniajacy`; `EndpointConventionTests.KazdyEndpointApi_WymagaLogowania_AZmieniajacyDane_MaFiltrRoli`; E2E rejestracja z linku zaproszenia | P3, P4, P5 | M |
| 10 | `fix/sesja-wygasla-i-bledy-sieci` | UI-1, UI-5, UI-9, UI-10, UI-3 | Jedno przekierowanie po 401 z unieważnieniem stanu `AuthService`; obsługa wyjątków sieci w `AuthService`; `ProblemAlert` zamiast „brak danych”; `NavigationLock`; odpytywanie statusu z flagą `disposed` i rosnącym odstępem | E2E `SesjaWygasla_PrzekierowanieNaLogowanie_IPowrotNaStrone`; E2E z `page.RouteAsync` (przerwane logowanie, 500 na `api/company`, przerwane odświeżenie statusu) | P11 | M |
| 11 | `fix/formularz-faktury-katalog-i-precyzja` | UI-2, UI-4, FAK-4, FAK-9, FAK-10, FAK-14, UI-12 | Podpowiedź z katalogu tylko przy wyborze; wspólny atrybut skali w `Contracts` (ilość ≤ 6, cena ≤ 8, wpłata ≤ 2) + `step="any"`; granice kwot i dat; `PolishDate.Today`; format ceny w PDF; waluta w przypomnieniu | E2E „nazwa z katalogu + dopisek” i „ręczna cena zostaje”; `InvoiceApiTests.IloscZSiedmiomaMiejscami_JestOdrzucana`; test własności `TotalGross == P_15`; `…KwotaPozaZakresem_400_ZamiastBledu500`; `…NastepnyNumer_NaPrzelomieMiesiaca_WedlugCzasuPolskiego`; `PdfTests.CenaZWielomaMiejscami_JestDrukowanaBezObciecia` | P18, P19 | M |
| 12 | `fix/listy-filtry-i-nawigacja` | UI-6, UI-8, PERF-9, PERF-11, UI-15, UI-16, UI-17 | Odrzucanie nieaktualnych odpowiedzi, `replace: true`, link na cały wiersz; stronicowanie kontrahentów/katalogu i wyszukiwanie po stronie serwera; neutralizacja `%`/`_` i limit długości `q`; `@key` na panelach; potwierdzenia usuwania | E2E opóźniona odpowiedź dla pierwszej frazy; E2E „wstecz” po kliknięciu numeru; `Customers_ListBeyondLimitIsPagedNotTruncated`; `InvoiceSearch_TreatsWildcardsLiterally` | - | M |
| 13 | `fix/import-i-odczyt-faktur-z-ksef` | FAK-8, FAK-11, FAK-13, KSEF-7 | Płatność `FromInvoice` przy imporcie „Zapłacono”; czytnik: `P_9B`, `P_10`, `P_11A`, zaliczkowe; kolumna „inne” w CSV; faktura nieczytelna zapisywana z metadanymi zamiast blokować punkt kontrolny. Po gałęzi 5 (wspólny czytnik). | `IssuedImportTests.FakturaZaplaconaPrzyWystawieniu_PoImporcieJestOplacona`; `ReceivedInvoicesTests.Odczyt_FakturaOdBrutto_ZRabatem_IZaliczkowa`; `…FakturaKtorejNieDaSiePrzetworzyc_NieBlokujeKolejnych`; `ExportTests.KolumnyStawekSumujaSieDoRazem_DlaFakturyMarza` | - | M |
| 14 | `fix/limity-uslug-zewnetrznych-i-naduzycia` | KSEF-9, SEC-4, SEC-5, SEC-7, SEC-8, SEC-9, KSEF-17, TST-3 (KSeF) | Budżet godzinowy KSeF wspólny dla odbioru/importu/trybu ręcznego; limit prób tokenu; limit e-mail globalny i na konto (ew. potwierdzanie adresu); komunikaty ogólne zamiast `ex.Message`; token zaproszenia poza logami; `AllowAutoRedirect = false`; 502 przy niedostępnym KSeF | `ReceivedInvoicesTests.BudzetGodzinowy_ObejmujeOdbiorIImportLacznie`; `KsefSettingsTests.WeryfikacjaTokenu_PoKilkuProbach_Zwraca429`; `…KsefNiedostepny_PrzyZapisieTokenu_Zwraca502`; `EmailTests.BladWysylki_NieUjawniaSzczegolowSerwera`; `KsefEnvironmentGuardTests.PrzekierowaniePozaSrodowiskoTestowe_NieJestWykonywane` | P8, P22 | M-L |
| 15 | `feature/nabywca-zagraniczny-vat-ue` (albo `fix/blokada-stawek-zagranicznych`) | FAK-7 | Pola identyfikatora zagranicznego i `KodUE`/`NrVatUE` w XML - albo na teraz blokada stawek WDT/eksport bez identyfikatora | `Fa3XmlTests.NabywcaZUE_MaNrVatUE_AWdtBezNumeruJestOdrzucane` | decyzja o zakresie | S / L |
| 16 | `fix/wydajnosc-eksportu-i-zapytan` | PERF-1, PERF-2, PERF-3, PERF-4, PERF-5, PERF-10, PERF-15, PERF-17, KSEF-16, UI-13 | Agregacje w SQL, projekcje bez kolumn XML, strumieniowany ZIP, indeksy (po `EXPLAIN` na danych testowych ~100 tys. wierszy), wyciszenie logu SQL | Testy z interceptorem poleceń: `ExportSummary_DoesNotReadXmlColumns`, `InvoiceDetails_DoesNotSelectXmlColumns`, `PaymentsSummary_AggregatesInDatabase`; `HostingTests` dla poziomu logowania | P24 | L |
| 17 | `refactor/wspolny-kod-uslug-i-stron` | PERF-16, FAK-15, KSEF-19, TST-11, UI-14, UI-18, UI-19 | Wspólna pętla usług i synchronizacji „do HWM”, `InvoiceWriter`, jedno wyrażenie „kwota należna”, klasy opcji z `ValidateOnStart`, komponenty `Pager`/`Loader`, usunięcie martwego CSS (+ `?v=`). Bez zmiany zachowania; po gałęziach 3-6. | Istniejący zestaw jako regresja; `HostingTests.NieprawidlowaKonfiguracja_OdmowaStartu`; test spójności kwoty należnej (lista = szczegóły = podsumowanie) | - | L |
| 18 | `fix/testy-atrapa-ksef-i-ci` | TST-4 (reszta), TST-5, TST-7, TST-8, TST-9, TST-10, TST-12, UI-20 | `ProductionApiFactory`; stronicowanie i `IsTruncated` w atrapie; asercje kroków przygotowania; odpowiedzi atrapy kluczowane NIP; podział E2E na kolekcje; limit czasu, artefakty i pokrycie w CI; aktualizacja `docs/testy.md` | `ProductionHostingTests.*`; `ReceivedInvoicesTests.WieleStron_PobieraWszystkoWKolejnychPrzebiegach`; test kontraktowy atrapa/bramka | P27, P28, P29 | M-L |
| 19 | `fix/wdrozenie-kontrola-stanu-i-pwa` | kontrola stanu a `AllowedHosts`, UI-11, PERF-14 (pkt 3) | `Host` w kontroli stanu lub `AllowedHosts` z `localhost`; `ignoreSearch` w service workerze i komunikat o nowej wersji; retencja treści wiadomości. Najpierw potwierdzić uruchomieniem kontenera i buildem publikacyjnym. | krok CI `docker-image`: kontener `healthy`; rozszerzenie `Pwa_AplikacjaDoInstalacji_…` | P25 | M |

Minimalny zestaw przed oddaniem pracy: gałęzie 1-3 (małe, usuwają jedno krytyczne i trzy wysokie oraz czerwone CI),
potem 4 i 5 (pozostałe dwa krytyczne), 6-8. Gałęzie 16-19 można opisać w pracy jako dalsze kierunki.

## 4. Odrzucone znaleziska

| ID | Dlaczego odrzucone |
|---|---|
| KSEF-2 (wysokie) | Scenariusz wymaga dwóch instancji aplikacji. `docs/podsumowanie.md` („Ograniczenia”) stwierdza wprost: „Jedna instancja aplikacji: usługi w tle i limity żądań działają w pamięci procesu (skalowanie poziome wymagałoby blokad w bazie lub kolejki zewnętrznej)”; wdrożenie D48 to jeden kontener, a w jednej instancji pętla `KsefSubmissionWorker` jest pojedyncza i żaden endpoint nie wywołuje procesora. To świadome ograniczenie, nie błąd. Zostaje zalecenie: dopisać je do `docs/decyzje-projektowe.md` i `docs/wdrozenie.md` (zakaz uruchamiania dwóch replik, także na czas podmiany wersji). |
| PERF-14 pkt 2, PERF-8 (wariant „dwie instancje”) | Ta sama podstawa co KSEF-2. PERF-8 pozostaje potwierdzone w wariancie restartu między dwoma zapisami, PERF-14 w punktach 1 (= SEC-4) i 3. |
| Uwaga z raportu 03 („Poza obszarem”): unikalny indeks `OwnerId` niespójny z D46 | Zgodne z D46: konto może należeć do wielu firm (członkostwa) i mieć **jedną** własną. Nieaktualny jest tylko komentarz „1 konto = 1 firma (D4)” w `AppDbContext.cs:41`. |

Nie odrzucono żadnego znaleziska jako nieprawdziwego: wszystkie sprawdzone opisy zgadzały się z kodem. Dwa znaleziska
dotyczą skutków świadomych decyzji i zachowują status z adnotacją: PERF-13 (D17: „do 3 prób”) i SEC-5 (D28: limit na
użytkownika - obejście wynika z braku weryfikacji kont).

## 5. Luki w testach

Do uzupełnienia razem z poprawkami (nazwy testów w planie, sekcja 3):

1. **Czas** - serwer w testach używa prawdziwego zegara (TST-1, TST-2). Bez `FakeTimeProvider` nie da się przetestować
   odstępów ponowień, okna limitów, wygaśnięcia zaproszeń i bufora tokenu.
2. **Współbieżność** - w całym projekcie jest jedno `Task.WhenAll`. Brak testów dla wpłat, proformy, korekt, numeracji,
   limitu e-mail i ręcznej synchronizacji (TST-6).
3. **Atrapa KSeF** - nie symuluje przyjęcia z utraconą odpowiedzią, stanu „w przetwarzaniu”, drugiej strony wyników,
   `IsTruncated`, 429 ani błędu pojedynczej faktury (TST-4). W tym kroku trzeba było opakować atrapę, żeby potwierdzić
   KSEF-1 i KSEF-6.
4. **Izolacja błędów między firmami** - żaden test nie sprawdza, że błąd firmy A nie zatrzymuje firmy B; pętle
   `ReceivedInvoicesWorker`, `RecurringInvoiceWorker` i `PaymentReminderWorker` nie są wykonywane (TST-5).
5. **Role i konwencja endpointów** - kilkanaście endpointów zmieniających dane bez asercji 403, brak testu po
   `EndpointDataSource` (ISO-7, TST-6).
6. **Konfiguracja produkcyjna** - HSTS, `Secure`, nagłówki proxy, brak Swaggera, domyślnie włączone limity (TST-5).
7. **Korekty i import** - kolejność korekt, korekta faktury importowanej (rabat, ceny brutto), korekty walutowe w serii.
8. **Spójność kwot** - brak testu własności „suma w bazie = `P_15` w XML = podsumowanie w DTO” dla wartości granicznych skali.
9. **Błędy usług zewnętrznych** - NBP zwracający HTML, niedostępny KSeF przy zapisie tokenu, wygaśnięcie sesji i brak
   sieci w E2E (UI-20).
10. **Jakość istniejących testów** - kroki przygotowania bez asercji (TST-7), zależność od kolejności (TST-8).

## 6. Wnioski do pracy inżynierskiej

Po zakończeniu implementacji przeprowadzono ustrukturyzowany przegląd kodu w siedmiu obszarach: bezpieczeństwo,
izolacja danych i role, integracja z KSeF i usługi w tle, poprawność merytoryczna faktur, wydajność i baza danych,
frontend oraz testy i jakość. Każdy obszar miał własną listę pytań kontrolnych, a każde znalezisko musiało wskazywać
konkretny kod i scenariusz „dane wejściowe → zły wynik”; przegląd był wyłącznie odczytowy, bez połączeń z KSeF, NBP
i wykazem podatników VAT. Zgłoszono 101 znalezisk (3 krytyczne, 15 wysokich, 41 średnich, 42 niskie), z czego według
rodzaju wskazanego jako pierwszy: 59 błędów, 16 luk bezpieczeństwa, 15 nieefektywności, 8 uproszczeń i 3 luki w testach. W kroku końcowym zweryfikowano
niezależnie wszystkie znaleziska krytyczne i wysokie oraz 47% pozostałych: 53 potwierdzono, 1 odrzucono jako świadome
ograniczenie (praca w jednej instancji), 3 uznano za niepewne, a 44 przyjęto z raportów obszarów bez ponownej weryfikacji.
Przegląd nie wykazał żadnej możliwości odczytu ani zmiany danych innej firmy, co potwierdza skuteczność przyjętego
modelu izolacji (D15, D46). Najważniejsza obserwacja jest taka, że izolacja danych nie oznacza izolacji dostępności:
wspólne kolejki usług w tle i globalny indeks numeru KSeF pozwalały jednemu użytkownikowi zatrzymać wysyłkę faktur
wszystkim firmom. Drugi wniosek dotyczy testów: 290 testów integracyjnych i 26 scenariuszy E2E przechodziło, a mimo to
nie wykrywały błędów krytycznych, ponieważ atrapa KSeF nie odtwarzała awarii częściowych (przyjęcie dokumentu z utraconą
odpowiedzią), a zestaw nie zawierał testów współbieżności ani sterowania czasem. Trzeci wniosek: błędy merytoryczne
skupiły się na styku z danymi spoza aplikacji (korekty faktur zaimportowanych z KSeF), gdzie założenie „netto = ilość ×
cena” przestaje obowiązywać. Poprawki zaplanowano w 19 gałęziach uporządkowanych według wagi; do każdej przypisano testy,
które wykryłyby dany błąd. *(Do uzupełnienia po wykonaniu: liczba poprawionych znalezisk i dodanych testów.)*

## 7. Pytania do autora

Zebrane ze wszystkich raportów, bez powtórzeń. W nawiasie raport źródłowy i znaleziska, których kształt zależy od odpowiedzi.

**Model firm i wdrożenie**
- **P1.** Czy dwie firmy o tym samym NIP są dozwolone (np. właściciel i biuro rachunkowe na osobnych kontach)? (02, 03 → ISO-1: indeks per firma czy unikalny NIP)
- **P2.** Czy aplikacja ma zawsze działać jako jedna instancja, także podczas wdrażania nowej wersji? (03, 05 → KSEF-2, PERF-8, PERF-14)
- **P3.** Czy członek cudzej firmy ma móc założyć własną po przyjęciu zaproszenia i czy zaproszona osoba ma po rejestracji widzieć formularz zakładania firmy? (02, 06 → ISO-3, UI-7)
- **P4.** Czy księgowa ma móc uruchamiać synchronizację faktur otrzymanych i sprawdzenie rachunku? (02 → ISO-4)
- **P5.** Czy `GET /api/company/notifications` i `GET /api/company/ksef` mają być czytelne dla każdej roli (pracownik widzi e-mail właściciela)? (02)

**KSeF**
- **P6.** Co ma zobaczyć i móc zrobić użytkownik, gdy faktura po przyjęciu do przetwarzania nie dostaje statusu końcowego przez godziny lub dni? (03 → KSEF-3)
- **P7.** Czy usunięcie tokenu KSeF przy wysyłkach w toku ma być dozwolone? (03 → KSEF-3)
- **P8.** Jakie są obowiązujące limity API KSeF (minutowe, godzinowe; na token czy na IP)? (03 → KSEF-9, SEC-7)
- **P9.** Czy limit trzech przypomnień ma obejmować przypomnienia ręczne i nieudane wysyłki? (03)
- **P10.** Wygenerowanie nowego tokenu testowego nie cofa poprzedniego w KSeF - czy to zamierzone? (03)
- **P11.** Czy odpytywanie faktury `Accepted` bez UPO ma trwać bez limitu; ile realnie czeka się na UPO w testowym KSeF? (06 → UI-3, KSEF-5)

**Faktury**
- **P12.** Korekty faktur importowanych: zostawić (stan „przed” z kwot dokumentu) czy wyłączyć dla dokumentów, których aplikacja nie odtwarza? (04 → FAK-1)
- **P13.** Czy przy wysyłce szkicu sprzed kilku dni data wystawienia ma być aktualizowana albo ma pojawić się ostrzeżenie (art. 106na ust. 1)? (04)
- **P14.** Czy numeracja `NrWierszaFa` w korektach (kolejno „przed”, potem „po”) jest świadoma? (04)
- **P15.** Czy numer powinien być nadawany dopiero przy wysyłce, a nie szkicowi (luki po usuniętych szkicach)? (04)
- **P16.** Czy korekty walutowe z nowej przyczyny (inny kurs niż faktury korygowanej) są świadomie poza zakresem? (04 → FAK-5)
- **P17.** Czy nadpłata (suma wpłat > kwota należna) ma być w ogóle możliwa, np. po korekcie in minus? (05 → PERF-6: blokada wiersza czy także ograniczenie w bazie)
- **P18.** Jaka precyzja ceny jednostkowej jest zamierzona: 2 miejsca (serwer odrzuca więcej) czy do 8 jak w FA(3)? (06 → UI-4, FAK-4)
- **P19.** Czy podpowiedź z katalogu ma działać także przy ręcznym wpisaniu pełnej nazwy, czy tylko po wyborze z listy? (06 → UI-2)

**Bezpieczeństwo**
- **P20.** Czy brak resetu hasła jest świadomy; jeśli tak, czy nieużywane ścieżki Identity zablokować? (01 → SEC-10)
- **P21.** Czy logowanie tokenem Bearer ma pozostać w produkcji (tokeny odświeżania żyją 14 dni i nie są unieważniane przy wylogowaniu)? (01)
- **P22.** Czy planowane jest potwierdzanie adresu e-mail przy rejestracji? Zamyka naraz SEC-4, SEC-5 i scenariusz SEC-9. (01)
- **P23.** Czy wyłączenie 2FA ma dodatkowo wymagać hasła? (01 → SEC-1)

**Skala, wdrożenie, testy**
- **P24.** Jaki jest oczekiwany rząd wielkości danych jednej firmy (faktur na rok)? Przy mniej niż 1000 PERF-1/PERF-2 są mało dotkliwe. (05)
- **P25.** Czy mieszanie wersji po wdrożeniu było obserwowane po wprowadzeniu `?v=`; czy wymagane jest powiadomienie o nowej wersji w PWA? (06 → UI-11)
- **P26.** Czy stałe daty „2026-11” w danych testowych są celowe (zgodność ze zrzutami w pracy)? Jeśli tak - zamrozić zegar serwera w testach zamiast przeliczać daty. (07 → TST-1)
- **P27.** Czy prototyp `tools/FakturowanieKSeF.KsefSpike` ma zostać w repozytorium i czy może wyjść z głównego rozwiązania? (07 → TST-12)
- **P28.** Czy pomiar pokrycia kodu ma trafić do pracy? (07 → TST-10)
- **P29.** Czy testy na żywo mają być uruchamiane okresowo (np. ręcznie wyzwalane zadanie CI)? (07)
