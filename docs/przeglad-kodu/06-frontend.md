# Obszar 06 - frontend (Blazor WebAssembly)

Raport zapisz w `docs/przeglad-kodu/wyniki/06-frontend.md`. ID znalezisk: `UI-1`, `UI-2`, …
Najpierw przeczytaj `00-zasady.md`, potem decyzje D13, D18-D22, D40, D45, D50 i `docs/instrukcja-uzytkownika.md`.

## Zakres plików

- `FakturowanieKSeF.Client/Pages/*.razor`, `Components/*.razor`, `Components/*.cs`, `Layout/*.razor`
- `FakturowanieKSeF.Client/Services/*` (`ApiClient`, `AuthService`, `CompanyContext`, `Format`)
- `FakturowanieKSeF.Client/Program.cs`, `wwwroot/index.html`, `wwwroot/css/app.css`, `wwwroot/js/*`,
  `service-worker*.js`, `manifest.webmanifest`
- testy E2E: `FakturowanieKSeF.Tests/E2E/*` (do oceny pokrycia, nie stylu)

## Pytania kontrolne

**Błędy działania**
- Obsługa odpowiedzi API: czy każdy błąd (400 z polami, 401 po wygaśnięciu sesji, 403, 404, 409, 429, 500, brak sieci)
  daje czytelny komunikat i nie zostawia formularza w stanie „zapisywanie…” na zawsze?
- Podwójne kliknięcia i wyścigi: wysyłka do KSeF, zapis faktury, rejestracja płatności, przełączanie firmy.
  Odpowiedzi przychodzące w złej kolejności (wyszukiwarka z opóźnieniem, filtry w adresie strony).
- Cykl życia komponentów: `IDisposable`/`IAsyncDisposable` dla timerów, `CancellationTokenSource`, odświeżania
  statusu wysyłki - wycieki po opuszczeniu strony, aktualizacje stanu po `Dispose`.
- Kultura pl-PL: liczby z przecinkiem w polach `InputNumber`, daty, waluty; parsowanie w `Format`.
- `CompanyContext` i `AuthService`: nieaktualny stan po wylogowaniu, zmianie firmy, wygaśnięciu sesji.
- Walidacja `DeepValidator`: czy błędy serwera trafiają do właściwych pól (także w pozycjach faktury).

**Bezpieczeństwo po stronie klienta** (tylko to, czego nie obejmuje obszar 01)
- `MarkupString` z danymi użytkownika (XSS), `returnUrl` po logowaniu (otwarte przekierowanie), dane w
  `localStorage`, informacje wrażliwe w adresach URL.

**Wydajność**
- Zbędne zapytania: te same dane pobierane wielokrotnie (np. firma, kontrahenci, katalog na każdym renderze),
  zapytania sekwencyjne, które mogłyby iść równolegle (`Task.WhenAll`).
- Renderowanie: duże listy bez wirtualizacji/stronicowania, `StateHasChanged` w pętlach, ciężkie obliczenia
  w szablonie (np. w `@foreach` przy każdym renderze - wykres, sumy formularza faktury).
- Rozmiar aplikacji: nieużywane zależności, trimming, kompresja; czas pierwszego wczytania.
- CSS: martwe reguły w `app.css` (klasy nieużywane w żadnym `.razor`), reguły nadpisujące się nawzajem.

**Dostępność (krótko, tylko błędy)**
- Kontrolki bez etykiet, elementy klikalne niebędące przyciskami/linkami, utrata fokusu po nawigacji,
  kontrast w trybie ciemnym (szczególnie tekst `muted` i plakietki `pill`).

**Uproszczenia**
- Powtarzające się fragmenty stron (nagłówki, karty z tabelą, stany „wczytywanie/pusto/błąd”) - czy warto
  wydzielić wspólne komponenty? Kod martwy po przebudowie interfejsu (D50).
