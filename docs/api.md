# API aplikacji

REST API pod prefiksem `/api`, format JSON. Interaktywna dokumentacja (Swagger) w środowisku
deweloperskim: `/swagger`. Błędy zwracane są w formacie ProblemDetails (RFC 9457), z komunikatami po polsku.

## Uwierzytelnianie

ASP.NET Core Identity (`/api/auth/*`): `register`, `login`, `refresh`, `manage/info`, `logout`.

- **Przeglądarka (aplikacja Blazor):** `POST /api/auth/login?useCookies=true` ustawia ciasteczko sesji `ksef.auth`
  (HttpOnly, SameSite=Strict, ważne 8 h z przedłużaniem przy aktywności); `POST /api/auth/logout` je usuwa.
- **Inni klienci (testy, ewentualna aplikacja mobilna):** `POST /api/auth/login` zwraca token Bearer
  i token odświeżania; nagłówek `Authorization: Bearer <token>`.

Bez ważnej sesji lub tokenu endpointy biznesowe zwracają **401**. Polityka haseł: min. 8 znaków, wielka i mała
litera, cyfra, znak specjalny. Po 5 nieudanych logowaniach konto jest blokowane na 15 minut. Komunikaty błędów
po polsku.

## Endpointy

| Metoda | Adres | Opis |
|---|---|---|
| GET | `/api/company` | Dane firmy zalogowanego użytkownika (404, jeśli nie utworzono) |
| PUT | `/api/company` | Utworzenie (201) lub aktualizacja (200) danych firmy |
| GET | `/api/products?q=` | Katalog towarów i usług firmy, wyszukiwanie po nazwie (D35) |
| GET | `/api/products/{id}` | Pozycja katalogu |
| POST | `/api/products` | Dodanie pozycji (`name`, `unit`, `unitNetPrice`, `vatRate`); 409 - nazwa już jest w katalogu |
| PUT | `/api/products/{id}` | Edycja pozycji (nie zmienia wystawionych faktur) |
| DELETE | `/api/products/{id}` | Usunięcie pozycji (miękkie) |
| GET | `/api/customers?q=` | Lista kontrahentów, wyszukiwanie po nazwie lub NIP |
| GET | `/api/customers/{id}` | Szczegóły kontrahenta |
| POST | `/api/customers` | Dodanie kontrahenta |
| PUT | `/api/customers/{id}` | Edycja kontrahenta |
| DELETE | `/api/customers/{id}` | Usunięcie kontrahenta (miękkie) |
| GET | `/api/invoices?status=&payment=&from=&to=&q=&page=&pageSize=` | Lista faktur z filtrami i stronicowaniem; `payment`: `Unpaid`, `Paid`, `Overdue` (D30) |
| GET | `/api/invoices/{id}` | Szczegóły faktury z pozycjami i podsumowaniem VAT |
| GET | `/api/invoices/next-number?issueDate=&kind=` | Propozycja kolejnego numeru (`kind=Correction` - seria `KOR/n/MM/RRRR`, `kind=Proforma` - `PRO/n/MM/RRRR`) |
| POST | `/api/invoices/import-from-ksef` | Pobranie z KSeF faktur sprzedażowych wystawionych poza aplikacją (firma jako sprzedawca); zwraca `downloaded`, `skipped`, `complete`. 409 - brak tokenu, 429 - częściej niż raz na minutę, 502 - błąd KSeF (D44) |
| POST | `/api/invoices/{id}/convert` | Szkic faktury VAT na podstawie proformy (nabywca, pozycje, warunki); 409 - dokument nie jest proformą albo już wystawiono z niej fakturę (D42) |
| GET | `/api/invoices/{id}/pdf` | Wizualizacja faktury w PDF (z kodem QR weryfikacji KSeF dla faktur wysłanych) |
| GET | `/api/invoices/{id}/xml` | XML faktury w strukturze FA(3), zweryfikowany względem schematu XSD (422, gdy faktury nie da się przedstawić w FA(3)) - zob. `docs/fa3-xml.md` |
| POST | `/api/invoices` | Utworzenie faktury (szkicu). Z `correctedInvoiceId`, `correctionReason`, `correctionType` - faktura korygująca (KOR) do faktury przyjętej w KSeF; `items` to stan po korekcie (pusta lista - korekta do zera) (D32). `currency`: PLN lub waluta z tabeli A NBP - przy zapisie serwer pobiera kurs NBP (400, gdy NBP niedostępny) (D43). Pole `isProforma: true` - faktura proforma (nie trafia do KSeF, D42). Opcjonalne `paidOn` - faktura zapłacona przy wystawieniu (`Zaplacono` w FA(3); nie później niż data wystawienia), przy wysyłce rejestrowana jako płatność (D33) |
| PUT | `/api/invoices/{id}` | Edycja faktury (tylko szkic lub faktura odrzucona) |
| DELETE | `/api/invoices/{id}` | Usunięcie faktury (tylko szkic lub faktura odrzucona); 409 - faktura jest wzorem faktury cyklicznej (D39) |
| POST | `/api/invoices/{id}/send` | Wysłanie faktury do KSeF (body: `version`); 202 i status `Pending`, dalej asynchronicznie - zob. `docs/integracja-ksef.md` |
| GET | `/api/invoices/{id}/upo` | UPO faktury przyjętej przez KSeF (404, jeśli jeszcze niedostępne) |
| GET | `/api/company/ksef` | Stan integracji z KSeF (czy token jest skonfigurowany; sam token nigdy nie jest zwracany) |
| PUT | `/api/company/ksef/token` | Zapis tokenu KSeF po weryfikacji w KSeF (400, jeśli KSeF go odrzuci) |
| POST | `/api/company/ksef/test-token` | Wygenerowanie tokenu w testowym KSeF dla NIP firmy (502 przy błędzie KSeF) |
| DELETE | `/api/company/ksef/token` | Usunięcie tokenu |
| GET | `/api/received-invoices?q=&payment=&from=&to=&page=&pageSize=` | Lista faktur otrzymanych (z lokalnej bazy), filtr rozliczenia jak wyżej |
| GET | `/api/received-invoices/{id}` | Faktura otrzymana z treścią odczytaną z XML |
| GET | `/api/received-invoices/{id}/xml` | XML faktury otrzymanej (w postaci pobranej z KSeF) |
| GET | `/api/received-invoices/{id}/pdf` | Wizualizacja faktury otrzymanej w PDF z kodem QR weryfikacji |
| POST | `/api/received-invoices/{id}/bank-account-check` | Sprawdzenie rachunku z faktury w wykazie podatników VAT na dziś; wynik zapisany przy fakturze (409 - brak rachunku lub rachunek zagraniczny, 429 - limit, 502 - błąd MF) (D29) |
| GET | `/api/received-invoices/sync-status` | Stan synchronizacji (ostatnia synchronizacja, punkt kontrolny, błąd) |
| POST | `/api/received-invoices/sync` | Ręczna synchronizacja z KSeF (409 bez tokenu, 429 częściej niż raz na minutę, 502 przy błędzie KSeF) |
| GET | `/api/vat-registry/{nip}` | Dane podmiotu z wykazu podatników VAT (Biała lista MF): nazwa, adres rozbity na pola, status VAT, rachunki. 404 - brak w wykazie, 429 - dzienny limit zapytań, 502 - błąd API MF (D28) |
| POST | `/api/invoices/{id}/payments` | Rejestracja wpłaty (`paidOn`, `amount`, `note`); zwraca rozliczenie. 400 - nadpłata, data z przyszłości; 409 - szkic, faktura odrzucona lub opłacona (D30) |
| DELETE | `/api/invoices/{id}/payments/{paymentId}` | Usunięcie błędnej wpłaty; zwraca rozliczenie. 409 - zapłata oznaczona w fakturze w KSeF (`fromInvoice`, D33) |
| POST | `/api/received-invoices/{id}/payments` | Rejestracja zapłaty za fakturę otrzymaną (jak wyżej) |
| DELETE | `/api/received-invoices/{id}/payments/{paymentId}` | Usunięcie błędnej zapłaty |
| POST | `/api/auth/login` (z 2FA) | Przy włączonym 2FA samo hasło zwraca 401 z `detail: RequiresTwoFactor`; logowanie z polem `twoFactorCode` lub `twoFactorRecoveryCode`. Wbudowany `/api/auth/manage/2fa` jest wyłączony (404) |
| GET | `/api/account/2fa` | Stan logowania dwuskładnikowego: włączone, liczba pozostałych kodów odzyskiwania (D41) |
| POST | `/api/account/2fa/setup` | Nowy klucz aplikacji uwierzytelniającej: klucz, URI `otpauth://`, kod QR (PNG, Base64); 409 - 2FA już włączone |
| POST | `/api/account/2fa/enable` | Włączenie po potwierdzeniu kodem (`code`); zwraca 10 kodów odzyskiwania; 400 - zły kod |
| POST | `/api/account/2fa/disable` | Wyłączenie - wymaga kodu z aplikacji lub kodu odzyskiwania |
| POST | `/api/account/2fa/recovery-codes` | Nowe kody odzyskiwania - wymaga kodu z aplikacji |
| GET | `/api/exchange-rates/{currency}?date=` | Kurs średni NBP z ostatniego dnia roboczego przed datą (dniem sprzedaży) - podgląd w formularzu; 502 - API NBP niedostępne (D43) |
| GET | `/api/account/companies` | Firmy dostępne dla użytkownika (własna i z zaproszeń) z rolą i znacznikiem bieżącej (D46) |
| POST | `/api/account/active-company` | Wybór bieżącej firmy (`companyId`) - tylko spośród dostępnych (404); ustawia ciasteczko HttpOnly `ksef.company`. Inni klienci API mogą wysyłać nagłówek `X-Company-Id` |
| GET | `/api/account/invitations/{token}` | Podgląd zaproszenia (firma, rola, adres, ważność) |
| POST | `/api/account/invitations/accept` | Przyjęcie zaproszenia (`token`); 403 - konto z innym adresem niż zaproszenie, 404 - token nieważny, wygasły, odwołany lub użyty |
| GET | `/api/company/members` | Właściciel: użytkownicy firmy i oczekujące zaproszenia |
| POST | `/api/company/members/invitations` | Właściciel: zaproszenie (`email`, `role`: `Employee` / `Accountant`) - e-mail z linkiem ważnym 7 dni; zwraca link |
| DELETE | `/api/company/members/invitations/{id}` | Właściciel: odwołanie zaproszenia |
| PUT | `/api/company/members/{id}` | Właściciel: zmiana roli |
| DELETE | `/api/company/members/{id}` | Właściciel: odebranie dostępu |
| GET | `/api/dashboard` | Dane wykresów pulpitu: sprzedaż i koszty netto w 12 miesiącach, wiekowanie należności, 5 największych nabywców (D40) |
| GET | `/api/payments/summary` | Należności i zobowiązania: pozostało do zapłaty, w tym po terminie (kwoty w PLN) |
| POST | `/api/invoices/{id}/email` | Wysłanie faktury e-mailem (`to`, `message`) - PDF i XML w załączniku; 202 - wiadomość w kolejce. 409 - faktura nieprzyjęta w KSeF lub poczta nieskonfigurowana, 429 - dzienny limit wiadomości firmy (D36) |
| GET | `/api/invoices/{id}/emails` | Historia wiadomości dotyczących faktury: odbiorca, rodzaj, stan (`Pending`, `Sent`, `Failed`), liczba prób, ostatni błąd |
| POST | `/api/invoices/{id}/reminder` | Przypomnienie o płatności (`to`, `message`) z kwotą pozostałą do zapłaty i fakturą w załączniku; 409 - faktura opłacona, nieprzyjęta w KSeF lub korekta (D37) |
| GET | `/api/invoices/{id}/email-recipient` | Podpowiedź adresu nabywcy: e-mail kontrahenta albo ostatni adres, na który wysłano fakturę |
| GET | `/api/company/notifications` | Ustawienia powiadomień: automatyczne przypomnienia, liczba dni po terminie, czy poczta jest skonfigurowana |
| PUT | `/api/company/notifications` | Zmiana ustawień (`autoReminders`, `reminderDaysAfterDue` 0-60, `notifyReceivedInvoices`, `notificationEmail` - puste: adres konta) (D37, D38) |
| GET | `/api/recurring-invoices` | Harmonogramy faktur cyklicznych z danymi wzoru, najbliższą datą i ostatnią utworzoną fakturą (D39) |
| GET | `/api/recurring-invoices/{id}` | Harmonogram |
| POST | `/api/recurring-invoices` | Utworzenie (`templateInvoiceId` - faktura VAT firmy, `intervalMonths` 1/3/6/12, `nextIssueDate` - nie w przeszłości, `endDate`, `name`, `isActive`, `notifyByEmail`) |
| PUT | `/api/recurring-invoices/{id}` | Zmiana okresu, dat, wstrzymanie/wznowienie (wzoru nie można zmienić) |
| DELETE | `/api/recurring-invoices/{id}` | Usunięcie harmonogramu (utworzone faktury pozostają) |
| GET | `/api/exports/summary?from=&to=` | Podsumowanie okresu dla księgowej: liczba i kwoty faktur sprzedaży (przyjętych w KSeF, wg daty wystawienia) i zakupu (wg daty otrzymania w KSeF). 400 - brak okresu, koniec przed początkiem, okres dłuższy niż 366 dni (D34) |
| GET | `/api/exports/sales.csv?from=&to=` | Zestawienie sprzedaży (CSV: średnik, przecinek dziesiętny, UTF-8 z BOM; kwoty per stawka) |
| GET | `/api/exports/purchases.csv?from=&to=` | Zestawienie zakupów (CSV, jak wyżej; z wynikiem sprawdzenia rachunku na białej liście) |
| GET | `/api/exports/package.zip?from=&to=` | Paczka ZIP: oba zestawienia, `README.txt`, `sprzedaz/` - XML wysłany do KSeF i `*_UPO.xml`, `zakupy/` - XML pobrany z KSeF |
| GET | `/api/health` | Stan aplikacji i bazy danych (bez uwierzytelniania) |

## Kody odpowiedzi

| Kod | Znaczenie |
|---|---|
| 200 / 201 / 204 | Sukces |
| 400 | Błąd walidacji: lista błędów per pole, np. `Address.PostalCode`, `Items[1].Quantity` |
| 401 | Brak lub nieważny token |
| 404 | Rekord nie istnieje **lub należy do innego użytkownika** (celowo nierozróżniane) |
| 422 | Faktury nie da się przedstawić w strukturze FA(3) (np. dane sprzed wprowadzenia ograniczeń) |
| 429 | Przekroczony limit (synchronizacja z KSeF częściej niż raz na minutę, dzienny limit zapytań do wykazu VAT) |
| 502 | Błąd usługi zewnętrznej (KSeF, wykaz podatników VAT) - komunikat z jej odpowiedzi |
| 409 | Konflikt: duplikat (numer faktury, NIP kontrahenta), zmiana danych w międzyczasie (nieaktualna wersja), operacja niedozwolona w danym statusie, brak utworzonej firmy |

## Reguły biznesowe

**Uprawnienia ról (D46).** Operacje zmieniające dane wymagają roli: dane firmy, token KSeF, powiadomienia i użytkownicy -
właściciel; faktury, kontrahenci, katalog, faktury cykliczne, e-maile - właściciel lub pracownik; płatności - każda rola.
Brak uprawnień - 403. Odczyt - każda rola.

**Izolacja danych.** Identyfikator firmy nigdy nie pochodzi z żądania. Jest ustalany na podstawie zalogowanego
użytkownika (`CurrentCompany`) i dodawany do każdego zapytania. Dostęp do cudzego rekordu, nawet przy znanym
identyfikatorze, kończy się odpowiedzią 404. Próba użycia cudzego kontrahenta na fakturze kończy się 400.
Pokrywa to test `DataIsolationTests`.

**Faktura.**
- Dane sprzedawcy są kopiowane z profilu firmy, a dane nabywcy z kontrahenta (`customerId`) albo z pola `buyer`.
- Kwoty liczy wyłącznie serwer (klient nie przesyła kwot): netto pozycji = ilość × cena, zaokrąglone do groszy;
  VAT liczony od sumy netto w każdej stawce; zaokrąglenie połówkowe od zera.
- Pusty numer oznacza nadanie automatyczne w formacie `FV/{n}/{MM}/{RRRR}` (numeracja miesięczna w ramach firmy).
  Przy równoczesnym utworzeniu dwóch faktur konflikt numerów jest wykrywany i numer nadawany ponownie.
- Edycja wymaga przesłania `version` z ostatniego odczytu. Jeśli faktura została w międzyczasie zmieniona,
  zwracane jest 409 (optymistyczna kontrola współbieżności).
- Statusy: edycja i usunięcie możliwe tylko dla `Draft` i `Rejected`. Faktury `Pending` (przetwarzane przez KSeF)
  i `Accepted` (z numerem KSeF) są nienaruszalne. Poprawiona faktura odrzucona wraca do statusu `Draft`.

**Walidacja.**
- Reguły walidacji są zapisane atrybutami w DTO (`FakturowanieKSeF.Contracts`): NIP z sumą kontrolną,
  kod pocztowy `00-000` dla Polski, kody kraju i waluty, rachunek 10–34 znaki, ilość > 0, cena ≥ 0.
- Działają też zależności między polami: firma wymaga NIP, przelew wymaga terminu płatności, termin nie może być
  wcześniejszy niż data wystawienia, nabywca musi być wskazany, waluta tylko PLN, pozycje „zw” wymagają podstawy
  prawnej zwolnienia, stawki 4% i 3% (ryczałt dla taksówek) są niedostępne.
- API sprawdza je automatycznie, a ten sam kod będzie używany w formularzach Blazor.
- Dane są normalizowane przed zapisem: NIP bez myślników i prefiksu „PL”, rachunek bez spacji.
