# Raport: Obszar 03 - integracja z KSeF i usługi w tle
Status: W TOKU
Ostatnia aktualizacja: 2026-11-11 (start)

## Postęp
- [x] dokumentacja: `docs/integracja-ksef.md`, decyzje D8, D10, D11, D24-D27, D44, D51
- [x] `FakturowanieKSeF.Ksef/KsefGateway.cs` (+ bufor tokenów dostępu)
- [x] `KsefTestOnlyHandler.cs`, `KsefEnvironment.cs`, `KsefQr.cs`, `KsefServiceCollectionExtensions.cs`
- [x] `Services/KsefSubmissionProcessor.cs`, `KsefSubmissionWorker.cs` (maszyna stanów, równoległość, utknięcia, backoff)
- [x] `Endpoints/InvoiceEndpoints.cs` (`/send`), `KsefSettingsEndpoints.cs`
- [x] `Services/ReceivedInvoiceSync.cs` + `Endpoints/ReceivedInvoiceEndpoints.cs` (HWM, deduplikacja, budżet, raz/min)
- [ ] `Services/IssuedInvoiceImport.cs`
- [ ] `Services/RecurringInvoices.cs`
- [ ] `Services/Email/EmailOutbox.cs`, `PaymentReminders.cs`, `ReceivedInvoiceNotifier.cs`
- [ ] testy: `KsefSendingTests.cs`, `ReceivedInvoicesTests.cs`, `IssuedImportTests.cs`, `FakeKsefGateway.cs` (luki pokrycia)
- [ ] podsumowanie i sekcje końcowe
Następny krok: dokończyć `IssuedInvoiceImport.cs` (przeczytany, znaleziska poniżej), potem `RecurringInvoices.cs`, e-mail, testy; na końcu rozwinąć notatki robocze w pełne znaleziska.

## Znaleziska
(dopisywane na bieżąco; sortowanie wg wagi na końcu)

### Notatki robocze (do rozwinięcia w pełne znaleziska - wszystkie wynikają z lektury kodu)
- N1 (krytyczne): `KsefSubmissionProcessor.cs:77-86` + `KsefGateway.cs:148-177` - wyjątek/timeout po faktycznym przyjęciu faktury przez KSeF
  (odpowiedź na `SendOnlineSessionInvoiceAsync` nie dotarła albo padł `SaveChanges`/proces przed zapisem numeru referencyjnego) →
  `InvoiceReferenceNumber == null` → ponowna wysyłka (duplikat / status 440) albo po 10 próbach `Rejected` mimo przyjęcia w KSeF.
  `IssuedInvoiceImport.KnownAsync` (skrót SHA-256) dodatkowo ukrywa taką fakturę przed importem.
- N2: `KsefGateway.cs:279-282` - każdy `KsefApiException` przy uwierzytelnianiu (także 429 `KsefRateLimitException`, 5xx) → `KsefAuthenticationException`
  → wysyłka od razu `Rejected` „KSeF odrzucił token” (brak ponawiania), PUT /token zwraca błąd walidacji tokenu.
- N3: brak backoffu: 10 prób co 3 s = ok. 30 s do `Rejected`; po przyjęciu odpytywanie statusu/UPO co 3 s bez ograniczeń (UPO do 7 dni);
  `CloseSession` próbowany tylko raz.
- N4: `ProcessAsync` `.OrderBy(Id).Take(50)` - 50 najstarszych otwartych (zablokowanych lub czekających na UPO) głodzi nowsze wysyłki.
- N5: Pending na zawsze po przyjęciu (token usunięty/odrzucony, sesja usunięta w czyszczonym środowisku testowym) - brak stanu końcowego i wyjścia dla użytkownika;
  odrzucony token = 60 s odpytywania klienta MF na każdy przebieg (blokada kolejki wszystkich firm).
- N6: brak blokady/kontroli wersji na `KsefSubmission` - dwie instancje (rolling deploy) wyślą tę samą fakturę dwa razy.
- N7: `ReceivedInvoiceSync.cs:95,125` / `IssuedInvoiceImport.cs:79,111` - po `DbUpdateException` (wyścig cykliczna+ręczna, unikalny indeks) encja zostaje `Added`,
  końcowy `SaveChanges` rzuca ponownie → 500 / przerwanie pętli po wszystkich firmach, `LastSyncAt` niezapisany → ponowienie co minutę.
- N8: limit „raz na minutę” nieatomowy (`ReceivedInvoiceEndpoints.cs:193`, `InvoiceEndpoints.cs:289`) - `LastSyncAt` zapisywany dopiero po zapytaniach do KSeF.
- N9: budżet: 4 cykle/h × (3+3 zapytań metadanych) = 24 > 20/h; pobrania 40+40 na cykl vs 64/h (i limit minutowy) - do sprawdzenia z limitami MF.
- N10: globalnie unikalny indeks `Invoices.KsefNumber` + brak unikalności NIP firm → import D44 drugiej firmy z tym samym NIP zablokowany na stałe.
- N11: trucizna (poison) - stały błąd jednej faktury (np. wyjątek czytnika inny niż Format/Xml, null w metadanych) zatrzymuje punkt kontrolny na zawsze.
- N12: `IsAccepted` wymaga numeru KSeF - kod 200 bez numeru → `Rejected` (do weryfikacji).
