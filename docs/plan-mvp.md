# Plan realizacji MVP

Cel MVP: zalogowany użytkownik wystawia fakturę VAT, wysyła ją do KSeF (środowisko testowe), widzi jej status
i numer KSeF, a także widzi faktury, które inni wystawili na jego NIP. Aplikacja działa na serwerze pod HTTPS.

## Zakres MVP

**W MVP:**
- rejestracja i logowanie,
- profil firmy (1 konto = 1 firma; model danych pozwala później dodać więcej),
- kontrahenci (CRUD),
- faktura VAT podstawowa: tworzenie, edycja szkicu, automatyczne sumy, numeracja,
- generowanie XML FA(3) i walidacja względem XSD,
- wysyłka do KSeF (test), status, numer KSeF, UPO,
- pobieranie faktur zakupowych z KSeF,
- podgląd/wydruk faktury (PDF),
- wdrożenie na serwer (Docker, HTTPS).

**Poza MVP (później):**
- faktury korygujące, zaliczkowe i inne rodzaje,
- wielu użytkowników w jednej firmie, role,
- powiadomienia (e-mail/push), wersja mobilna (PWA),
- raporty, eksport do księgowości, tryb offline KSeF, produkcyjne środowisko KSeF.

## Etapy

| Etap | Zakres | Rezultat (kryterium ukończenia) |
|---|---|---|
| 0. Fundamenty | Hosting klienta Blazor z API (jedna domena, brak CORS), projekt testów xUnit, CI w GitHub Actions (build + testy), seed danych dev | `dotnet run` API serwuje frontend, CI zielone |
| 1. Spike KSeF | Proof of concept w środowisku testowym: uwierzytelnienie, otwarcie sesji, wysłanie przykładowego XML, odebranie numeru KSeF i UPO. Wybór: oficjalny klient .NET MF vs własna implementacja | Działający prototyp konsolowy + decyzja w `decyzje-projektowe.md` |
| 2. Model danych v2 | Snapshot sprzedawcy/nabywcy na fakturze, stawki VAT jako enum (zgodne z FA(3)), sumy per stawka, `DateOnly`, `Restrict` na relacjach, unikalne indeksy, soft delete z filtrem, `UpdatedAt`, encje: dane KSeF firmy, faktury otrzymane | Migracja, diagram ERD |
| 3. Backend | DTO (osobny projekt `Contracts`), endpointy CRUD (firma, kontrahenci, faktury), izolacja danych po `OwnerId`, walidacja (NIP z sumą kontrolną), serwis obliczania sum, numeracja faktur, obsługa błędów (ProblemDetails) | Testy jednostkowe obliczeń i testy integracyjne izolacji danych |
| 4. Frontend | Logowanie/rejestracja, przechowywanie tokenu, layout, strony: profil firmy, kontrahenci, lista faktur (filtry, statusy), formularz faktury z sumami na żywo, podgląd | Pełny przepływ w przeglądarce bez KSeF |
| 5. XML FA(3) | Mapowanie faktury na XML FA(3), walidacja XSD, testy na przykładach MF | Wygenerowany XML przechodzi walidację |
| 6. Wysyłka do KSeF | Bezpieczne przechowywanie tokenu KSeF (szyfrowanie Data Protection), wysyłka w sesji interaktywnej, BackgroundService odpytujący o status i UPO, aktualizacja statusu faktury, obsługa odrzuceń | Faktura z aplikacji ma numer KSeF z testowego KSeF |
| 7. Odbiór z KSeF | Okresowe pobieranie metadanych faktur zakupowych, pobranie XML, parsowanie, lista i podgląd | Faktura wystawiona na nasz NIP pojawia się w aplikacji |
| 8. PDF | Wizualizacja faktury (np. QuestPDF) z numerem KSeF i kodem QR weryfikacji | Pobieranie PDF z listy faktur |
| 9. Wdrożenie | Dockerfile, docker-compose produkcyjny, reverse proxy z automatycznym HTTPS (Caddy), sekrety w zmiennych środowiskowych, backup bazy, VPS + domena | Aplikacja dostępna publicznie pod HTTPS |
| 10. Testy i dokumentacja | Testy E2E głównych scenariuszy (Playwright), testy bezpieczeństwa izolacji danych, dokumentacja do pracy | Komplet rozdziałów technicznych pracy |

## Uzasadnienie kolejności
- Integracja z KSeF to największe ryzyko projektu, dlatego spike (etap 1) jest przed budową modelu i UI:
  jego wyniki mogą zmienić model danych.
- Backend przed frontendem: API testowalne przez Swagger, UI powstaje na stabilnym kontrakcie.
- Wdrożenie można wykonać wcześniej (np. po etapie 4), żeby od początku testować w docelowym środowisku.

## Otwarte pytania do autora
- 1 konto = 1 firma w MVP?
- Czy korekty muszą być w MVP (wymóg promotora)?
- Termin oddania pracy (harmonogram)?
- Wymagany układ rozdziałów pracy?
