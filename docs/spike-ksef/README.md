# Spike KSeF: wyniki prototypu (etap 1)

**Data:** 2026-11-01 • **Środowisko:** TEST (`https://api-test.ksef.mf.gov.pl`) • **Klient:** oficjalny klient MF 2.9.0
• **Kod:** `tools/FakturowanieKSeF.KsefSpike`

## Cel
Potwierdzić przed budową właściwej aplikacji, że da się przejść cały przepływ wystawienia faktury w KSeF 2.0:
uwierzytelnienie, wysyłkę zaszyfrowanej faktury FA(3), odbiór numeru KSeF i UPO.

## Wynik: sukces

| Krok | Czas od startu | Wynik |
|---|---|---|
| Pobranie certyfikatów publicznych MF | 00:00 | OK |
| Losowy NIP testowy + samopodpisany certyfikat | 00:01 | NIP `9767751984` |
| Uwierzytelnienie (wyzwanie → podpis XAdES → status → token JWT) | 00:03 | OK, operacja `20261101-AU-1F71BC6000-E6FB701804-28` |
| Otwarcie sesji interaktywnej (AES-256, klucz szyfrowany RSA-OAEP kluczem MF) | 00:03 | sesja `20261101-SO-1F72210000-47B0A23594-4B` |
| Wysłanie zaszyfrowanej faktury FA(3) | 00:04 | ref. `20261101-EE-1F722FA000-47B4FF55FB-C7` |
| Przetworzenie faktury | 00:05 | status 200 „Sukces” |
| **Numer KSeF** | 00:05 | **`9767751984-20261101-476E8D000000-0D`** |
| Zamknięcie sesji i pobranie UPO | 00:07 | OK ([`upo.xml`](upo.xml)) |

Pliki: [`faktura-wyslana.xml`](faktura-wyslana.xml) (faktura w postaci jawnej przed zaszyfrowaniem),
[`upo.xml`](upo.xml) (UPO podpisane przez MF), [`podsumowanie.txt`](podsumowanie.txt).

UPO potwierdza środowisko: `<NazwaPodmiotuPrzyjmujacego>Ministerstwo Finansów - środowisko testowe (TE)</NazwaPodmiotuPrzyjmujacego>`.
Wszystkie dane są testowe (losowy NIP, fikcyjni kontrahenci) i nie mają mocy prawnej.

## Przebieg komunikacji

```
Aplikacja                                   KSeF (TEST)
   |-- GET  certyfikaty publiczne MF ----------->|
   |-- POST /auth/challenge -------------------->|  wyzwanie
   |-- POST /auth/xades-signature (podpis) ----->|  numer operacji + token tymczasowy
   |-- GET  /auth/{ref} (aż kod != 100) -------->|  200 = uwierzytelniono
   |-- POST /auth/token/redeem ----------------->|  accessToken (JWT) + refreshToken
   |-- POST /sessions/online (klucz AES zaszyfrowany kluczem MF) -> numer sesji
   |-- POST /sessions/online/{ref}/invoices ---->|  faktura zaszyfrowana AES-256 + skróty SHA-256
   |-- GET  .../invoices/{ref} (aż kod != 150) ->|  200 + numer KSeF
   |-- POST /sessions/online/{ref}/close ------->|
   |-- GET  .../invoices/ksef/{nrKSeF}/upo ----->|  UPO (XML podpisany przez MF)
```

## Wnioski dla dalszych etapów

1. **Model danych (etap 2).** Faktura w bazie musi przechowywać: numer referencyjny sesji, numer referencyjny
   faktury w KSeF, numer KSeF, datę nadania numeru KSeF, skrót SHA-256 dokumentu (potrzebny do kodu QR
   weryfikacji w etapie 8), kod i opis statusu (wraz ze szczegółami błędów walidacji) oraz XML UPO.
   Trzeba też przechowywać dokładnie ten XML, który został wysłany: to on jest fakturą, a nie rekord w bazie.
2. **Statusy są asynchroniczne.** KSeF przetwarza fakturę w tle (kod 150 → 200 lub błąd). W aplikacji status
   będzie odpytywany przez usługę działającą w tle (etap 6), a użytkownik zobaczy stan „wysłana, oczekuje”.
3. **Uwierzytelnianie użytkowników.** Samopodpisany certyfikat działa tylko na środowisku TEST. Docelowo
   użytkownik poda **token KSeF** wygenerowany w Aplikacji Podatnika (uwierzytelnianie tokenem: token
   szyfrowany kluczem publicznym MF). Token jest sekretem i musi być przechowywany zaszyfrowany (etap 6).
   Token dostępu (JWT) jest krótkotrwały i odświeżany tokenem odświeżania.
4. **Wydajność.** Cały przepływ trwał ok. 7 s, w tym nadanie numeru KSeF ok. 1 s po wysłaniu.
5. **Środowisko TEST jest współdzielone** między integratorami i okresowo czyszczone, więc dane testowe
   nie mogą być traktowane jako trwałe (np. UPO trzeba zapisać u siebie od razu).
6. **Bezpieczeństwo.** Wszystkie żądania przeszły przez `KsefTestOnlyHandler`. Środowisko deweloperskie
   ma w polityce sieci dopuszczony wyłącznie host testowy KSeF.
