# Funkcje dodatkowe (rozwój po MVP)

Po zrealizowaniu MVP przeanalizowano funkcje popularnych programów do fakturowania z obsługą KSeF dla małych firm
(inFakt, Fakturownia, wFirma, iFirma, Fakturowo, Comarch ERP Optima). Na tej podstawie powstała lista funkcji
dodatkowych z priorytetami. Każda funkcja jest realizowana na osobnej gałęzi i scalana osobnym pull requestem.

## Analiza rozwiązań konkurencyjnych

Funkcje występujące u większości konkurentów, a nieobecne w MVP:
- uzupełnianie danych kontrahenta po NIP (wykaz podatników VAT lub REGON),
- weryfikacja rachunku kontrahenta na białej liście (KSeF tego nie sprawdza),
- status płatności faktur (opłacona / nieopłacona / po terminie),
- kopiowanie faktury („wystaw podobną”),
- faktury korygujące,
- wysyłka faktury e-mailem, przypomnienia o płatności, faktury cykliczne, płatności online,
- aplikacja mobilna, powiadomienia o nowych fakturach kosztowych.

**Obserwacja:** konkurenci oferują rozpoznawanie faktur kosztowych ze zdjęć (OCR). Przy KSeF faktury krajowe
przychodzą jako ustrukturyzowany XML, więc OCR traci znaczenie - aplikacja odczytuje dane wprost ze struktury FA(3).

## Lista funkcji i stan realizacji

| # | Funkcja | Priorytet | Stan |
|---|---|---|---|
| 1 | Dane kontrahenta z wykazu podatników VAT po NIP (D28) | wysoki | ✅ |
| 2 | Weryfikacja rachunku na białej liście (faktury otrzymane) (D29) | wysoki | ✅ |
| 3 | Status płatności faktur wystawionych i otrzymanych (D30) | wysoki | ✅ |
| 4 | „Wystaw podobną” - kopia faktury jako nowy szkic (D31) | wysoki | ✅ |
| 5 | Faktury korygujące | wysoki | planowana |
| 6 | Wysyłka faktury e-mailem | średni | do decyzji |
| 7 | Faktury cykliczne | średni | do decyzji |
| 8 | Przypomnienia o płatności | średni | do decyzji |
| 9 | Powiadomienie o nowej fakturze kosztowej | średni | do decyzji |
| 10 | Pulpit z wykresami | średni | do decyzji |
| 11 | Katalog produktów i usług | niski | do decyzji |
| 12 | Waluty obce z kursem NBP | niski | do decyzji |
| 13 | Eksport dla księgowej (CSV, paczka XML + UPO) | niski | do decyzji |
| 14 | Aplikacja mobilna (PWA) | niski | do decyzji |
| 15 | Logowanie dwuskładnikowe | niski | do decyzji |
| 16 | Faktura proforma | niski | do decyzji |
| 17 | Tryb offline KSeF (kod QR II) | badawczy | do decyzji |
| 18 | Dostęp księgowej do firmy (role) | badawczy | do decyzji |
| 19 | Import faktur sprzedażowych wystawionych poza aplikacją | badawczy | do decyzji |

## 1. Dane kontrahenta z wykazu podatników VAT

Przycisk **Pobierz z wykazu VAT** przy polu NIP (kontrahent, nabywca na fakturze, dane firmy) uzupełnia nazwę
i adres na podstawie wykazu podatników VAT MF. Szczegóły i uzasadnienie: decyzja D28.

```mermaid
sequenceDiagram
    participant U as Przeglądarka
    participant A as API (VatRegistryLookup)
    participant C as Pamięć podręczna
    participant MF as wl-api.mf.gov.pl
    U->>A: GET /api/vat-registry/{nip}
    A->>C: wynik na dziś?
    alt w pamięci podręcznej
        C-->>A: dane
    else brak
        A->>A: limit dzienny (aplikacja, użytkownik)
        A->>MF: GET /api/search/nip/{nip}?date=dziś
        MF-->>A: podmiot / brak
        A->>C: zapisz do końca dnia
    end
    A-->>U: nazwa, adres (pola), status VAT, rachunki
```

## 2. Weryfikacja rachunku na białej liście

KSeF nie sprawdza, czy rachunek na fakturze należy do sprzedawcy. Zapłata powyżej 15 000 zł na rachunek spoza
wykazu podatników VAT pozbawia nabywcę prawa do zaliczenia wydatku do kosztów (art. 15d ustawy o CIT, art. 22p
ustawy o PIT) i grozi solidarną odpowiedzialnością za VAT sprzedawcy (art. 117ba Ordynacji podatkowej).
W podglądzie faktury otrzymanej przycisk **Sprawdź rachunek na białej liście** wywołuje metodę `check` API MF
dla NIP sprzedawcy i rachunku z XML faktury. Wynik z datą i identyfikatorem zapytania MF jest zapisywany przy
fakturze (potwierdzenie sprawdzenia), a faktury z rachunkiem spoza wykazu są oznaczone na liście. Szczegóły: D29.

## 3. Status płatności (rozliczenia)

Użytkownik rejestruje wpłaty za faktury wystawione i zapłaty za faktury otrzymane (także częściowe). Stan
rozliczenia (*nieopłacona*, *częściowo opłacona*, *opłacona*, *po terminie*) jest wyliczany z sumy płatności
i terminu z faktury. Listy faktur mają filtr rozliczenia, a pulpit pokazuje należności i zobowiązania, w tym kwoty
po terminie. Szczegóły: D30.

## 4. Wystaw podobną

Przycisk w podglądzie faktury otwiera formularz nowej faktury (`/faktury/nowa?wzor={id}`) wypełniony nabywcą,
pozycjami i warunkami płatności wzoru. Numer i data wystawienia są nowe, termin płatności zachowuje odstęp dni.
Jeśli kontrahent wzoru nadal istnieje, faktura wskazuje go w książce adresowej (aktualne dane), w przeciwnym razie
kopiowane są dane nabywcy z wzoru. Szczegóły: D31.

Źródła analizy: strony producentów i porównania z 2026 r. (iFirma - przegląd programów do obsługi KSeF,
Bezprawnik - ranking programów do faktur z KSeF, strony funkcji Fakturowni, inFaktu i wFirmy).
