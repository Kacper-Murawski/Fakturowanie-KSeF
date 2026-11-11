# Obszar 01 - bezpieczeństwo aplikacji

Raport zapisz w `docs/przeglad-kodu/wyniki/01-bezpieczenstwo.md`. ID znalezisk: `SEC-1`, `SEC-2`, …
Najpierw przeczytaj `00-zasady.md`, potem `docs/bezpieczenstwo.md` i `docs/wdrozenie.md`.

## Zakres plików

- `FakturowanieKSeF.Api/Program.cs`, `FakturowanieKSeF.Api/Infrastructure/*` (szczególnie `SecurityHeaders.cs`,
  `RateLimits.cs`, `ApiErrors.cs`, `StaticFileCaching.cs`, `PolishIdentityErrorDescriber.cs`)
- `FakturowanieKSeF.Api/Endpoints/AccountEndpoints.cs`, `MemberEndpoints.cs`, `KsefSettingsEndpoints.cs`
- `FakturowanieKSeF.Api/Services/KsefTokenProtector.cs`, `Services/Email/*`
- `FakturowanieKSeF.Api/Integrations/*` (klienci HTTP NBP i wykazu VAT)
- `FakturowanieKSeF.Ksef/*` (szczególnie `KsefTestOnlyHandler.cs`, `Fa3/Fa3InvoiceReader.cs`, `Fa3/Fa3SchemaValidator.cs`)
- `FakturowanieKSeF.Client/Services/*`, `Client/wwwroot/index.html`, `Client/wwwroot/js/*`, `service-worker.published.js`
- `Dockerfile`, `deploy/*`, `.github/workflows/*`, `appsettings*.json`

## Pytania kontrolne (sprawdź każde i odnotuj wynik)

**Uwierzytelnianie i sesja**
- Czy ciasteczko sesji ma `HttpOnly`, `Secure` (produkcyjnie), `SameSite` i czy da się przejąć sesję lub wykonać CSRF
  na operacji zmieniającej dane (formularze, przełączenie firmy `ksef.company`, przyjęcie zaproszenia)?
- Czy endpointy Identity (`MapIdentityApi`: rejestracja, reset hasła, potwierdzenie e-mail, `manage/*`) nie otwierają
  ścieżki obejścia 2FA, wyliczania kont (różne odpowiedzi dla istniejącego/nieistniejącego e-maila) albo zmiany e-maila bez hasła?
- 2FA (`AccountEndpoints.cs`): czy wyłączenie, nowe kody odzyskiwania i blokada wbudowanego `/api/auth/manage/2fa`
  są szczelne? Czy kod TOTP można odtworzyć (brak limitu prób poza blokadą konta)?
- Limity żądań (`RateLimits.cs`): czy partycja po IP za proxy nie pozwala ominąć limitu nagłówkiem `X-Forwarded-For`?
  Czy limit logowania obejmuje też kroki 2FA i rejestrację?

**Zaproszenia i role**
- Token zaproszenia: entropia, przechowywanie (skrót), jednorazowość, wygasanie, wiązanie z adresem e-mail,
  wyścig przy równoczesnym przyjęciu, możliwość nadania sobie wyższej roli.

**Dane wejściowe i wstrzyknięcia**
- XML z KSeF (faktury otrzymane, UPO): XXE, „billion laughs”, bardzo duże dokumenty, złośliwe wartości w polach
  wyświetlanych w UI lub w PDF.
- E-mail: wstrzyknięcie nagłówków (`To`, `Subject`, `Reply-To`), treść z danych użytkownika, linki w treści.
- CSV w eksporcie dla księgowej: wstrzyknięcie formuł (`=`, `+`, `-`, `@` na początku komórki).
- PDF (QuestPDF): dane użytkownika w dokumencie, zasoby zewnętrzne.
- Czy gdziekolwiek jest surowy SQL, `MarkupString` z danymi użytkownika, `eval`/dynamiczny JS?
- Nazwy plików w nagłówkach `Content-Disposition` (numer faktury od użytkownika).

**Sieć i zależności**
- SSRF: czy użytkownik może wpłynąć na adres, z którym łączy się serwer (NBP, wykaz VAT, KSeF, SMTP, `App:PublicUrl`)?
- Czy `KsefTestOnlyHandler` jest na każdym HttpClient do KSeF (także w kliencie MF z submodułu) i czy nie da się go obejść
  przekierowaniem HTTP?
- Odporność na DoS: duże ciała żądań, stronicowanie bez limitu `pageSize`, eksport ZIP za długi okres,
  kosztowne zapytania dostępne bez uwierzytelnienia.

**Sekrety i konfiguracja**
- Czy jakikolwiek sekret (token KSeF, hasło SMTP, connection string) trafia do logów, odpowiedzi API, komunikatów błędów
  lub ProblemDetails (`ApiErrors.ExceptionHandler`)?
- Produkcyjna konfiguracja: HSTS, CSP (czy `'unsafe-inline'` w `style-src` jest konieczne), `AllowedHosts`,
  nagłówki `X-Forwarded-*`, Swagger tylko w Development, szczegóły wyjątków.
- Obraz Docker: użytkownik bez uprawnień, brak sekretów w warstwach, CI bez wycieków sekretów.

**Zależności**
- Ostrzeżenia NuGet o podatnościach (np. `System.Security.Cryptography.Pkcs`/`Xml` z submodułu klienta MF):
  czy podatna ścieżka kodu jest osiągalna w tej aplikacji i jak to załatać bez aktualizacji submodułu.
