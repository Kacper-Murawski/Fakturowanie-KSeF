# Raport: Obszar 03 - integracja z KSeF i usługi w tle
Status: ZAKOŃCZONY
Ostatnia aktualizacja: 2026-11-11 (koniec przeglądu)

## Postęp
- [x] dokumentacja: `docs/integracja-ksef.md`, decyzje D8, D10, D11, D24-D27, D44, D51
- [x] `FakturowanieKSeF.Ksef/KsefGateway.cs` (+ bufor tokenów dostępu)
- [x] `KsefTestOnlyHandler.cs`, `KsefEnvironment.cs`, `KsefQr.cs`, `KsefServiceCollectionExtensions.cs`
- [x] `Services/KsefSubmissionProcessor.cs`, `KsefSubmissionWorker.cs` (maszyna stanów, równoległość, utknięcia, backoff)
- [x] `Endpoints/InvoiceEndpoints.cs` (`/send`, `/import-from-ksef`), `KsefSettingsEndpoints.cs`
- [x] `Services/ReceivedInvoiceSync.cs` + `Endpoints/ReceivedInvoiceEndpoints.cs` (HWM, deduplikacja, budżet, raz/min)
- [x] `Services/IssuedInvoiceImport.cs`
- [x] `Services/RecurringInvoices.cs`
- [x] `Services/Email/EmailOutbox.cs`, `PaymentReminders.cs`, `ReceivedInvoiceNotifier.cs`, `EmailSender.cs`
- [x] testy: `KsefSendingTests.cs`, `ReceivedInvoicesTests.cs`, `IssuedImportTests.cs`, `Infrastructure/FakeKsefGateway.cs` (luki pokrycia)
- [x] klient MF (`external/ksef-client-csharp`): `AuthCoordinator`, `RestClient.ErrorHandling`, wyjątki, circuit breaker - tylko zachowanie
- [x] podsumowanie i sekcje końcowe
Następny krok: brak - przegląd zakończony.

## Podsumowanie

Ścieżka „szczęśliwa” wysyłki i odbioru jest poprawna, a zasada D25 (po zapisaniu numeru referencyjnego nigdy `Rejected`
z powodu błędu komunikacji) jest zaimplementowana i przetestowana. Najpoważniejsze ryzyko leży tuż przed nią: gdy KSeF
przyjmie fakturę, a aplikacja nie zdąży zapisać numeru referencyjnego (utracona odpowiedź, awaria zapisu, restart, druga
instancja), faktura jest wysyłana ponownie albo oznaczana `Rejected`, mimo że istnieje w KSeF (KSEF-1, KSEF-2).
Druga grupa to odporność usług w tle: brak odstępów między ponowieniami, kolejka ograniczona do 50 najstarszych wysyłek,
stan `Pending` bez wyjścia oraz jeden wadliwy rekord, który zatrzymuje przebieg dla wszystkich firm (KSEF-3 - KSEF-7, KSEF-12).
Limity API KSeF nie są faktycznie egzekwowane: limit „raz na minutę” jest nieatomowy, a budżet liczony na synchronizację,
nie na godzinę (KSEF-8, KSEF-9).

Wszystkie znaleziska wynikają z lektury kodu aplikacji i klienta MF; żadnego nie potwierdzałem uruchomieniem testu
(nie tworzyłem plików `Tmp03*`, nie łączyłem się z KSeF). Tam, gdzie skutek zależy od zachowania prawdziwego KSeF lub
sposobu wdrożenia, pewność jest oznaczona jako „do weryfikacji”.

Liczba znalezisk: krytyczne 1, wysokie 5, średnie 8, niskie 5. Luk bezpieczeństwa pozwalających na wyciek cudzych danych
w tym obszarze nie znalazłem; KSEF-10 to zakłócenie pracy innej firmy, a KSEF-17 to ochrona „w głąb”.

## Znaleziska

### KSEF-1. Faktura przyjęta przez KSeF bez zapisanego numeru referencyjnego jest wysyłana ponownie lub oznaczana `Rejected`
- **Waga:** krytyczne
- **Rodzaj:** błąd
- **Pewność:** potwierdzone lekturą kodu (ścieżka w aplikacji); zachowanie KSeF przy drugiej wysyłce (status 440) wg
  `external/ksef-client-csharp/KSeF.Client.Core/Models/ApiResponses/InvoiceInSessionStatusCodeResponse.cs:64` - do weryfikacji na żywo.
- **Miejsce:** `FakturowanieKSeF.Api/Services/KsefSubmissionProcessor.cs:77-86`, `:60`, `:143-158`;
  `FakturowanieKSeF.Ksef/KsefGateway.cs:148-177`; `FakturowanieKSeF.Api/Services/IssuedInvoiceImport.cs:119-122`
- **Problem:** jedynym dowodem „faktura mogła trafić do KSeF” jest `InvoiceReferenceNumber`, zapisywany dopiero po powrocie
  z `SendInvoiceAsync` i po `SaveChangesAsync`. Otwarcie sesji i wysłanie faktury to jedno wywołanie bramki, a numer sesji
  nie jest utrwalany przed wysłaniem. Jeśli KSeF przyjmie fakturę, a odpowiedź nie dotrze (timeout, zerwane połączenie),
  padnie zapis do bazy albo proces zostanie zatrzymany między liniami 79 a 60, aplikacja uznaje, że faktura „na pewno nie trafiła do KSeF”.
- **Scenariusz:** (a) `SendOnlineSessionInvoiceAsync` rzuca `HttpRequestException`/`TaskCanceledException` po przyjęciu przez KSeF →
  `RecordFailure`, `notSentYet == true` → kolejny przebieg (po 3 s) otwiera nową sesję i wysyła ten sam XML drugi raz. KSeF
  odpowiada wtedy statusem 440 „Duplikat faktury” → krok [2] ustawia `Rejected`, choć pierwsza wysyłka ma numer KSeF.
  (b) Dziesięć takich błędów z rzędu → `Rejected` „Nie wysłano: KSeF niedostępny”, faktura jest w KSeF, a użytkownik może ją
  edytować, usunąć lub wysłać ponownie. (c) Import D44 nie naprawi stanu: `KnownAsync` rozpoznaje dokument po skrócie SHA-256
  starej wysyłki (także usuniętej faktury - `IgnoreQueryFilters`) i go pomija, więc faktura z KSeF nigdy nie pojawi się w aplikacji.
- **Propozycja poprawki:** rozdzielić krok [1] na dwa utrwalane kroki: otwarcie sesji (zapis `SessionReferenceNumber`) i wysłanie
  faktury. Przy ponowieniu z zapisaną sesją najpierw sprawdzić w KSeF listę faktur sesji po skrócie dokumentu, a dopiero potem
  wysyłać. Status 440 traktować jako „już przyjęta”: odczytać numer KSeF ze szczegółów i ustawić `Accepted`. Przerwanie po 10
  próbach dopuszczać tylko, gdy żadna próba nie doszła do etapu wysłania dokumentu. W imporcie D44 dopasowanie po skrócie
  powinno uzupełniać numer KSeF istniejącej faktury zamiast ją pomijać.
- **Test, który to wykryje:** `KsefSendingTests.OdpowiedzUtraconaPoPrzyjeciu_NieWysylaDrugiRaz_INieOznaczaOdrzuconej` -
  atrapa zapisuje fakturę w `SentInvoices`, po czym rzuca `HttpRequestException`; asercje: `SentInvoices.Count == 1` po kolejnych
  przebiegach i status różny od `Rejected`. Dziś `FakeKsefGateway.SendInvoiceAsync` rzuca `SendFailure` przed zapisem faktury,
  więc ten przypadek nie jest modelowany.

### KSEF-2. Brak ochrony przed równoległym przetwarzaniem tej samej wysyłki (dwie instancje usługi)
- **Waga:** wysokie
- **Rodzaj:** błąd
- **Pewność:** do weryfikacji - zależy od wdrożenia (czy kiedykolwiek działają dwie instancje, np. przy podmianie wersji);
  brak mechanizmu w kodzie potwierdzony lekturą.
- **Miejsce:** `FakturowanieKSeF.Api/Services/KsefSubmissionProcessor.cs:40-61`; `FakturowanieKSeF.Models/Models/KsefSubmission.cs`
  (brak tokenu współbieżności); analogicznie `Email/EmailOutbox.cs:79-106`, `RecurringInvoices.cs:42-76`, `Email/PaymentReminders.cs:54-84`
- **Problem:** kontrola wersji (`Invoice.Version`) chroni tylko endpoint `/send`. Usługa w tle pobiera otwarte wysyłki zwykłym
  `SELECT`, bez `FOR UPDATE SKIP LOCKED`, bez tokenu współbieżności na `KsefSubmission` i bez blokady. W obrębie jednej
  instancji pętla jest pojedyncza, więc problemu nie ma; przy dwóch instancjach każda wyśle tę samą fakturę.
- **Scenariusz:** dwie instancje odczytują wysyłkę bez numeru referencyjnego, obie wywołują `SendInvoiceAsync`, późniejszy zapis
  nadpisuje numery wcześniejszego. Jeśli wygra zapis drugiej wysyłki (440 w KSeF), faktura kończy jako `Rejected` mimo
  przyjęcia pierwszej. Te same warunki dają podwójne e-maile, dwa szkice faktury cyklicznej i podwójne przypomnienia.
- **Propozycja poprawki:** przejmowanie pracy atomowo (`UPDATE … WHERE … RETURNING` albo `FOR UPDATE SKIP LOCKED` w transakcji),
  ewentualnie blokada doradcza PostgreSQL na usługę; `xmin` jako token współbieżności `KsefSubmission`. Jeśli założeniem jest
  jedna instancja - zapisać to w `docs/decyzje-projektowe.md` i w dokumentacji wdrożenia.
- **Test, który to wykryje:** `KsefSendingTests.DwaRownoleglePrzebiegiProcesora_WysylajaFaktureRaz` - dwa `ProcessAsync` w osobnych
  zakresach DI uruchomione równolegle, atrapa z opóźnieniem w `SendInvoiceAsync`; asercja `SentInvoices.Count == 1`.

### KSEF-3. `Pending` bez wyjścia po przyjęciu faktury do przetwarzania
- **Waga:** wysokie
- **Rodzaj:** błąd
- **Pewność:** potwierdzone lekturą kodu; przypadek usuniętej sesji w czyszczonym środowisku testowym - do weryfikacji.
- **Miejsce:** `FakturowanieKSeF.Api/Services/KsefSubmissionProcessor.cs:72-74`, `:90-98`, `:143-158`;
  `FakturowanieKSeF.Api/Endpoints/KsefSettingsEndpoints.cs:71-79`; `FakturowanieKSeF.Api/Endpoints/InvoiceEndpoints.cs:160-161`, `:349-350`, `:375-376`
- **Problem:** dla wysyłki z numerem referencyjnym każdy błąd jest ponawiany bez końca i bez stanu „wymaga interwencji”.
  Gdy błąd jest trwały, faktura zostaje `Pending` na zawsze, a w tym stanie nie można jej edytować, usunąć ani wysłać ponownie.
- **Scenariusz:** (1) właściciel usuwa token KSeF (`DELETE /company/ksef/token` nie sprawdza wysyłek w toku), gdy faktura ma
  status 150 → każdy przebieg rzuca „Firma nie ma skonfigurowanego tokenu KSeF”. (2) Token cofnięty lub zastąpiony tokenem
  odrzucanym przez KSeF. (3) Sesja nie istnieje już po stronie KSeF (środowisko testowe jest okresowo czyszczone) →
  `GetSessionInvoiceAsync` zwraca błąd przy każdym odpytaniu. (4) KSeF nigdy nie wychodzi ze statusu 150. Użytkownik widzi
  „Problemy z połączeniem z KSeF (N)… Aplikacja ponawia próby” (`InvoiceDetails.razor:109-111`) i nie ma żadnej akcji.
- **Propozycja poprawki:** po określonym czasie lub liczbie prób przejść w jawny stan „wymaga wyjaśnienia” (nie `Rejected`),
  z rosnącym odstępem ponowień i akcją użytkownika „sprawdź ponownie w KSeF”. Usunięcie tokenu blokować (409) albo ostrzegać,
  gdy istnieją wysyłki w toku. Dla brakującego tokenu nie zwiększać licznika i nie logować ostrzeżenia co 3 s.
- **Test, który to wykryje:** `KsefSendingTests.UsuniecieTokenuPodczasPrzetwarzania_NieZostawiaFakturyWPendingBezWyjscia` oraz
  `…StalyBladStatusuPoPrzyjeciu_PrzechodziWStanWymagajacyInterwencji`.

### KSEF-4. Kolejka wysyłek ograniczona do 50 najstarszych - zablokowane lub czekające na UPO głodzą nowe
- **Waga:** wysokie
- **Rodzaj:** błąd
- **Pewność:** potwierdzone lekturą kodu.
- **Miejsce:** `FakturowanieKSeF.Api/Services/KsefSubmissionProcessor.cs:40-47`, `:60`
- **Problem:** każdy przebieg bierze `OrderBy(Id).Take(50)` spośród wszystkich otwartych wysyłek wszystkich firm, łącznie
  z przyjętymi bez UPO (do 7 dni) i trwale zablokowanymi (KSEF-3). Nie ma rotacji ani odroczenia, więc gdy takich rekordów
  jest 50, nowsze wysyłki nie są przetwarzane wcale. Dodatkowo wyjątek z `SaveChangesAsync` w linii 60 nie jest łapany
  i przerywa przebieg dla pozostałych rekordów; jeśli jest powtarzalny, blokuje wszystko za wadliwym rekordem.
- **Scenariusz:** 50 faktur przyjętych, dla których UPO nie jest jeszcze dostępne (np. nieudane zamknięcie sesji), albo
  50 wysyłek firm z usuniętym tokenem → kolejna kliknięta „Wyślij” faktura dowolnej firmy zostaje `Pending` bez żadnej próby.
- **Propozycja poprawki:** kolumna `NextAttemptAt` z odstępem rosnącym po błędzie i sortowanie po niej; osobne, rzadsze
  zapytanie dla UPO; zapis każdego rekordu w `try/catch` z odłączeniem wadliwej encji od kontekstu.
- **Test, który to wykryje:** `KsefSendingTests.ZablokowaneWysylki_NieGlodzaNowych` - 50 wysyłek z trwałym błędem statusu
  plus jedna nowa; po przebiegu nowa ma numer referencyjny.

### KSEF-5. Brak odstępów między ponowieniami: `Rejected` po ok. 30 s niedostępności, odpytywanie KSeF co 3 s bez końca
- **Waga:** wysokie
- **Rodzaj:** błąd / wydajność
- **Pewność:** potwierdzone lekturą kodu.
- **Miejsce:** `FakturowanieKSeF.Api/Services/KsefSubmissionProcessor.cs:31`, `:122-127`, `:130-141`, `:151`;
  `FakturowanieKSeF.Api/Services/KsefSubmissionWorker.cs:37-47`; `FakturowanieKSeF.Ksef/KsefGateway.cs:208-211`
- **Problem:** gdy jest jakakolwiek otwarta wysyłka, pętla działa co 3 s i każdy rekord jest próbowany w każdym przebiegu.
  Dziesięć prób przed wysłaniem wyczerpuje się w ok. 30 s przy szybkim błędzie (odmowa połączenia, otwarty circuit breaker
  klienta MF - `KsefCircuitBreakerOpenException`). Po przyjęciu status i UPO są odpytywane co 3 s bez limitu. Nagłówek
  `Retry-After` z `KsefRateLimitException` jest ignorowany. Zamknięcie sesji jest próbowane raz; po niepowodzeniu UPO
  powstanie dopiero po samoczynnym wygaśnięciu sesji, a do tego czasu aplikacja pyta o nie co 3 s.
- **Scenariusz:** półminutowa przerwa KSeF → faktury oczekujące na wysłanie dostają `Rejected` „KSeF niedostępny po 10
  próbach” (dokumentacja obiecuje ponawianie). Jedna przyjęta faktura bez UPO = ok. 1200 zapytań na godzinę, do 7 dni.
- **Propozycja poprawki:** ponawianie z odstępem wykładniczym (np. 5 s → 15 min) i limitem czasu zamiast liczby prób;
  uwzględnienie `Retry-After`; UPO odpytywać rzadko (minuty, potem godziny); ponawiać zamknięcie sesji.
- **Test, który to wykryje:** `KsefSendingTests.KrotkaNiedostepnoscKsef_NiePrzerywaWysylki` (z `FakeTimeProvider`: 10 błędów
  w ciągu minuty, potem sukces → `Accepted`) oraz `…UpoNiedostepne_NieJestOdpytywaneWKazdymPrzebiegu` (licznik wywołań atrapy).

### KSEF-6. Błąd zapisu jednej faktury przerywa synchronizację wszystkich firm i wymusza ponawianie co minutę
- **Waga:** wysokie
- **Rodzaj:** błąd
- **Pewność:** potwierdzone lekturą kodu (po nieudanym `SaveChanges` encja pozostaje w stanie `Added` - zachowanie EF Core);
  nie uruchamiałem testu.
- **Miejsce:** `FakturowanieKSeF.Api/Services/ReceivedInvoiceSync.cs:93-95`, `:116-125`, `:183-194`;
  `FakturowanieKSeF.Api/Services/IssuedInvoiceImport.cs:78-79`, `:102-111`
- **Problem:** `DbUpdateException` z zapisu pojedynczej faktury jest łapany, ale wadliwa encja zostaje w kontekście. Końcowy
  `SaveChangesAsync` (poza `try`) próbuje ją zapisać ponownie i rzuca. Nie zapisują się wtedy `LastSyncAt`, `LastSyncError`
  ani punkt kontrolny. W usłudze cyklicznej wyjątek wychodzi z pętli `foreach` po firmach (jeden kontekst dla wszystkich
  firm), więc pozostałe firmy są pomijane, a firma z błędem pozostaje „do synchronizacji” i wraca po minucie zamiast po 15.
- **Scenariusz:** synchronizacja cykliczna i ręczna tej samej firmy biegną jednocześnie (zob. KSEF-8): obie widzą
  `AnyAsync == false`, obie pobierają fakturę, druga narusza unikalny indeks `(CompanyId, KsefNumber)` → odpowiedź 500
  w żądaniu ręcznym albo przerwanie cyklu. Gdy przyczyna jest stała (KSEF-10), firma jest odpytywana w KSeF co minutę
  (60 zapytań o metadane na godzinę przy limicie 20), a firmy dalej na liście nie są synchronizowane nigdy.
- **Propozycja poprawki:** przy błędzie zapisu odłączyć encję (`Entry(x).State = Detached`), naruszenie unikalności
  traktować jako „już pobrana”; osobny zakres DI i `try/catch` na firmę w pętli usługi; `LastSyncAt` zapisywać na początku.
- **Test, który to wykryje:** `ReceivedInvoicesTests.RownoleglaSynchronizacja_NieKonczySieBledemAniDuplikatem` (atrapa
  w `DownloadInvoiceAsync` wstawia ten sam wiersz innym kontekstem) oraz `…BladJednejFirmy_NieZatrzymujeSynchronizacjiPozostalych`.

### KSEF-7. Trwale wadliwa faktura zatrzymuje punkt kontrolny na zawsze
- **Waga:** średnie
- **Rodzaj:** błąd
- **Pewność:** mechanizm potwierdzony lekturą kodu; konkretne dane wywołujące wyjątek - do weryfikacji (wymaga analizy
  `Fa3InvoiceReader` i rzeczywistych odpowiedzi KSeF).
- **Miejsce:** `FakturowanieKSeF.Api/Services/ReceivedInvoiceSync.cs:74-99`, `:142-152`;
  `FakturowanieKSeF.Api/Services/IssuedInvoiceImport.cs:75-88`, `:127-135`
- **Problem:** punkt kontrolny przesuwa się tylko za faktury przetworzone bez wyjątku. Wyjątek powtarzalny dla jednej
  faktury (inny niż `FormatException`/`XmlException` z czytnika, `null` w metadanych przekazany do `Truncate`, stały błąd
  pobrania lub zapisu) powoduje, że każda kolejna synchronizacja zatrzymuje się w tym samym miejscu. Nie ma licznika prób
  ani pominięcia z oznaczeniem.
- **Scenariusz:** faktury otrzymane pochodzą od dowolnego podatnika, więc treść jest danymi niezaufanymi: jeden dokument,
  którego nie da się przetworzyć, blokuje odbiór wszystkich późniejszych faktur firmy; użytkownik widzi tylko komunikat
  błędu ostatniej synchronizacji.
- **Propozycja poprawki:** fakturę, której nie da się odczytać, zapisywać z samymi metadanymi i XML (odczyt terminu
  płatności w szerokim `try`), a przy powtarzającym się błędzie pobrania - po N próbach pominąć z trwałym oznaczeniem.
- **Test, który to wykryje:** `ReceivedInvoicesTests.FakturaKtorejNieDaSiePrzetworzyc_NieBlokujeKolejnych`.

### KSEF-8. Limit „raz na minutę” dla ręcznej synchronizacji i importu nie jest atomowy
- **Waga:** średnie
- **Rodzaj:** błąd
- **Pewność:** potwierdzone lekturą kodu.
- **Miejsce:** `FakturowanieKSeF.Api/Endpoints/ReceivedInvoiceEndpoints.cs:193-197`;
  `FakturowanieKSeF.Api/Endpoints/InvoiceEndpoints.cs:289-293`; `ReceivedInvoiceSync.cs:48`, `:125`; `IssuedInvoiceImport.cs:51`, `:111`
- **Problem:** endpoint porównuje `LastSyncAt` odczytany z bazy, a nowa wartość jest utrwalana dopiero po zapytaniach do
  KSeF (przy pierwszym zapisie faktury albo na końcu). Wszystkie żądania, które nadejdą w trakcie trwającej synchronizacji,
  przechodzą kontrolę. Nie ma też blokady względem przebiegu cyklicznego.
- **Scenariusz:** dwukrotne kliknięcie „Pobierz z KSeF”, dwie karty albo skrypt z 10 równoległymi żądaniami → 10
  równoległych synchronizacji, 10 razy więcej zapytań do KSeF i wyścig opisany w KSEF-6.
- **Propozycja poprawki:** rezerwacja atomowa: `ExecuteUpdateAsync` ustawiający `LastSyncAt = now` z warunkiem
  `LastSyncAt IS NULL OR LastSyncAt < now - 1 min`; zero zmienionych wierszy = 429. Dodatkowo blokada doradcza na firmę
  wspólna dla synchronizacji ręcznej i cyklicznej.
- **Test, który to wykryje:** `ReceivedInvoicesTests.RownolegleReczneSynchronizacje_TylkoJednaLaczySieZKsef` - `Task.WhenAll`
  dwóch `POST /sync` przy atrapie z opóźnieniem; asercje: jedna odpowiedź 429, `Queries.Count == 1`.

### KSEF-9. Budżet zapytań jest liczony na synchronizację, a limity KSeF są godzinowe
- **Waga:** średnie
- **Rodzaj:** błąd
- **Pewność:** rachunek potwierdzony względem limitów podanych w `docs/integracja-ksef.md` (20 zapytań i 64 pobrania na
  godzinę); rzeczywiste limity MF, w tym minutowe i ich zakres (token/IP) - do weryfikacji.
- **Miejsce:** `FakturowanieKSeF.Api/Services/ReceivedInvoiceSync.cs:30-32`, `:172`, `:188-194`;
  `FakturowanieKSeF.Api/Services/IssuedInvoiceImport.cs:28-30`
- **Problem:** odbiór i import D44 używają tych samych endpointów KSeF, każdy z własnym budżetem 3 zapytań i 40 pobrań,
  cztery razy na godzinę, plus synchronizacje ręczne co minutę. Najgorszy przypadek cykliczny to 24 zapytania o metadane
  i 320 pobrań na godzinę. 40 pobrań wykonywanych jedno po drugim może też przekroczyć limit minutowy.
- **Scenariusz:** pierwsza synchronizacja firmy z ponad 64 fakturami z ostatnich 30 dni: pierwszy cykl pobiera do 80,
  kolejny dostaje 429. Błąd jest zapisywany, a po 15 minutach próba się powtarza - bez uwzględnienia `Retry-After`.
- **Propozycja poprawki:** licznik godzinowy na firmę (lub na token) wspólny dla odbioru, importu i trybu ręcznego;
  po 429 odroczenie do czasu z `Retry-After`; odstęp między pobraniami.
- **Test, który to wykryje:** `ReceivedInvoicesTests.BudzetGodzinowy_ObejmujeOdbiorIImportLacznie` - atrapa zlicza wywołania,
  `FakeTimeProvider` przesuwa czas o 4 cykle; asercja: pobrań ≤ 64, zapytań ≤ 20.

### KSEF-10. Globalnie unikalny numer KSeF w `Invoices` przy braku unikalności NIP firm
- **Waga:** średnie
- **Rodzaj:** błąd / bezpieczeństwo (zakłócenie pracy innej firmy)
- **Pewność:** potwierdzone lekturą kodu (indeks i brak ograniczenia NIP); scenariusz nieuruchamiany.
- **Miejsce:** `FakturowanieKSeF.Api/Data/AppDbContext.cs:139`; `FakturowanieKSeF.Api/Services/IssuedInvoiceImport.cs:78-79`, `:119-120`;
  `FakturowanieKSeF.Api/Services/KsefSubmissionProcessor.cs:108`
- **Problem:** indeks `Invoices.KsefNumber` jest unikalny w całej bazie, a `KnownAsync` sprawdza numer tylko w obrębie firmy.
  Dwie firmy w aplikacji mogą mieć ten sam NIP (brak unikalności; na środowisku testowym token testowy można wygenerować
  dla dowolnego NIP - D24).
- **Scenariusz:** użytkownik B zakłada firmę z NIP firmy A, generuje token testowy i uruchamia import D44. Faktury A
  wystawione poza aplikacją trafiają do firmy B. Import firmy A dla tych samych numerów narusza indeks → `DbUpdateException`
  → trwała blokada importu firmy A i skutki KSEF-6 (przerwany cykl, ponawianie co minutę).
- **Propozycja poprawki:** indeks unikalny `(CompanyId, KsefNumber)`; naruszenie unikalności w imporcie traktować jako
  „już znana”. Osobno rozważyć, czy dwie firmy z tym samym NIP są dopuszczalne.
- **Test, który to wykryje:** `IssuedImportTests.DwieFirmyZTymSamymNip_ImportujaNiezaleznie`.

### KSEF-11. Błędy przejściowe przy uwierzytelnianiu są zgłaszane jako odrzucenie tokenu
- **Waga:** średnie
- **Rodzaj:** błąd
- **Pewność:** potwierdzone lekturą kodu (`KsefRateLimitException` dziedziczy po `KsefApiException`; błędy 5xx też dają `KsefApiException`).
- **Miejsce:** `FakturowanieKSeF.Ksef/KsefGateway.cs:279-282`; skutek w `KsefSubmissionProcessor.cs:151-157`
  i `KsefSettingsEndpoints.cs:37-40`
- **Problem:** każdy `KsefApiException` z uwierzytelniania, także 429 i 5xx, jest zamieniany na `KsefAuthenticationException`,
  które dla wysyłki przed przyjęciem oznacza natychmiastowe przerwanie bez ponawiania (D51 dotyczy tylko statusów 4xx
  odrzucenia tokenu).
- **Scenariusz:** token dostępu w pamięci wygasł, KSeF odpowiada 429 lub 503 na żądanie wyzwania → faktura od razu
  `Rejected` „Nie wysłano: KSeF odrzucił token (sprawdź, czy token jest aktywny…)”, choć token jest poprawny. W ustawieniach
  poprawny token jest odrzucany jako błąd walidacji pola.
- **Propozycja poprawki:** na `KsefAuthenticationException` mapować wyłącznie 400/401/403 (i rozpoznane odrzucenie 4xx);
  429 i 5xx przepuszczać jako błąd przejściowy. To samo rozróżnienie zastosować do błędów 4xx z otwarcia sesji i wysyłki,
  które dziś są ponawiane 10 razy i kończą się mylącym „KSeF niedostępny”.
- **Test, który to wykryje:** test jednostkowy `KsefGateway` z atrapą `IAuthCoordinator` rzucającą `KsefRateLimitException`
  (oczekiwany wyjątek inny niż `KsefAuthenticationException`) oraz `KsefSendingTests.LimitZadanPrzyUwierzytelnianiu_JestPonawiany`.

### KSEF-12. Odrzucony token blokuje pętlę wysyłek wszystkich firm na ok. 60 s w każdym przebiegu
- **Waga:** średnie
- **Rodzaj:** wydajność
- **Pewność:** potwierdzone lekturą `external/ksef-client-csharp/KSeF.Client/Api/Services/AuthCoordinator.cs:136-168`
  (odpytywanie co 2 s przez minutę, bez wcześniejszego wyjścia przy statusie 4xx).
- **Miejsce:** `FakturowanieKSeF.Ksef/KsefGateway.cs:276-288`; `KsefSubmissionProcessor.cs:49-61`; `ReceivedInvoiceSync.cs:188-194`
- **Problem:** ostateczne odrzucenie tokenu klient MF zgłasza dopiero po minucie odpytywania (ok. 30 żądań). Wynik negatywny
  nie jest zapamiętywany, a przetwarzanie jest sekwencyjne dla wszystkich firm.
- **Scenariusz:** wysyłka po przyjęciu (KSEF-3) lub oczekująca na UPO należy do firmy z odrzuconym tokenem → każdy przebieg
  czeka minutę, zanim obsłuży faktury innych firm. W synchronizacji cyklicznej taka firma kosztuje 2 minuty na cykl
  (odbiór + import). `PUT /company/ksef/token` z błędnym tokenem trwa 60 s.
- **Propozycja poprawki:** krótkotrwale zapamiętywać odrzucenie dla pary NIP + skrót tokenu; własne odpytywanie statusu
  uwierzytelnienia z wyjściem przy 4xx; przetwarzanie pogrupowane po firmie z limitem czasu na firmę.
- **Test, który to wykryje:** `KsefSendingTests.FirmaZOdrzuconymTokenem_NieOpozniaWysylekInnychFirm` (atrapa z opóźnieniem
  uwierzytelnienia jednej firmy, pomiar kolejności i liczby wywołań).

### KSEF-13. Przejściowa niedostępność NBP trwale wyłącza harmonogram faktury cyklicznej w walucie obcej
- **Waga:** średnie
- **Rodzaj:** błąd
- **Pewność:** potwierdzone lekturą kodu.
- **Miejsce:** `FakturowanieKSeF.Api/Services/RecurringInvoices.cs:58-67`, `:103-104`;
  `FakturowanieKSeF.Api/Endpoints/InvoiceEndpoints.cs:539-548`
- **Problem:** `ApplyAsync` zamienia `NbpException` na wynik walidacji, a `CreateDraftAsync` każdy taki wynik traktuje jako
  wadliwy wzór: ustawia `IsActive = false` z komunikatem „sprawdź fakturę-wzór”.
- **Scenariusz:** harmonogram w EUR, w chwili przebiegu `api.nbp.pl` nie odpowiada → harmonogram zostaje wyłączony na stałe
  z mylącą przyczyną; kolejne okresy nie powstaną, dopóki użytkownik go nie włączy.
- **Propozycja poprawki:** odróżnić błąd przejściowy (kurs NBP) od błędu wzoru; przy przejściowym zostawić harmonogram
  aktywny, zapisać `LastError` i spróbować w następnym przebiegu.
- **Test, który to wykryje:** `RecurringInvoiceTests.NiedostepnyNbp_NieWylaczaHarmonogramu_ISzkicPowstajePoPowrocie` (`FakeNbpClient` z awarią).

### KSEF-14. Wyjątek dla jednego rekordu przerywa przebieg faktur cyklicznych i przypomnień dla wszystkich firm
- **Waga:** średnie
- **Rodzaj:** błąd
- **Pewność:** potwierdzone lekturą kodu; konkretna przyczyna powtarzalnego wyjątku - do weryfikacji.
- **Miejsce:** `FakturowanieKSeF.Api/Services/RecurringInvoices.cs:49-83`, `:100-117`;
  `FakturowanieKSeF.Api/Services/Email/PaymentReminders.cs:55-84`
- **Problem:** pętle po harmonogramach i firmach nie mają `try/catch` na element, a jeden kontekst bazy obsługuje wszystkie
  firmy. Wyjątek (np. `DbUpdateException` inny niż duplikat numeru albo trzeci duplikat z rzędu - encja zostaje `Added`)
  kończy cały przebieg. Kolejność jest stała (`CompanyId`, `Id`), więc ten sam rekord zatrzyma każdy kolejny przebieg.
  Dodatkowo szkic i przesunięcie terminu harmonogramu to dwa osobne zapisy: restart pomiędzy nimi tworzy drugi szkic
  za ten sam okres.
- **Scenariusz:** harmonogram firmy o niskim identyfikatorze powoduje powtarzalny wyjątek → szkice i przypomnienia
  wszystkich późniejszych firm nie powstają, w logu jest tylko „Błąd usługi faktur cyklicznych” co godzinę.
- **Propozycja poprawki:** osobny zakres DI i `try/catch` na harmonogram/firmę; szkic i aktualizacja harmonogramu w jednej transakcji.
- **Test, który to wykryje:** `RecurringInvoiceTests.BladJednegoHarmonogramu_NieZatrzymujePozostalych`;
  `ReminderTests.BladJednejFirmy_NieZatrzymujePrzypomnienInnych`.

### KSEF-15. Kod 200 bez numeru KSeF jest traktowany jak odrzucenie
- **Waga:** niskie
- **Rodzaj:** błąd
- **Pewność:** do weryfikacji - nie wiadomo, czy KSeF kiedykolwiek zwraca 200 bez `ksefNumber`.
- **Miejsce:** `FakturowanieKSeF.Ksef/KsefGateway.cs:27-28`; `FakturowanieKSeF.Api/Services/KsefSubmissionProcessor.cs:98-116`
- **Problem:** `IsAccepted` wymaga kodu 200 i numeru KSeF; każdy inny wynik niebędący 100/150 kończy się `Rejected`.
  Odpowiedź 200 z pustym numerem oznaczyłaby przyjętą fakturę jako odrzuconą, wbrew zasadzie D25.
- **Scenariusz:** KSeF zwraca status 200 chwilę przed uzupełnieniem numeru → `Rejected`, sesja zamknięta, użytkownik wysyła ponownie.
- **Propozycja poprawki:** 200 bez numeru traktować jak „w toku”; odrzucenie tylko dla kodów ≥ 400.
- **Test, który to wykryje:** `KsefSendingTests.Status200BezNumeruKsef_NieOznaczaOdrzuconej` (`StatusResponses` z kodem 200 i `KsefNumber == null`).

### KSEF-16. Automatyczne przypomnienia co godzinę wczytują całą historię faktur po terminie i wykonują zapytania N+1
- **Waga:** niskie
- **Rodzaj:** wydajność
- **Pewność:** potwierdzone lekturą kodu.
- **Miejsce:** `FakturowanieKSeF.Api/Services/Email/PaymentReminders.cs:54-71`
- **Problem:** dla każdej firmy wczytywane są wszystkie przyjęte faktury VAT z terminem w przeszłości, razem z płatnościami
  i korektami, także dawno opłacone (filtr `Remaining > 0` działa w pamięci). Dla każdej nieopłaconej wykonywane są 2-3
  kolejne zapytania (historia przypomnień, adresat). Koszt rośnie z całą historią firmy, co godzinę.
- **Scenariusz:** firma z 5000 opłaconych faktur → co godzinę 5000 encji z kolekcjami w pamięci, zero wysłanych wiadomości.
- **Propozycja poprawki:** warunek „pozostało do zapłaty”, liczbę i datę ostatniego przypomnienia przenieść do zapytania SQL;
  adresy pobierać zbiorczo.
- **Test, który to wykryje:** test z licznikiem zapytań (interceptor EF): liczba zapytań nie zależy od liczby faktur opłaconych.

### KSEF-17. `KsefTestOnlyHandler` nie widzi przekierowań HTTP
- **Waga:** niskie
- **Rodzaj:** bezpieczeństwo (ochrona „w głąb”)
- **Pewność:** do weryfikacji - wynika z działania `SocketsHttpHandler` (przekierowania obsługuje handler podstawowy, poniżej
  `DelegatingHandler`); w konfiguracji klienta MF nie znalazłem wyłączenia `AllowAutoRedirect`.
- **Miejsce:** `FakturowanieKSeF.Ksef/KsefTestOnlyHandler.cs:15-26`; `FakturowanieKSeF.Ksef/KsefServiceCollectionExtensions.cs:22-24`
- **Problem:** handler sprawdza adres żądania początkowego. Odpowiedź 3xx z hosta testowego zostałaby wykonana pod nowym
  adresem bez kontroli, wbrew opisowi „sprawdza adres KAŻDEGO wychodzącego żądania”.
- **Scenariusz:** wymaga przekierowania wysłanego przez serwer testowy MF - mało prawdopodobne, ale to „ostatnia linia obrony”.
- **Propozycja poprawki:** wyłączyć automatyczne przekierowania w handlerze podstawowym klienta KSeF.
- **Test, który to wykryje:** `KsefEnvironmentGuardTests.PrzekierowaniePozaSrodowiskoTestowe_NieJestWykonywane` (handler testowy zwracający 302).

### KSEF-18. Bufor tokenów dostępu nie jest unieważniany po odpowiedzi 401
- **Waga:** niskie
- **Rodzaj:** błąd
- **Pewność:** potwierdzone lekturą kodu; częstość zależy od zachowania KSeF.
- **Miejsce:** `FakturowanieKSeF.Ksef/KsefGateway.cs:266-296`
- **Problem:** token dostępu jest używany do końca wyliczonego czasu życia. Gdy KSeF unieważni go wcześniej, każda operacja
  kończy się 401 do wygaśnięcia wpisu; przed wysłaniem faktury zużywa to 10 prób w 30 s (KSEF-5). Klucz bufora i czas życia
  są poprawne; po zmianie lub usunięciu tokenu KSeF stary wpis jest nieosiągalny i wygasa sam.
- **Scenariusz:** token dostępu unieważniony po stronie KSeF → wysyłki `Rejected` „KSeF niedostępny”, choć ponowne
  uwierzytelnienie by wystarczyło.
- **Propozycja poprawki:** po 401 z operacji usunąć wpis z bufora i ponowić raz z nowym tokenem dostępu.
- **Test, który to wykryje:** test jednostkowy `KsefGateway`: pierwsze wywołanie operacji zwraca 401 → drugie uwierzytelnienie i sukces.

### KSEF-19. Powtórzony kod synchronizacji odbioru i importu oraz drobne nieefektywności
- **Waga:** niskie
- **Rodzaj:** uproszczenie / wydajność
- **Pewność:** potwierdzone lekturą kodu.
- **Miejsce:** `FakturowanieKSeF.Api/Services/ReceivedInvoiceSync.cs:37-125` i `IssuedInvoiceImport.cs:40-112`;
  `ReceivedInvoiceSync.cs:65`, `:178-194`; `IssuedInvoiceImport.cs:67`; `Email/EmailSender.cs:57-62`; `KsefGateway.cs:148-177`
- **Problem:** (1) pętla „strony → pobranie → punkt kontrolny → HWM” jest skopiowana w dwóch klasach; każda poprawka
  z KSEF-6 - KSEF-9 musi trafić w oba miejsca. (2) Sprawdzenie „czy znana” to osobne zapytanie na fakturę (do 300 na
  synchronizację) zamiast jednego na stronę. (3) Usługa cykliczna używa jednego kontekstu dla wszystkich firm, więc wszystkie
  pobrane XML pozostają śledzone do końca cyklu. (4) Każda wiadomość e-mail to nowe połączenie SMTP z logowaniem.
  (5) Każda faktura otwiera i zamyka własną sesję KSeF.
- **Scenariusz:** koszt rośnie z liczbą firm i faktur; brak błędnego wyniku.
- **Propozycja poprawki:** wspólna klasa synchronizacji „do HWM” sparametryzowana rolą podmiotu i sposobem zapisu; zapytanie
  `WHERE KsefNumber IN (…)` na stronę; zakres DI na firmę; jedno połączenie SMTP na partię.
- **Test, który to wykryje:** istniejące testy synchronizacji i importu po refaktoryzacji; licznik zapytań dla punktu (2).

## Sprawdzone bez uwag

- `KsefEnvironment.cs`, `KsefServiceCollectionExtensions.cs`: adres bazowy ze stałej, brak wartości produkcyjnej; jedyny
  klient HTTP biblioteki MF (`AddHttpClient<IRestClient, RestClient>`) ma dopięty `KsefTestOnlyHandler` (poza KSEF-17).
- `KsefQr.cs`: budowa linku KOD I i obrazu - bez uwag.
- Endpoint `POST /api/invoices/{id}/send`: `CompanyId` z `CurrentCompany`, kontrola wersji, walidacja XSD przed zapisem,
  nic nie wysyła do KSeF w żądaniu.
- Token KSeF: zapis tylko zaszyfrowany, DTO zwraca wyłącznie informację o istnieniu; klucz bufora to skrót tokenu.
- Zasada D25 dla błędów po zapisaniu numeru referencyjnego (`RecordFailure`) - zgodna z decyzją i pokryta testem
  `AwariaSieciPoWyslaniu_NigdyNieOznaczaFakturyJakoOdrzuconej`.
- Punkt kontrolny HWM w ścieżce bez błędów: sortowanie rosnące, granica `from` włączna z deduplikacją, HWM tylko po pełnym
  przejściu bez `IsTruncated`, przerwanie na limicie pobrań zostawia punkt na ostatniej przetworzonej fakturze.
- Faktury cykliczne: strefa czasowa (`PolishDate.Today`), koniec miesiąca (`Next`), jeden szkic po przestoju, data końcowa,
  usunięcie wzoru (zablokowane w `DELETE /invoices/{id}`), usunięty kontrahent.
- Kolejka e-mail: ponawianie z rosnącymi odstępami i stanem końcowym `Failed`, wadliwa wiadomość nie blokuje kolejki,
  czyszczenie nagłówków ze znaków sterujących.
- Wszystkie usługi w tle: nowy zakres DI na przebieg, obsługa `CancellationToken` przy zatrzymaniu, wyjątek nie kończy pętli usługi.

## Poza obszarem

- `ReceivedInvoiceEndpoints.cs:23` - grupa `/received-invoices` nie ma filtra roli (`RequireRoleForChanges`), więc `POST /sync`
  może wywołać każdy członek firmy. Do oceny w obszarze 02, czy to zamierzone.
- `AppDbContext.cs:41-42` - komentarz „1 konto = 1 firma (D4)” i unikalny indeks `OwnerId` wyglądają na niespójne z D46
  (wiele firm na koncie). Nie sprawdzałem migracji.

## Pytania do autora

1. Czy aplikacja ma działać zawsze jako jedna instancja (także podczas wdrażania nowej wersji)? Od tego zależy waga KSEF-2.
2. Co ma zobaczyć i móc zrobić użytkownik, gdy faktura po przyjęciu do przetwarzania nie dostaje statusu końcowego przez
   godziny lub dni (KSEF-3)?
3. Czy usunięcie tokenu KSeF przy wysyłkach w toku ma być dozwolone?
4. Jakie są obowiązujące limity API KSeF (minutowe, godzinowe, na token czy na IP) - dokumentacja projektu podaje tylko godzinowe.
5. Czy dwie firmy w aplikacji z tym samym NIP są dopuszczalne (KSEF-10)?
6. Czy limit trzech przypomnień ma obejmować przypomnienia ręczne i nieudane wysyłki? Kod liczy wszystkie
   (`PaymentReminders.cs:67-70`), opis w klasie mówi o przypomnieniach automatycznych.
7. Wygenerowanie nowego tokenu testowego nie cofa poprzedniego w KSeF - czy to zamierzone?
