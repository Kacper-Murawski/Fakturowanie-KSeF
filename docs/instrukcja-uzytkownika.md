# Instrukcja użytkownika

Aplikacja Fakturowanie KSeF służy do wystawiania faktur VAT, wysyłania ich do Krajowego Systemu e-Faktur (KSeF)
i odbierania faktur od kontrahentów. **Wersja testowa** komunikuje się wyłącznie ze środowiskiem testowym KSeF:
faktury nie mają mocy prawnej. Używaj wyłącznie danych fikcyjnych.

## 1. Konto

**Rejestracja.** Wybierz „Zarejestruj się”, podaj e-mail i hasło. Hasło musi mieć co najmniej 8 znaków,
w tym wielką i małą literę, cyfrę oraz znak specjalny. Po rejestracji zostaniesz zalogowany i przeniesiony
do danych firmy.

![Logowanie](zrzuty/00-logowanie.png)

**Logowanie i wylogowanie.** Sesja trwa 8 godzin i wydłuża się przy aktywności. Po 5 nieudanych próbach logowania
konto jest blokowane na 15 minut. Przycisk „Wyloguj” znajduje się w prawym górnym rogu.

## 2. Moja firma i integracja z KSeF

W menu **Moja firma** uzupełnij dane, które pojawią się jako sprzedawca na fakturach: nazwę, NIP, adres
i rachunek bankowy. Rachunek będzie podpowiadany na fakturach płatnych przelewem.

W sekcji **Integracja z KSeF** skonfiguruj token, bez którego nie można wysyłać ani odbierać faktur:
- **Zapisz token**: wklej token wygenerowany w Aplikacji Podatnika KSeF. Aplikacja sprawdzi go w KSeF przed zapisaniem.
- **Wygeneruj token testowy**: aplikacja sama utworzy token w testowym KSeF dla NIP-u firmy.

Token jest przechowywany w postaci zaszyfrowanej i nie jest nigdy wyświetlany.

![Moja firma i integracja z KSeF](zrzuty/03-moja-firma-i-ksef.png)

## 3. Kontrahenci

W menu **Kontrahenci** możesz prowadzić książkę adresową nabywców: firmy z NIP-em lub osoby prywatne.
Wyszukiwanie działa po nazwie i NIP. Zmiana danych kontrahenta nie zmienia faktur już wystawionych.

![Formularz kontrahenta](zrzuty/04-kontrahent-formularz.png)

## 4. Wystawianie faktury

Wybierz **Nowa faktura**:
1. **Numer**: zostaw puste pole, a aplikacja nada kolejny numer (np. `FV/1/11/2026`). Możesz też wpisać własny.
2. **Daty**: data wystawienia oraz data sprzedaży, jeśli jest inna.
3. **Nabywca**: wybierz kontrahenta z listy albo wpisz dane ręcznie.
4. **Pozycje**: nazwa, jednostka, ilość, cena netto i stawka VAT. Wartości i podsumowanie według stawek liczą się
   na bieżąco. Przy stawce „zw” podaj podstawę prawną zwolnienia.
5. **Płatność**: forma, termin (przyciski 7, 14 lub 30 dni) i rachunek.
6. **Zapisz fakturę**. Faktura zostaje zapisana jako **szkic**, który możesz jeszcze edytować lub usunąć.

![Nowa faktura](zrzuty/06-nowa-faktura.png)

## 5. Wysyłka do KSeF

W podglądzie szkicu wybierz **Wyślij do KSeF** i potwierdź. Faktura otrzyma status „Wysłana do KSeF”,
a strona będzie odświeżać się automatycznie.

![Faktura w trakcie wysyłki](zrzuty/08-faktura-wysylana.png)

Po kilku sekundach KSeF nada fakturze **numer KSeF**, a status zmieni się na „Przyjęta w KSeF”. Od tej chwili
faktura jest dokumentem ostatecznym i nie można jej zmienić. Dostępne są przyciski:
- **Pobierz UPO**: urzędowe poświadczenie odbioru faktury przez KSeF,
- **Pobierz PDF**: wydruk faktury z kodem QR weryfikacji,
- **Pobierz XML (FA(3))**: faktura w postaci przesłanej do KSeF.

![Faktura przyjęta w KSeF](zrzuty/09-faktura-przyjeta-w-ksef.png)

Jeśli KSeF **odrzuci** fakturę, zobaczysz kod błędu i jego opis. Popraw fakturę („Edytuj”) i wyślij ją ponownie.

## 6. Wydruk (PDF) i kod QR

PDF zawiera kod QR weryfikacji z numerem KSeF pod spodem. Odbiorca, który dostał fakturę poza KSeF
(e-mailem, w wydruku), może zeskanować kod, żeby sprawdzić, czy faktura znajduje się w KSeF, i pobrać jej XML.
PDF szkicu jest oznaczony jako dokument niebędący fakturą.

![Wydruk PDF](zrzuty/14-wydruk-pdf.png)

## 7. Lista faktur i pulpit

W menu **Faktury** jest lista faktur z wyszukiwaniem (numer, nabywca, NIP, numer KSeF), filtrami statusu i dat
oraz stronicowaniem. **Pulpit** pokazuje liczbę faktur w każdym statusie.

![Lista faktur](zrzuty/10-lista-faktur.png)

![Pulpit](zrzuty/11-pulpit.png)

## 8. Faktury otrzymane

W menu **Faktury otrzymane** znajdują się faktury wystawione na NIP Twojej firmy przez innych podatników,
pobrane z KSeF. Aplikacja pobiera je automatycznie co 15 minut. Możesz też wybrać **Pobierz z KSeF**,
nie częściej niż raz na minutę. Nowa faktura jest dostępna do pobrania zwykle kilka minut po jej wystawieniu.

![Faktury otrzymane](zrzuty/12-faktury-otrzymane.png)

Podgląd faktury otrzymanej pokazuje jej pełną treść, a także umożliwia pobranie XML i PDF z kodem QR.

![Faktura otrzymana](zrzuty/13-faktura-otrzymana.png)

## Najczęstsze komunikaty

| Komunikat | Znaczenie i rozwiązanie |
|---|---|
| „Najpierw uzupełnij dane swojej firmy.” | Wypełnij formularz w menu Moja firma. |
| „Najpierw skonfiguruj token KSeF…” | Zapisz lub wygeneruj token w sekcji Integracja z KSeF. |
| „Dane zostały w międzyczasie zmienione…” | Faktura była edytowana w innym oknie. Odśwież stronę i powtórz zmianę. |
| „Faktura o tym numerze już istnieje.” | Zmień numer albo zostaw pole puste (numer automatyczny). |
| „Synchronizacja była uruchomiona przed chwilą…” | Odczekaj minutę przed ponownym pobraniem faktur otrzymanych. |
| „W obecnej wersji obsługiwane są wyłącznie faktury w PLN.” | Ograniczenie wersji MVP. |
