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
| POST | `/api/invoices` | Utworzenie faktury (szkicu) |
| PUT | `/api/invoices/{id}` | Edycja faktury (tylko szkic lub faktura odrzucona) |
| DELETE | `/api/invoices/{id}` | Usunięcie faktury (tylko szkic lub faktura odrzucona) |
| GET | `/api/health` | Stan aplikacji i bazy danych (bez uwierzytelniania) |

## Kody odpowiedzi

| Kod | Znaczenie |
|---|---|
| 200 / 201 / 204 | Sukces |
| 400 | Błąd walidacji: lista błędów per pole, np. `Address.PostalCode`, `Items[1].Quantity` |
| 401 | Brak lub nieważny token |
| 404 | Rekord nie istnieje **lub należy do innego użytkownika** (celowo nierozróżniane) |
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
  wcześniejszy niż data wystawienia, nabywca musi być wskazany.
- API sprawdza je automatycznie, a ten sam kod będzie używany w formularzach Blazor.
- Dane są normalizowane przed zapisem: NIP bez myślników i prefiksu „PL”, rachunek bez spacji.
