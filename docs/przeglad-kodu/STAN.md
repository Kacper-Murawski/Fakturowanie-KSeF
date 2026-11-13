# Stan przeglądu kodu

Aktualizowany przez osobę/sesję koordynującą przegląd po każdej zmianie. Źródłem prawdy jest nagłówek `Status:`
w plikach `wyniki/NN-*.md` - ta tabela to jego skrót.

| Obszar | Raport | Status | Uwagi |
|---|---|---|---|
| 01 Bezpieczeństwo | `wyniki/01-bezpieczenstwo.md` | zakończony | 11 znalezisk (0 krytycznych, 1 wysokie) |
| 02 Izolacja danych i role | `wyniki/02-izolacja-danych-i-role.md` | zakończony | 7 znalezisk (1 krytyczne, 2 wysokie) |
| 03 KSeF i usługi w tle | `wyniki/03-ksef-i-uslugi-w-tle.md` | zakończony | 19 znalezisk (1 krytyczne, 5 wysokich) |
| 04 Poprawność faktur | `wyniki/04-poprawnosc-faktur.md` | zakończony | 15 znalezisk (1 krytyczne, 2 wysokie) |
| 05 Backend - wydajność i baza | `wyniki/05-backend-wydajnosc-i-baza.md` | zakończony | 17 znalezisk (0 krytycznych, 2 wysokie) |
| 06 Frontend | `wyniki/06-frontend.md` | zakończony | 20 znalezisk |
| 07 Testy i jakość | `wyniki/07-testy-i-jakosc.md` | zakończony | 12 znalezisk |
| 99 Weryfikacja i raport | `wyniki/RAPORT.md` | zakończony | 53 potwierdzone, 1 odrzucone, 3 niepewne, 44 bez ponownej weryfikacji |

Kolejność: najpierw 02, 03, 04; potem 01, 05, 06, 07; na końcu 99.

## Poprawki

| Znalezisko | Poprawka | PR | Decyzja |
|---|---|---|---|
| SEC-1 (wysokie) - zgadywanie kodu 2FA | blokada konta + limit 5/min, hasło przy wyłączaniu 2FA i nowych kodach | #43 `fix/logowanie-2fa-i-limity-sesji` | D52 |
| SEC-2 - przekierowanie po logowaniu | `returnUrl` tylko ścieżki aplikacji | #43 | D52 |
| SEC-3 - limit logowania blokuje biuro za NAT | limit tylko dla operacji wrażliwych, logowanie na parę IP + e-mail | #43 | D52 |
| SEC-11 - HSTS 30 dni | 365 dni, bez subdomen | #43 | D52 |
| Poza obszarem - HEALTHCHECK a `AllowedHosts` | `localhost` w `AllowedHosts` produkcji | #43 | D52 |
| SEC-4 - przekaźnik spamu, wyścig limitu | potwierdzenie e-maila, atomowe liczniki firmy i aplikacji, osobna pula wiadomości systemowych | #44 `feature/potwierdzanie-email-i-reset-hasla` | D53 |
| SEC-9 - token zaproszenia w logach | tokeny i kody we fragmencie adresu, podgląd zaproszenia metodą POST | #44 | D53 |
| SEC-10 - brak resetu hasła, zmiana e-maila bez hasła | reset hasła e-mailem, zmiana adresu z hasłem (i 2FA) | #44 | D53 |
| SEC-5 - limit wykazu VAT | limit także na adres IP | #45 `fix/limity-uslug-zewnetrznych` | D54 |
| SEC-6 - kurs NBP dla dowolnej daty | zakres 02.01.2002 … jutro, buforowanie braku kursu, limit pamięci podręcznej | #45 | D54 |
| SEC-7 - token KSeF bez limitu | 5 prób na firmę na 10 minut | #45 | D54 |
| SEC-8 - surowe komunikaty błędów | ogólne opisy dla użytkownika, szczegóły w logu | #45 | D54 |
