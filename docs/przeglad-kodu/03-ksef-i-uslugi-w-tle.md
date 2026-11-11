# Obszar 03 - integracja z KSeF i usługi w tle

Raport zapisz w `docs/przeglad-kodu/wyniki/03-ksef-i-uslugi-w-tle.md`. ID znalezisk: `KSEF-1`, `KSEF-2`, …
Najpierw przeczytaj `00-zasady.md`, potem `docs/integracja-ksef.md` i decyzje D8, D10, D11, D24-D27, D44, D51.

## Zakres plików

- `FakturowanieKSeF.Ksef/KsefGateway.cs`, `KsefTestOnlyHandler.cs`, `KsefEnvironment.cs`, `KsefQr.cs`,
  `KsefServiceCollectionExtensions.cs`
- `FakturowanieKSeF.Api/Services/KsefSubmissionProcessor.cs`, `KsefSubmissionWorker.cs`, `ReceivedInvoiceSync.cs`,
  `IssuedInvoiceImport.cs`, `RecurringInvoices.cs`, `Email/EmailOutbox.cs`, `Email/PaymentReminders.cs`,
  `Email/ReceivedInvoiceNotifier.cs`
- `FakturowanieKSeF.Api/Endpoints/InvoiceEndpoints.cs` (wysyłka `/send`), `KsefSettingsEndpoints.cs`,
  `ReceivedInvoiceEndpoints.cs` (synchronizacja ręczna)
- testy: `KsefSendingTests.cs`, `ReceivedInvoicesTests.cs`, `IssuedImportTests.cs`, `FakeKsefGateway.cs`
- klient MF w `external/ksef-client-csharp` - tylko do zrozumienia zachowania, bez oceniania jego kodu

## Pytania kontrolne

**Wysyłka (najważniejsze: brak duplikatów w KSeF i brak „zgubionych” faktur)**
- Prześledź każdy stan `KsefSubmission` i każde przejście. Czy istnieje ścieżka, w której faktura przyjęta przez
  KSeF zostaje oznaczona `Rejected` albo wysłana drugi raz (restart w trakcie, wyjątek po `SendInvoiceAsync`,
  przed zapisem `InvoiceReferenceNumber`, timeout HTTP po stronie klienta przy faktycznym przyjęciu)?
- Równoległość: dwie instancje usługi w tle, dwa przebiegi pętli, wysyłka ręczna w trakcie przebiegu - czy jedna
  wysyłka nie zostanie przetworzona dwa razy? Czy kontrola wersji (`Version`) wystarcza?
- Czy faktura w stanie `Pending` może utknąć na zawsze (np. brak tokenu po jego usunięciu, sesja KSeF wygasła,
  UPO nigdy nie przyjdzie)? Co widzi użytkownik?
- Rozpoznawanie błędów: czy błędy trwałe (4xx, odrzucony token - D51) są odróżnione od przejściowych (sieć, 5xx, 429)?
  Czy ponawianie ma odstęp (backoff), czy uderza w KSeF przy każdym przebiegu?
- Bufor tokenów dostępu (`IMemoryCache`): klucz, czas życia, unieważnienie po zmianie lub usunięciu tokenu KSeF.

**Odbiór faktur „do HWM”**
- Czy punkt kontrolny nigdy nie przeskoczy faktury (sortowanie, strony, `IsTruncated`, przerwanie w połowie strony,
  limit pobrań, wyjątek w trakcie)? Czy granica `from` jest włączna/wyłączna zgodnie z API?
- Deduplikacja po numerze KSeF: wyścig dwóch synchronizacji tej samej firmy (cykliczna + ręczna), unikalny indeks.
- Budżet zapytań i limity API KSeF: czy przy wielu firmach usługa w tle nie przekroczy limitów (zapytania na
  token / na IP)? Czy synchronizacja ręczna „raz na minutę” jest egzekwowana atomowo?
- Import faktur wystawionych poza aplikacją: dopasowanie do faktur już istniejących, faktury korygujące, waluty.

**Pozostałe usługi w tle**
- Faktury cykliczne: duplikaty przy restarcie, strefa czasowa (Europe/Warsaw vs UTC), koniec miesiąca, harmonogram
  po usunięciu wzoru lub kontrahenta.
- Kolejka e-mail i przypomnienia: wielokrotne wysłanie tej samej wiadomości, limity dzienne, ponawianie po błędzie SMTP.
- Ogólnie dla usług w tle: obsługa `CancellationToken` przy zatrzymaniu, wyjątki zabijające pętlę, zakresy DI
  (`DbContext` żyjący zbyt długo), pamięć rosnąca z czasem, zapytania po wszystkich firmach bez stronicowania.

**Wydajność i uproszczenia w tym obszarze**
- Zbędne zapytania do KSeF (np. uwierzytelnianie przy każdym odpytaniu), sekwencyjne operacje, które mogłyby być
  zgrupowane, powtórzony kod synchronizacji odbioru i importu (`ReceivedInvoiceSync` vs `IssuedInvoiceImport`).
