# Generowanie XML faktury w strukturze FA(3)

Faktura przesyłana do KSeF to dokument XML zgodny ze strukturą logiczną **FA(3)**
(`schemat_FA(3)_v1-0E.xsd`, przestrzeń nazw `http://crd.gov.pl/wzor/2025/06/25/13775/`).
Aplikacja generuje go z danych faktury (`Fa3XmlGenerator`) i przed wydaniem zawsze sprawdza
względem oficjalnego schematu XSD (`Fa3SchemaValidator`). Kod: `FakturowanieKSeF.Ksef/Fa3/`.

## Weryfikacja w KSeF

Faktura wygenerowana przez aplikację została wysłana do **testowego** środowiska KSeF (spike w trybie `--generated`)
i przyjęta: numer KSeF `3414695388-20261101-4ED30D000000-E4`, UPO podpisane przez
„Ministerstwo Finansów - środowisko testowe (TE)”. Faktura zawierała stawki 23%, 5% i „zw” z podstawą prawną.
Pliki: [`spike-ksef/faktura-z-generatora.xml`](spike-ksef/faktura-z-generatora.xml),
[`spike-ksef/upo-faktura-z-generatora.xml`](spike-ksef/upo-faktura-z-generatora.xml).

## Mapowanie danych aplikacji na pola FA(3)

| Element FA(3) | Znaczenie | Źródło w aplikacji |
|---|---|---|
| `Naglowek/KodFormularza` | `FA`, atrybuty `kodSystemowy="FA (3)"`, `wersjaSchemy="1-0E"` | stała |
| `Naglowek/WariantFormularza` | `3` | stała |
| `Naglowek/DataWytworzeniaFa` | moment wygenerowania XML (UTC) | zegar systemowy |
| `Naglowek/SystemInfo` | nazwa systemu wystawiającego | stała |
| `Podmiot1/DaneIdentyfikacyjne/NIP`, `Nazwa` | sprzedawca | `Invoice.Seller` (kopia danych firmy) |
| `Podmiot1/Adres` | `KodKraju`, `AdresL1` = ulica, `AdresL2` = kod i miejscowość | `Invoice.Seller.Address` |
| `Podmiot2/DaneIdentyfikacyjne/NIP` albo `BrakID=1` | nabywca - firma (NIP) albo osoba prywatna | `Invoice.Buyer.Nip` |
| `Podmiot2/Adres` | adres nabywcy | `Invoice.Buyer.Address` |
| `Podmiot2/JST`, `GV` | `2` - nie dotyczy jednostki JST ani grupy VAT | stała |
| `Fa/KodWaluty` | `PLN` lub waluta obca (EUR, USD…) | `Invoice.Currency` |
| `Fa/P_14_1W`, `P_14_2W`, `P_14_3W` | waluta obca: podatek w PLN (podatek × kurs, do grosza) | `InvoiceCalculator.VatInPln` (D43) |
| `Fa/FaWiersz/KursWaluty` | waluta obca: kurs średni NBP (do 6 miejsc) | `Invoice.ExchangeRate` |
| `Fa/P_1`, `P_1M` | data i miejsce wystawienia | `IssueDate`, `IssuePlace` |
| `Fa/P_2` | numer faktury | `Number` |
| `Fa/P_6` | data sprzedaży - tylko gdy różna od daty wystawienia | `SaleDate` |
| `Fa/P_13_1` + `P_14_1` | netto i VAT - stawka podstawowa (23%, 22%) | podsumowanie z `InvoiceCalculator` |
| `Fa/P_13_2` + `P_14_2` | stawka obniżona pierwsza (8%, 7%) | j.w. |
| `Fa/P_13_3` + `P_14_3` | stawka obniżona druga (5%) | j.w. |
| `Fa/P_13_6_1` / `6_2` / `6_3` | 0% - kraj / WDT / eksport | j.w. |
| `Fa/P_13_7` | sprzedaż zwolniona (zw) | j.w. |
| `Fa/P_13_8` / `P_13_9` | niepodlegające opodatkowaniu (np I / np II) | j.w. |
| `Fa/P_13_10` | odwrotne obciążenie (oo) | j.w. |
| `Fa/P_15` | kwota należności ogółem (brutto); na korekcie różnica (może być ujemna) | `TotalGross` |
| `Fa/Adnotacje/P_16`, `P_17`, `P_23` | metoda kasowa, samofakturowanie, procedura uproszczona - `2` (nie) | stała |
| `Fa/Adnotacje/P_18` | `1` gdy występuje odwrotne obciążenie, inaczej `2` | pozycje faktury |
| `Fa/Adnotacje/P_18A` | mechanizm podzielonej płatności - `2` | ograniczenie MVP |
| `Fa/Adnotacje/Zwolnienie` | `P_19=1` + `P_19A` (podstawa prawna) albo `P_19N=1` | `ExemptionBasis`, pozycje „zw” |
| `Fa/Adnotacje/NoweSrodkiTransportu`, `PMarzy` | `P_22N=1`, `P_PMarzyN=1` (nie dotyczy) | stała |
| `Fa/RodzajFaktury` | `VAT` - faktura podstawowa, `KOR` - faktura korygująca | `Kind` |
| `Fa/PrzyczynaKorekty` | przyczyna korekty (KOR) | `CorrectionReason` |
| `Fa/TypKorekty` | skutek w ewidencji VAT: 1 - data faktury pierwotnej, 2 - data korekty, 3 - inna (opcjonalnie) | `CorrectionType` |
| `Fa/DaneFaKorygowanej` | `DataWystFaKorygowanej`, `NrFaKorygowanej`, `NrKSeF=1`, `NrKSeFFaKorygowanej` | kopia danych faktury korygowanej |
| `Fa/FaWiersz/StanPrzed` | `1` - wiersz stanu przed korektą (metoda „przed i po”, osobna numeracja wierszy) | `InvoiceItem.BeforeCorrection` |
| `Fa/FaWiersz` | `NrWierszaFa`, `P_7` nazwa, `P_8A` j.m., `P_8B` ilość, `P_9A` cena netto, `P_11` wartość netto, `P_12` stawka | `Invoice.Items` |
| `Fa/Platnosc/Zaplacono`, `DataZaplaty` | `1` i data - faktura zapłacona w chwili wystawienia (D33) | `PaidOn` |
| `Fa/Platnosc/TerminPlatnosci/Termin` | termin płatności | `DueDate` |
| `Fa/Platnosc/FormaPlatnosci` | 1-7 (np. 6 = przelew) | `PaymentMethod` |
| `Fa/Platnosc/RachunekBankowy/NrRB` | numer rachunku | `BankAccountNumber` |

Pary P_13_x / P_14_x występują tylko dla stawek użytych na fakturze. Kwoty liczone są tym samym kalkulatorem
co w API i formularzu (VAT od sumy netto w stawce, D16), więc kwoty w XML są identyczne z prezentowanymi
użytkownikowi.

## Faktura korygująca (KOR)

Korekta jest zapisywana metodą „przed i po”: w `FaWiersz` najpierw wiersze stanu przed korektą ze znacznikiem
`StanPrzed=1`, potem wiersze stanu po korekcie (numeracja wierszy ciągła). Pola `P_13_x`, `P_14_x` i `P_15` zawierają
różnicę po - przed, liczoną per stawka tym samym kalkulatorem co faktura (ujemną przy obniżce). Stawka obecna
w obu stanach bez zmiany kwot daje różnicę `0.00`. Element `Platnosc` jest wykazywany tylko przy dopłacie
(różnica dodatnia). Faktura korygowana jest wskazywana numerem KSeF (`NrKSeF=1`). Korekta wygenerowana przez
aplikację została przyjęta przez testowy KSeF (test `KsefLiveTests.FakturaKorygujaca_JestPrzyjmowanaPrzezTestowyKsef`).

## Formatowanie i wymagania techniczne

- Kodowanie **UTF-8 bez znacznika BOM**, deklaracja XML na początku, brak innych instrukcji przetwarzania
  (wymagania KSeF sprawdzane także przez klienta MF).
- Liczby z kropką dziesiętną, niezależnie od ustawień regionalnych serwera: kwoty z 2 miejscami
  po przecinku (TKwotowy), ilość do 6 (TIlosci), cena do 8 (TKwotowy2).
- Daty w formacie `RRRR-MM-DD`, znacznik czasu w UTC (`...Z`).
- Znaki specjalne (`&`, `<`, cudzysłowy) i polskie litery kodowane przez `XmlWriter`. Generator nie składa
  XML przez łączenie tekstów, co wyklucza wstrzyknięcie znaczników przez dane użytkownika.

## Walidacja względem schematu

- Schemat FA(3) i schematy bazowe MF (`StrukturyDanych`, `ElementarneTypyDanych`, `KodyKrajow`)
  są **osadzone w aplikacji**. Źródło: CIRFMF/ksef-docs, `faktury/schemy/FA`.
- Odwołania do crd.gov.pl są przekierowywane na kopie lokalne, a każde inne odwołanie zewnętrzne jest blokowane.
  Walidacja nie wymaga internetu, a schemat nie może pobrać żadnego zasobu z sieci ani z dysku.
- Przetwarzanie DTD jest wyłączone, co chroni przed atakami XXE.
- Zbiór schematów jest kompilowany raz i współdzielony.
- Poprawność walidatora potwierdza test na przykładowej fakturze z repozytorium MF.

## Zakres i ograniczenia (MVP)

| Ograniczenie | Powód |
|---|---|
| Tylko waluta PLN | Waluta obca wymaga kwot VAT w PLN (`P_14_xW`) i kursu waluty |
| Bez stawek 4% i 3% | W FA(3) dotyczą wyłącznie ryczałtu dla taksówek osobowych (inny sposób rozliczenia) |
| `P_18A = 2` (bez MPP) | Mechanizm podzielonej płatności dotyczy towarów z zał. 15 powyżej 15 000 zł - poza zakresem MVP |
| Tylko faktura VAT podstawowa | Korekty (KOR) i zaliczki (ZAL) po MVP (D5) |
| Nabywca z polskim NIP lub osoba prywatna | Kontrahenci zagraniczni (VAT UE, inny identyfikator) - poza MVP |

Ograniczenia są egzekwowane na trzech poziomach: walidacja formularza i API (komunikat dla użytkownika),
generator (wyjątek `Fa3GenerationException`) oraz interfejs (np. lista stawek bez 4% i 3%).

## API

`GET /api/invoices/{id}/xml` zwraca plik XML (`application/xml`) zweryfikowany względem schematu. Odpowiedź 422
oznacza fakturę, której nie da się przedstawić w FA(3). W interfejsie: przycisk „Pobierz XML (FA(3))”
w podglądzie faktury.
