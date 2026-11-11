# Obszar 07 - testy, odporność na błędy i ogólna jakość kodu

Raport zapisz w `docs/przeglad-kodu/wyniki/07-testy-i-jakosc.md`. ID znalezisk: `TST-1`, `TST-2`, …
Najpierw przeczytaj `00-zasady.md`, potem `docs/testy.md`.

## Zakres plików

- cały projekt `FakturowanieKSeF.Tests` (testy jednostkowe, integracyjne z PostgreSQL, E2E Playwright, atrapy
  w `Infrastructure/`)
- `.github/workflows/*` (CI)
- przekrojowo cały kod produkcyjny - pod kątem obsługi wyjątków i jakości (bez wchodzenia w szczegóły obszarów 01-06)

## Zadania

1. **Uruchom testy** (`dotnet test`, z działającym PostgreSQL; E2E: `E2E_TESTS=1` i ścieżka do Chromium z `CLAUDE.md`)
   i zapisz wynik. Testów `KSEF_LIVE_TESTS` nie uruchamiaj.
2. **Mapa pokrycia ryzyk:** dla najważniejszych reguł (izolacja danych, role, maszyna stanów wysyłki, brak duplikatów,
   „do HWM”, kwoty VAT, korekty, waluty, numeracja, 2FA, limity żądań, CSP) wskaż test, który ją sprawdza,
   albo brak testu. Tabela w raporcie.
3. **Jakość testów:**
   - testy, które przechodzą niezależnie od zachowania (brak asercji, asercje na mockach zamiast efektów),
   - testy kruche: zależne od kolejności, czasu systemowego (`DateTime.Now` zamiast `TimeProvider`), strefy czasowej,
     opóźnień (`Task.Delay`) i selektorów E2E opartych na strukturze DOM zamiast `data-testid`,
   - atrapy (`FakeKsefGateway`, `FakeNbpClient`, `FakeVatWhiteList`, `FakeEmailSender`), które zachowują się inaczej
     niż prawdziwe usługi w sposób ukrywający błędy (np. KSeF zwracający od razu „przyjęta”, brak opóźnienia
     udostępnienia faktury - kod 21165),
   - czas wykonania: najwolniejsze testy i możliwość ich przyspieszenia (wspólne bazy, równoległość).
4. **Obsługa błędów w kodzie produkcyjnym:** połykane wyjątki (`catch {}` / `catch (Exception)` bez logowania),
   wyjątki zamieniane na 500 tam, gdzie powinno być 4xx, brak `CancellationToken` w długich operacjach,
   `async void`, `.Result`/`.Wait()`, `ConfigureAwait` - zgłaszaj tylko przypadki o realnym skutku.
5. **Jakość ogólna:** zduplikowany kod między projektami, martwy kod (nieużywane klasy, metody, endpointy, pliki),
   ostrzeżenia kompilatora (zbuduj rozwiązanie i podaj liczbę i rodzaje ostrzeżeń, pomijając ostrzeżenia NuGet
   z submodułu klienta MF - to obszar 01), niespójności konfiguracji (`appsettings*.json` vs kod).

Dla każdej luki w testach podaj konkretny test do dopisania (nazwa, warstwa, scenariusz, oczekiwany wynik).
