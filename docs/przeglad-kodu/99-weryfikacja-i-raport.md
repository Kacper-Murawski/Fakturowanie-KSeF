# Krok końcowy - weryfikacja znalezisk i raport zbiorczy

Wykonuje się po zakończeniu obszarów 01-07. Wynik: `docs/przeglad-kodu/wyniki/RAPORT.md`.
Obowiązują zasady z `00-zasady.md` (tylko czytanie, bez KSeF/NBP/wykazu VAT, testy tymczasowe poza repozytorium).

## Zadania

1. **Wczytaj wszystkie raporty** z `docs/przeglad-kodu/wyniki/01-…07-…`.
2. **Usuń duplikaty** - ten sam problem zgłoszony w kilku obszarach połącz w jedno znalezisko (zachowaj wszystkie ID
   źródłowe, np. `SEC-3 = ISO-5`).
3. **Zweryfikuj każde znalezisko o wadze krytycznej i wysokiej:**
   - przeczytaj wskazany kod i kod wywołujący; sprawdź, czy scenariusz jest możliwy w aplikacji (a nie tylko w teorii),
   - sprawdź, czy nie jest to świadoma decyzja z `docs/decyzje-projektowe.md` (wtedy odrzuć z uzasadnieniem),
   - gdy to możliwe, potwierdź tymczasowym testem (poza repozytorium) - i usuń go,
   - nadaj ostateczny status: **potwierdzone** / **odrzucone** (dlaczego) / **niepewne** (czego brakuje).
   Znaleziska średnie i niskie zweryfikuj wyrywkowo (co najmniej co trzecie) i oznacz, które sprawdzono.
4. **Uporządkuj** potwierdzone znaleziska według: waga → koszt poprawki → ryzyko poprawki.

## Struktura `RAPORT.md`

1. **Podsumowanie** (do pół strony): ogólna ocena kodu w czterech kategoriach - poprawność, bezpieczeństwo,
   wydajność, utrzymywalność - każda w skali 1-5 z jednym zdaniem uzasadnienia.
2. **Tabela znalezisk**: ID, tytuł, waga, rodzaj, status, miejsce, szacowany koszt poprawki (S/M/L).
3. **Plan poprawek**: grupy znalezisk, które warto naprawić razem jako jedną gałąź i jeden PR
   (proponowana nazwa gałęzi po polsku zgodnie z `CLAUDE.md`, np. `fix/izolacja-…`), w kolejności wykonania.
4. **Odrzucone znaleziska** z uzasadnieniem (to też wartość - pokazuje, co sprawdzono).
5. **Luki w testach** do uzupełnienia razem z poprawkami.
6. **Wnioski do pracy inżynierskiej**: 5-10 zdań nadających się do rozdziału o testowaniu i jakości
   (metoda przeglądu, liczba i rodzaje znalezisk, co poprawiono).
