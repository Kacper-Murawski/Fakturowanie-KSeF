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
| GET | `/api/customers?q=` | Lista kontrahentów, wyszukiwanie po nazwie lub NIP |
| GET | `/api/customers/{id}` | Szczegóły kontrahenta |
| POST | `/api/customers` | Dodanie kontrahenta |
| PUT | `/api/customers/{id}` | Edycja kontrahenta |
| DELETE | `/api/customers/{id}` | Usunięcie kontrahenta (miękkie) |
| GET | `/api/invoices?status=&from=&to=&q=&page=&pageSize=` | Lista faktur z filtrami i stronicowaniem |
| GET | `/api/invoices/{id}` | Szczegóły faktury z pozycjami i podsumowaniem VAT |
| GET | `/api/invoices/next-number?issueDate=` | Propozycja kolejnego numeru |
| GET | `/api/invoices/{id}/pdf` | Wizualizacja faktury w PDF (z kodem QR weryfikacji KSeF dla faktur wysłanych) |
| GET | `/api/invoices/{id}/xml` | XML faktury w strukturze FA(3), zweryfikowany względem schematu XSD (422, gdy faktury nie da się przedstawić w FA(3)) - zob. `docs/fa3-xml.md` |
| POST | `/api/invoices` | Utworzenie faktury (szkicu) |
| PUT | `/api/invoices/{id}` | Edycja faktury (tylko szkic lub faktura odrzucona) |
| DELETE | `/api/invoices/{id}` | Usunięcie faktury (tylko szkic lub faktura odrzucona) |
| POST | `/api/invoices/{id}/send` | Wysłanie faktury do KSeF (body: `version`); 202 i status `Pending`, dalej asynchronicznie - zob. `docs/integracja-ksef.md` |
| GET | `/api/invoices/{id}/upo` | UPO faktury przyjętej przez KSeF (404, jeśli jeszcze niedostępne) |
| GET | `/api/company/ksef` | Stan integracji z KSeF (czy token jest skonfigurowany; sam token nigdy nie jest zwracany) |
| PUT | `/api/company/ksef/token` | Zapis tokenu KSeF po weryfikacji w KSeF (400, jeśli KSeF go odrzuci) |
| POST | `/api/company/ksef/test-token` | Wygenerowanie tokenu w testowym KSeF dla NIP firmy (502 przy błędzie KSeF) |
| DELETE | `/api/company/ksef/token` | Usunięcie tokenu |
| GET | `/api/received-invoices?q=&from=&to=&page=&pageSize=` | Lista faktur otrzymanych (z lokalnej bazy) |
| GET | `/api/received-invoices/{id}` | Faktura otrzymana z treścią odczytaną z XML |
| GET | `/api/received-invoices/{id}/xml` | XML faktury otrzymanej (w postaci pobranej z KSeF) |
| GET | `/api/received-invoices/{id}/pdf` | Wizualizacja faktury otrzymanej w PDF z kodem QR weryfikacji |
| POST | `/api/received-invoices/{id}/bank-account-check` | Sprawdzenie rachunku z faktury w wykazie podatników VAT na dziś; wynik zapisany przy fakturze (409 - brak rachunku lub rachunek zagraniczny, 429 - limit, 502 - błąd MF) (D29) |
| GET | `/api/received-invoices/sync-status` | Stan synchronizacji (ostatnia synchronizacja, punkt kontrolny, błąd) |
| POST | `/api/received-invoices/sync` | Ręczna synchronizacja z KSeF (409 bez tokenu, 429 częściej niż raz na minutę, 502 przy błędzie KSeF) |
| GET | `/api/vat-registry/{nip}` | Dane podmiotu z wykazu podatników VAT (Biała lista MF): nazwa, adres rozbity na pola, status VAT, rachunki. 404 - brak w wykazie, 429 - dzienny limit zapytań, 502 - błąd API MF (D28) |
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
