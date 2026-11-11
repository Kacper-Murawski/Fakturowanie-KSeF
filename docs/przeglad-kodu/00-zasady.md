# Przegląd kodu - zasady wspólne dla wszystkich obszarów

Przeczytaj ten plik w całości przed plikiem swojego obszaru (`01-…` - `07-…`). Obowiązuje każdego wykonawcę przeglądu.

## Cel

Znaleźć w kodzie aplikacji Fakturowanie KSeF:
1. **błędy** - zachowanie niezgodne z zamierzonym (złe kwoty, złe stany, wyjątki, wyścigi, utrata danych),
2. **luki bezpieczeństwa** - wszystko, co atakujący (zalogowany lub nie) może wykorzystać,
3. **nieefektywności** - kod, który da się napisać wydajniej (zapytania do bazy, pamięć, sieć, renderowanie)
   albo prościej (powtórzenia, zbędne warstwy, martwy kod), bez zmiany zachowania.

To przegląd, nie implementacja: **niczego nie poprawiasz**. Wynikiem jest raport znalezisk.

## Kontekst, który musisz znać przed zgłoszeniem czegokolwiek

Przeczytaj najpierw: `CLAUDE.md`, `docs/architektura.md`, `docs/decyzje-projektowe.md` (decyzje D1-D51) i dokumenty
wskazane w pliku Twojego obszaru. Poniższe rzeczy są **świadomymi decyzjami** - nie zgłaszaj ich jako błędów
(możesz zgłosić, jeśli ich **implementacja** jest wadliwa):

- KSeF wyłącznie w środowisku testowym (`KsefTestOnlyHandler`, brak wartości produkcyjnej) - D8,
- dane firmy należą do `CompanyId`, firmę wybiera `CurrentCompany` spośród firm dostępnych dla użytkownika - D15, D46,
- przeglądarka loguje się ciasteczkiem HttpOnly; tokeny Bearer zostają dla innych klientów API - D18,
- token KSeF szyfrowany Data Protection, klucze w bazie (produkcyjnie dodatkowo certyfikatem) - D23, D48,
- wysyłka do KSeF jako maszyna stanów; po przyjęciu przez KSeF nigdy `Rejected` z powodu błędu komunikacji - D25,
- odbiór faktur przyrostowo „do HWM” z budżetem zapytań - D26,
- dane demonstracyjne tylko w Development - D49; design system na Bootstrapie 5.3 - D50.

## Zasady pracy

- **Tylko czytanie.** Nie zmieniaj plików w repozytorium, nie twórz gałęzi, nie commituj, nie pushuj.
  Jedyny plik, który zapisujesz, to raport w `docs/przeglad-kodu/wyniki/` (nazwa w pliku obszaru).
- **Bez połączeń z KSeF, NBP i wykazem VAT.** Nie uruchamiaj aplikacji z prawdziwymi tokenami ani testów
  `KSEF_LIVE_TESTS` / `ExternalApiLiveTests`. Testy zwykłe (`dotnet test`) wolno uruchamiać - używają atrap.
- **Dowód zamiast przypuszczeń.** Każde znalezisko wskazuje konkretny kod (plik i linię) i konkretny scenariusz
  (dane wejściowe → zły wynik). Gdy możesz, potwierdź je: przeczytaj kod wywołujący, sprawdź test, a w razie potrzeby
  napisz tymczasowy test w katalogu roboczym poza repozytorium albo uruchom fragment - i **usuń go po sprawdzeniu**.
  Jeśli nie da się potwierdzić, oznacz znalezisko jako „do weryfikacji” i napisz, czego brakuje.
- **Nie zgłaszaj stylu.** Nazewnictwo, formatowanie, kolejność `using` - pomiń, chyba że prowadzą do błędu.
- **Nie zgłaszaj rzeczy spoza swojego obszaru**, chyba że są krytyczne - wtedy dopisz je na końcu raportu
  w sekcji „Poza obszarem”.
- **Nie duplikuj** - jeden problem występujący w wielu miejscach to jedno znalezisko z listą miejsc.

## Skala wag

| Waga | Znaczenie |
|---|---|
| **Krytyczne** | wyciek cudzych danych, obejście autoryzacji, utrata lub uszkodzenie danych, duplikat faktury w KSeF, błędna kwota podatku na fakturze |
| **Wysokie** | błąd, który użytkownik spotka w typowym użyciu; luka wymagająca nietypowych warunków; nieefektywność rosnąca z liczbą danych (np. zapytanie N+1 na liście) |
| **Średnie** | błąd w rzadkim przypadku brzegowym; brak ochrony „w głąb”; wyraźnie zbędny koszt (pamięć, zapytania) |
| **Niskie** | uproszczenie kodu, drobna nieefektywność, poprawa czytelności istotna dla utrzymania |

## Format raportu

Plik Markdown, po polsku. Najpierw krótkie podsumowanie (3-5 zdań: stan obszaru, najważniejsze ryzyka),
potem znaleziska posortowane od najpoważniejszych:

```markdown
### [ID obszaru]-[numer]. Krótki tytuł
- **Waga:** krytyczne | wysokie | średnie | niskie
- **Rodzaj:** błąd | bezpieczeństwo | wydajność | uproszczenie
- **Pewność:** potwierdzone (jak) | do weryfikacji (czego brakuje)
- **Miejsce:** `ścieżka/Plik.cs:123` (i kolejne, jeśli dotyczy wielu miejsc)
- **Problem:** co jest nie tak, w 1-3 zdaniach.
- **Scenariusz:** konkretne kroki lub dane wejściowe → obserwowany zły skutek.
- **Propozycja poprawki:** co zmienić (krótko; fragment kodu tylko gdy to wyjaśnia).
- **Test, który to wykryje:** jaki test dopisać (nazwa i co sprawdza).
```

Na końcu raportu:
- sekcja **„Sprawdzone bez uwag”** - lista przejrzanych elementów, w których nie znaleziono problemów
  (żeby było wiadomo, co zostało pokryte),
- sekcja **„Poza obszarem”** (jeśli są),
- **„Pytania do autora”** - miejsca, gdzie zamierzone zachowanie jest niejasne.

Jeżeli w danej kategorii nie ma znalezisk, napisz to wprost - pusty wynik jest wartościowy.
