# Integracja z KSeF - wysyłka faktur

Aplikacja wysyła faktury **wyłącznie do środowiska testowego KSeF** (`api-test.ksef.mf.gov.pl`, decyzja D8).
Komunikację realizuje oficjalny klient MF (D10) opakowany interfejsem `IKsefGateway` (`FakturowanieKSeF.Ksef`).

## Uwierzytelnianie firmy

Każda firma uwierzytelnia się w KSeF własnym **tokenem KSeF**: ciągiem znaków nadającym uprawnienia
w kontekście NIP firmy.

| Sposób uzyskania tokenu | Kiedy | Jak |
|---|---|---|
| Wklejenie tokenu | docelowo (produkcja) i na teście | token generowany przez podatnika w Aplikacji Podatnika KSeF; aplikacja sprawdza go w KSeF przed zapisaniem |
| „Wygeneruj token testowy” | tylko środowisko testowe | aplikacja tworzy samopodpisany certyfikat dla NIP firmy (dopuszczalny wyłącznie na teście), loguje się nim (podpis XAdES) i generuje przez API token z uprawnieniami `InvoiceWrite` i `InvoiceRead` |

Przy każdej operacji aplikacja wymienia token KSeF na krótkotrwały **token dostępu (JWT)**:
- pobiera z KSeF wyzwanie (challenge),
- szyfruje token KSeF razem ze znacznikiem czasu kluczem publicznym MF (RSA-OAEP),
- odbiera token dostępu.

Token dostępu jest przechowywany w pamięci serwera do chwili wygaśnięcia. Kluczem bufora jest skrót tokenu, nie sam token.

### Ochrona tokenu (D23)

- Token jest szyfrowany przed zapisem do bazy przez **ASP.NET Core Data Protection** (AES-256-CBC + HMAC-SHA256,
  osobny cel kryptograficzny `FakturowanieKSeF.KsefToken.v1`).
- Odszyfrowanie odbywa się wyłącznie w pamięci serwera, w chwili komunikacji z KSeF.
- Token nigdy nie jest zwracany do przeglądarki. API podaje tylko informację, czy token istnieje, a test to sprawdza.
- Klucze Data Protection są przechowywane w bazie (tabela `DataProtectionKeys`) i rotowane automatycznie.
  Dzięki temu przetrwają restart i wdrożenie nowej wersji. Docelowo klucze można dodatkowo zaszyfrować
  certyfikatem lub kluczem z magazynu sekretów (`ProtectKeysWith...`), żeby kopia samej bazy nie wystarczała
  do odczytu tokenów.

## Wysyłka faktury

```
Użytkownik            API                         Usługa w tle                        KSeF (TEST)
    │ „Wyślij do KSeF” │                                │                                  │
    │─────────────────►│ generuje XML FA(3), walidacja XSD                                 │
    │                  │ zapisuje wysyłkę (XML + SHA-256)                                  │
    │                  │ status faktury: Pending        │                                  │
    │◄── 202 Accepted ─│── sygnał ─────────────────────►│                                  │
    │                  │                                │ [1] token → JWT, sesja, faktura ►│
    │                  │                                │◄────── numer referencyjny ───────│
    │                  │                                │ [2] status? ────────────────────►│
    │                  │                                │◄── 150 (w toku) / 200 + nr KSeF ─│
    │                  │                                │     zamknięcie sesji ───────────►│
    │                  │                                │ [3] UPO? ───────────────────────►│
    │                  │                                │◄────────── UPO (XML) ────────────│
    │ strona odświeża status co 3 s (Pending → Accepted, przycisk „Pobierz UPO”)           │
```

- **Endpoint** `POST /api/invoices/{id}/send` działa szybko i niczego nie wysyła do KSeF.
  Generuje i waliduje XML, zapisuje dokładnie ten dokument (`KsefSubmissions.SentXml`) wraz ze skrótem SHA-256,
  ustawia status `Pending` i budzi usługę w tle.
- W tej chwili utrwalane są aktualne dane sprzedawcy. Moment wysyłki to moment wystawienia faktury.
- **Usługa w tle** (`KsefSubmissionWorker` → `KsefSubmissionProcessor`) to maszyna stanów, której każdy krok
  jest zapisywany w bazie. Restart serwera w trakcie wysyłki niczego nie gubi.
- Przy statusie „w toku” usługa odpytuje KSeF co ok. 3 s, a bez pracy co ok. 15 s. Sygnał z endpointu
  budzi ją natychmiast.

### Stany i przejścia

| Krok | Warunek | Działanie | Wynik |
|---|---|---|---|
| [1] | brak numeru referencyjnego | uwierzytelnienie, sesja interaktywna (AES-256), wysłanie zaszyfrowanej faktury | numer sesji i faktury, kod 150 |
| [2] | kod 150 | odpytanie o status | 200: numer KSeF, faktura `Accepted`; kod błędu: szczegóły, faktura `Rejected`; w obu przypadkach zamknięcie sesji |
| [3] | przyjęta, brak UPO | pobranie UPO (KSeF tworzy je po zamknięciu sesji) | UPO zapisane; próby przez 7 dni |

### Obsługa błędów (D25)

| Sytuacja | Zachowanie |
|---|---|
| Błąd sieci lub niedostępność KSeF **przed** przyjęciem faktury do KSeF | ponawianie; po 10 próbach wysyłka przerwana, faktura `Rejected` z opisem; użytkownik może wysłać ponownie |
| Nieprawidłowy lub cofnięty token | wysyłka przerwana od razu, komunikat dla użytkownika |
| Błąd sieci **po** przyjęciu faktury do przetwarzania | **tylko ponawianie** odpytania o status, nigdy `Rejected` |
| KSeF odrzucił fakturę (np. błąd semantyki) | `Rejected` + kod i szczegóły błędów z KSeF; po poprawie ponowna wysyłka tworzy nową próbę, historia zostaje |
| Podwójne kliknięcie „Wyślij” lub dwie karty | kontrola wersji rekordu: tylko jedna wysyłka, druga kończy się 409 |

Zasada z trzeciego wiersza jest kluczowa dla poprawności. Faktura, która mogła już trafić do KSeF, nie może
zostać oznaczona jako odrzucona z powodu własnego błędu komunikacji. KSeF mógł ją przyjąć, a ponowna wysyłka
utworzyłaby duplikat dokumentu podatkowego.

## Dokumenty

- **XML faktury** (`GET /api/invoices/{id}/xml`): dla faktury wysłanej zwracany jest dokładnie wysłany dokument,
  bo to on ma moc prawną, a nie dokument wygenerowany ponownie.
- **UPO** (`GET /api/invoices/{id}/upo`): urzędowe poświadczenie odbioru podpisane przez MF.

## Testowanie

| Rodzaj | Co sprawdza | Gdzie |
|---|---|---|
| Testy z atrapą KSeF (`FakeKsefGateway`) | pełny przepływ, odrzucenie i ponowna wysyłka, awarie sieci przed i po wysyłce, podwójna wysyłka, szyfrowanie tokenu, izolacja danych | `KsefSendingTests`, `KsefSettingsTests` (CI) |
| Test na żywo | prawdziwa bramka na testowym KSeF: token testowy, wysyłka, numer KSeF, UPO | `KsefLiveTests` (uruchamiany z `KSEF_LIVE_TESTS=1`) |
| Test E2E w przeglądarce | rejestracja → firma → token testowy → faktura → wysyłka → numer KSeF → UPO (ok. 10 s od kliknięcia „Wyślij”) | ręcznie, Playwright |

Wynik testu E2E: numer KSeF `2423594858-20261101-51150D000000-BA`, UPO wystawione przez
„Ministerstwo Finansów - środowisko testowe (TE)”.

## Odbiór faktur (faktury otrzymane)

Faktury wystawione na NIP firmy przez innych podatników są pobierane z KSeF i zapisywane w lokalnej bazie
(`ReceivedInvoices`, `ReceivedInvoiceSync`). Mechanizm odpowiada zaleceniom MF dla niskiego wolumenu faktur
(dokumentacja KSeF 2.0: *Przyrostowe pobieranie faktur*, *High Water Mark*, *Limity API*).

**Synchronizacja przyrostowa „do HWM”:**
1. zapytanie `POST /invoices/query/metadata`:
   - faktury, w których firma jest nabywcą (Podmiot2),
   - typ daty `PermanentStorage` (data trwałego zapisu), od punktu kontrolnego firmy,
   - `restrictToPermanentStorageHwmDate = true`, sortowanie rosnąco;
2. dla każdej nowej faktury (deduplikacja po numerze KSeF, indeks unikalny w bazie): `GET /invoices/ksef/{numer}`
   i zapis XML z metadanymi; każda faktura zapisywana jest od razu, więc przerwanie niczego nie gubi;
3. nowy punkt kontrolny:
   - po pełnym przejściu: **HWM**. To moment, do którego KSeF gwarantuje kompletność, więc tego okresu nie trzeba
     sprawdzać ponownie i nie powstają duplikaty;
   - po przerwaniu (limit, błąd): data ostatniej przetworzonej faktury.

**Tryb mieszany:** cyklicznie co 15 minut dla wszystkich firm z tokenem (minimalny odstęp zalecany przez MF)
oraz ręcznie przyciskiem „Pobierz z KSeF”, nie częściej niż raz na minutę (odpowiedź 429).

**Limity:** jedna synchronizacja wykonuje maksymalnie 3 zapytania o metadane i 40 pobrań faktur.
Limity KSeF to 20 zapytań i 64 pobrania na godzinę. Pozostałe faktury są pobierane w kolejnym przebiegu.

**Odczyt:** listy, wyszukiwanie i podgląd korzystają wyłącznie z lokalnej bazy. MF wprost odradza wywołania KSeF
przy każdej akcji użytkownika. Treść faktury do podglądu odczytuje z XML `Fa3InvoiceReader`.
Czytnik szuka elementów po nazwach lokalnych, więc obsługuje FA(3) i FA(2), a przetwarzanie DTD jest wyłączone (XXE).
Datą otrzymania faktury jest data nadania numeru KSeF (`AcquisitionDate`).

**Weryfikacja na testowym KSeF:**
- test automatyczny na żywo i scenariusz w przeglądarce z dwiema firmami: firma A wysyła fakturę na NIP firmy B,
  a firma B ją pobiera i wyświetla;
- **obserwacja:** faktura stała się dostępna do pobrania ok. **2 minuty** po nadaniu numeru KSeF, mimo że HWM
  przesuwał się na bieżąco (KSeF potrzebuje chwili na trwały zapis). Potwierdza to zasadność synchronizacji
  cyklicznej zamiast oczekiwania na natychmiastową dostępność.

## Wizualizacja faktury (PDF) i kod QR weryfikacji

PDF (`InvoicePdfRenderer`, biblioteka QuestPDF) powstaje z **dokumentu XML w strukturze FA**, odczytanego przez
`Fa3InvoiceReader`. Ten sam renderer obsługuje faktury wystawione i otrzymane, a wydruk faktury wysłanej pokazuje
dokładnie treść dokumentu przekazanego do KSeF.

| Faktura | Źródło treści | Kod QR (KOD I) | Oznaczenie |
|---|---|---|---|
| Szkic / odrzucona | XML wygenerowany z danych | brak | ostrzeżenie „SZKIC” / „ODRZUCONA” na wydruku |
| Wysłana, bez numeru KSeF | wysłany XML | tak | pod kodem „OFFLINE” |
| Przyjęta przez KSeF | wysłany XML | tak | pod kodem numer KSeF |
| Otrzymana z KSeF | pobrany XML | tak | pod kodem numer KSeF |

**KOD I** (dokumentacja MF: *Kody weryfikujące QR*) to link
`https://qr-test.ksef.mf.gov.pl/invoice/{NIP sprzedawcy}/{data wystawienia DD-MM-RRRR}/{SHA-256 pliku faktury, Base64URL}`.
Po zeskanowaniu potwierdza obecność faktury w KSeF i pozwala pobrać jej XML. Jest wymagany, gdy faktura trafia
do odbiorcy poza KSeF (PDF, wydruk, e-mail). Obraz kodu tworzy biblioteka QRCoder (ISO/IEC 18004).

**Weryfikacja:**
- link jest identyczny z linkiem budowanym przez oficjalny klient MF (test automatyczny),
- skrót SHA-256 zapisany przy wysyłce jest identyczny ze skrótem `SkrotDokumentu` w UPO wystawionym przez KSeF
  (sprawdzone dla faktur przyjętych na środowisku testowym),
- treść PDF (numer, kwoty, polskie znaki, oznaczenie pod kodem QR) jest sprawdzana w testach przez odczyt
  tekstu z pliku (PdfPig).

Przykład: [`przyklady/faktura-przyjeta-w-ksef.pdf`](przyklady/faktura-przyjeta-w-ksef.pdf).

## Uwaga o danych testowych

Środowisko testowe KSeF jest **współdzielone przez integratorów** i okresowo czyszczone (dokumentacja MF).
Aplikacja ostrzega w ustawieniach KSeF, żeby używać wyłącznie danych fikcyjnych (np. losowego NIP).
