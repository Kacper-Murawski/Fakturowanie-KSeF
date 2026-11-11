# Przegląd kodu - zlecenia dla modelu wykonującego analizę

Zestaw zleceń kompleksowego przeglądu aplikacji: **poprawność (błędy), bezpieczeństwo (odporność na ataki),
efektywność (wydajność i prostota kodu)**. Przegląd jest podzielony na obszary, żeby każdy wykonawca analizował
mniejszą część kodu dokładnie, zamiast całego repozytorium powierzchownie.

| Plik | Obszar | ID znalezisk |
|---|---|---|
| [`00-zasady.md`](00-zasady.md) | zasady wspólne, format raportu, skala wag - **czyta każdy wykonawca** | - |
| [`01-bezpieczenstwo.md`](01-bezpieczenstwo.md) | uwierzytelnianie, sesja, 2FA, wstrzyknięcia, XML, e-mail, CSV, sekrety, konfiguracja, zależności | `SEC` |
| [`02-izolacja-danych-i-role.md`](02-izolacja-danych-i-role.md) | dane jednej firmy niedostępne dla innej, role, IDOR - tabela wszystkich endpointów | `ISO` |
| [`03-ksef-i-uslugi-w-tle.md`](03-ksef-i-uslugi-w-tle.md) | wysyłka do KSeF bez duplikatów, odbiór „do HWM”, usługi w tle, współbieżność | `KSEF` |
| [`04-poprawnosc-faktur.md`](04-poprawnosc-faktur.md) | kwoty i VAT, FA(3), korekty, waluty i kurs NBP, numeracja, płatności | `FAK` |
| [`05-backend-wydajnosc-i-baza.md`](05-backend-wydajnosc-i-baza.md) | zapytania EF Core, indeksy, transakcje, pamięć, uproszczenia backendu | `PERF` |
| [`06-frontend.md`](06-frontend.md) | Blazor: błędy, wyścigi, wycieki, wydajność renderowania, dostępność | `UI` |
| [`07-testy-i-jakosc.md`](07-testy-i-jakosc.md) | luki w testach, kruche testy, obsługa wyjątków, martwy kod, ostrzeżenia | `TST` |
| [`99-weryfikacja-i-raport.md`](99-weryfikacja-i-raport.md) | weryfikacja znalezisk, usunięcie duplikatów, raport zbiorczy i plan poprawek | - |

Wyniki trafiają do katalogu [`wyniki/`](wyniki/): jeden raport na obszar oraz `RAPORT.md` z kroku końcowego.

## Jak uruchomić

### Wariant A - z jednej sesji Claude Code (orkiestracja)

W sesji Claude Code w katalogu repozytorium wpisz:

```text
Wykonaj przegląd kodu opisany w docs/przeglad-kodu/README.md.
Uruchom równolegle 7 podagentów na modelu fable, po jednym na obszar 01-07. Każdy dostaje polecenie:
„Przeczytaj docs/przeglad-kodu/00-zasady.md i docs/przeglad-kodu/<plik obszaru>, wykonaj przegląd i zapisz raport
w docs/przeglad-kodu/wyniki/<nazwa z pliku obszaru>.” Po zakończeniu wszystkich wykonaj krok
docs/przeglad-kodu/99-weryfikacja-i-raport.md. Niczego w kodzie nie poprawiaj.
```

### Wariant B - każdy obszar w osobnej sesji

Dla każdego obszaru uruchom osobną sesję z modelem Fable (np. `claude --model fable` albo wybór modelu
poleceniem `/model`) i wklej:

```text
Przeczytaj docs/przeglad-kodu/00-zasady.md i docs/przeglad-kodu/0X-<obszar>.md, wykonaj przegląd
i zapisz raport we wskazanym pliku w docs/przeglad-kodu/wyniki/. Niczego w kodzie nie poprawiaj.
```

Na końcu w jednej sesji: `Wykonaj docs/przeglad-kodu/99-weryfikacja-i-raport.md.`

## Przygotowanie środowiska

- Aktualny `main` (wszystkie PR scalone), `git submodule update --init`.
- PostgreSQL uruchomiony (`docker compose up -d` albo lokalna instalacja) - potrzebny do `dotnet test`.
- Do testów E2E: Chromium dla Playwrighta (w kontenerze Claude Code ścieżka w `CLAUDE.md`).
- **Bez** tokenów KSeF, SMTP i user-secrets z prawdziwymi danymi - przegląd nie łączy się z usługami zewnętrznymi.

## Uwagi

- Przegląd jest kosztowny (model Fable, 7 równoległych analiz) i może trwać kilkadziesiąt minut.
  Najtańsza sensowna kolejność, jeśli trzeba ograniczyć koszt: 02 → 03 → 04 → 01 → 05 → 06 → 07.
- Raporty są materiałem do pracy inżynierskiej (rozdział o testowaniu i jakości) - nie usuwaj ich po poprawkach;
  poprawki wprowadzaj osobnymi gałęziami i PR, odwołując się do ID znalezisk.
