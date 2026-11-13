# Raport: Obszar 01 - bezpieczeństwo aplikacji
Status: ZAKOŃCZONY
Ostatnia aktualizacja: 2026-11-11 (zakończenie przeglądu)

## Postęp
- [x] Program.cs + Infrastructure/* (nagłówki, limity, błędy, cache plików)
- [x] Uwierzytelnianie i sesja (ciasteczko, CSRF, MapIdentityApi, 2FA) - AccountEndpoints.cs
- [x] Limity żądań (RateLimits.cs, X-Forwarded-For)
- [x] Zaproszenia i role (MemberEndpoints.cs) - token zaproszenia
- [x] KsefSettingsEndpoints.cs + KsefTokenProtector.cs
- [x] XML z KSeF (Fa3InvoiceReader, Fa3SchemaValidator, UPO) - XXE/DoS
- [x] E-mail (Services/Email/*) - wstrzyknięcia nagłówków, linki
- [x] CSV / PDF / Content-Disposition / surowy SQL / MarkupString / JS
- [x] SSRF i KsefTestOnlyHandler (wszystkie HttpClienty, przekierowania), Integrations/*
- [x] DoS (rozmiary żądań, pageSize, eksport ZIP, endpointy anonimowe)
- [x] Sekrety w logach/odpowiedziach, konfiguracja produkcyjna (HSTS, CSP, AllowedHosts, Swagger)
- [x] Klient: Client/Services/*, index.html, js/*, service-worker.published.js
- [x] Dockerfile, deploy/*, .github/workflows/*, appsettings*.json
- [x] Zależności NuGet (podatności)
Następny krok: brak - przegląd zakończony.

## Podsumowanie

Podstawy są solidne: ciasteczko sesji `HttpOnly`/`SameSite=Strict`/`Secure`, CSP bez skryptów inline, brak surowego SQL
i `MarkupString` z danymi użytkownika, XML czytany bez DTD, token KSeF szyfrowany i nieobecny w odpowiedziach,
token zaproszenia 256-bitowy przechowywany jako skrót, obraz Docker bez uprawnień administratora i bez sekretów.
Nie znaleziono znalezisk krytycznych (wyciek cudzych danych, obejście autoryzacji).
Najpoważniejsze ryzyko: weryfikacja kodu TOTP w `/api/account/2fa/*` nie ma limitu prób, więc przejęta sesja może
odgadnąć kod i wyłączyć 2FA - wbrew założeniu D41 (potwierdzone testem). Dalej: przekierowanie po logowaniu
(`returnUrl`) da się skierować na obcą domenę, limit „auth” 10/min na IP obejmuje też sprawdzenie sesji przy każdym
starcie aplikacji (blokada użytkowników za wspólnym adresem), a brak potwierdzenia e-mail przy rejestracji czyni limity
„na firmę/na użytkownika” (poczta, wykaz VAT) łatwymi do obejścia.

Liczba znalezisk: krytyczne 0, wysokie 1, średnie 4, niskie 6.

## Znaleziska

### SEC-1. Brak limitu prób kodu TOTP przy wyłączaniu 2FA i generowaniu kodów odzyskiwania
- **Waga:** wysokie
- **Rodzaj:** bezpieczeństwo
- **Pewność:** potwierdzone (tymczasowy test integracyjny: 40 błędnych kodów na `/api/account/2fa/disable` i 40 na
  `/recovery-codes` → 80 × 400, bez blokady i bez 429; następnie poprawny kod → 204. Test usunięty po sprawdzeniu.)
- **Miejsce:** `FakturowanieKSeF.Api/Endpoints/AccountEndpoints.cs:49-83` (`/enable`, `/disable`, `/recovery-codes`),
  `:86-87` (`VerifyTotpAsync`); `FakturowanieKSeF.Api/Infrastructure/RateLimits.cs:44-50` (jedyny limit: 600/min na użytkownika)
- **Problem:** `VerifyTwoFactorTokenAsync` nie zlicza nieudanych prób (blokada konta działa tylko w `/api/auth/login`),
  a grupa `/api/account/2fa` nie ma limitu `auth`. D41 zakłada, że „przejęta sesja nie zdejmie zabezpieczenia” - zdejmie,
  po odgadnięciu 6-cyfrowego kodu.
- **Scenariusz:** atakujący z przejętą sesją (ciasteczko lub token Bearer) wysyła `POST /api/account/2fa/disable` z kolejnymi
  kodami w tempie 600/min. Identity akceptuje kody z okna ±2 kroki (5 ważnych kodów naraz), więc szansa na trafienie to
  ok. 0,3% na minutę: 50% po ok. 4 godzinach, a sesja (8 h, przesuwana) w tym czasie nie wygasa. Po trafieniu 2FA jest
  wyłączone, a klucz zresetowany. To samo dla `/recovery-codes` (atakujący dostaje nowe kody odzyskiwania).
- **Propozycja poprawki:** po nieudanej weryfikacji wywołać `users.AccessFailedAsync(user)` i odrzucać żądania, gdy
  `IsLockedOutAsync` (po sukcesie `ResetAccessFailedCountAsync`); dodatkowo osobna polityka limitu dla grupy
  `/account/2fa` partycjonowana po użytkowniku (np. 5/min). Rozważyć wymaganie hasła przy wyłączaniu 2FA.
- **Test, który to wykryje:** `TwoFactorTests.Wylaczenie_PoKilkuZlychKodach_BlokujeKolejneProby` - 6 błędnych kodów na
  `/disable`, potem poprawny kod musi zostać odrzucony (423/429) do czasu wygaśnięcia blokady.

### SEC-2. Przekierowanie po logowaniu na obcą domenę (`returnUrl`)
- **Waga:** średnie
- **Rodzaj:** bezpieczeństwo
- **Pewność:** potwierdzone analizą (filtr w `Login.razor`; `blazor.webassembly.js` 10.0.14: `navigateTo` dla adresu spoza
  bazy wykonuje `location.href = uri`; parser URL WHATWG sprawdzony w Node: `new URL("http:evil.com", "https://app/")` →
  `http://evil.com/`, `"\\\\evil.com"` i `"/\\evil.com"` → `https://evil.com/`). Nie uruchamiano w przeglądarce.
- **Miejsce:** `FakturowanieKSeF.Client/Pages/Login.razor:95-96`
- **Problem:** filtr odrzuca tylko wartości zawierające `://` lub zaczynające się od `//`. Przechodzą m.in. `http:evil.com`
  (inny schemat bez ukośników), `\\evil.com`, `/\evil.com` i `/<TAB>/evil.com`, które przeglądarka rozwiązuje jako adres
  bezwzględny. `docs/bezpieczenstwo.md` deklaruje, że `returnUrl` przyjmuje wyłącznie adresy względne.
- **Scenariusz:** ofiara dostaje link `https://aplikacja/login?returnUrl=%5C%5Cevil.example%2Flogin`, loguje się na
  prawdziwej stronie, po czym trafia na `https://evil.example/login` - kopię formularza z komunikatem „sesja wygasła”
  wyłudzającą hasło i kod 2FA. (`javascript:` blokuje CSP.)
- **Propozycja poprawki:** akceptować tylko ścieżkę aplikacji: `Navigation.ToAbsoluteUri(ReturnUrl)` musi mieć ten sam
  `Scheme`+`Authority` co `Navigation.BaseUri`, a wartość nie może zawierać `\`, znaków sterujących ani `:` przed pierwszym `/`;
  najprościej - biała lista znaków i wymóg, by nie zaczynała się od `/` ani `\`.
- **Test, który to wykryje:** test jednostkowy wydzielonej funkcji `SafeReturnUrl` dla wartości `http:evil.com`,
  `\\evil.com`, `/\evil.com`, `/\t/evil.com`, `//evil.com`, `https://evil.com` (wszystkie → `""`) oraz scenariusz E2E:
  logowanie z `returnUrl=%5C%5Cexample.com` kończy się na stronie aplikacji.

### SEC-3. Limit „auth” (10/min na IP) obejmuje sprawdzenie sesji przy każdym starcie aplikacji - blokada użytkowników za wspólnym adresem
- **Waga:** średnie
- **Rodzaj:** błąd / bezpieczeństwo (dostępność)
- **Pewność:** potwierdzone analizą kodu (grupa `/api/auth` w całości pod polityką `Auth`; klient woła `manage/info` przy każdym załadowaniu)
- **Miejsce:** `FakturowanieKSeF.Api/Program.cs:241-249` (`RequireRateLimiting(RateLimits.Auth)` na całej grupie `/auth`,
  łącznie z `manage/info`, `refresh`, `logout`); `FakturowanieKSeF.Api/Infrastructure/RateLimits.cs:40-42`;
  `FakturowanieKSeF.Client/Services/AuthService.cs:27` (`GET api/auth/manage/info` przy każdym starcie);
  `FakturowanieKSeF.Client/Components/CompanySwitcher.razor:29` i `Pages/Invitation.razor:65` (`forceLoad` → kolejne `manage/info`)
- **Problem:** pula 10 żądań/min jest wspólna dla wszystkich osób zza jednego adresu IP i zużywa ją nie tylko logowanie,
  ale każde odświeżenie strony zalogowanego użytkownika, przełączenie firmy, wylogowanie i podgląd zaproszenia.
  Odpowiedź 429 na `manage/info` klient traktuje jak brak sesji i pokazuje logowanie, które też dostaje 429.
- **Scenariusz:** biuro rachunkowe (kilka osób za jednym NAT) albo sala podczas prezentacji: 10 otwarć/odświeżeń aplikacji
  w ciągu minuty → kolejne osoby są „wylogowane” i nie mogą się zalogować. Wariant złośliwy: jedna osoba w tej samej sieci
  (lub CGNAT operatora) wysyła 10 żądań/min na `/api/auth/login` i trwale blokuje logowanie pozostałym.
- **Propozycja poprawki:** politykę `Auth` nałożyć tylko na `login`, `register`, `refresh`, `forgotPassword`, `resetPassword`,
  `resendConfirmationEmail` i zaproszenia; `manage/info` i `logout` zostawić pod limitem globalnym (na użytkownika).
  Dla logowania rozważyć partycję „IP + e-mail” obok limitu na IP z wyższym progiem.
- **Test, który to wykryje:** `RateLimitTests.SprawdzenieSesji_NieZuzywaLimituLogowania` - 15 × `GET /api/auth/manage/info`
  zalogowanego użytkownika, potem `POST /api/auth/login` z tego samego adresu nie zwraca 429.

### SEC-4. Aplikacja jako przekaźnik wiadomości: limit tylko „na firmę”, liczony bez blokady, konta bez potwierdzenia adresu
- **Waga:** średnie
- **Rodzaj:** bezpieczeństwo
- **Pewność:** potwierdzone analizą kodu (brak limitu globalnego/na IP/na konto; wyścig - do weryfikacji testem współbieżnym)
- **Miejsce:** `FakturowanieKSeF.Api/Services/Email/EmailOutbox.cs:52-70` (licznik `CountAsync` + `Add` bez transakcji/blokady);
  `FakturowanieKSeF.Api/Endpoints/EmailEndpoints.cs:18-43` (proforma nie wymaga KSeF, `request.Message` do 2000 znaków);
  `FakturowanieKSeF.Api/Endpoints/MemberEndpoints.cs:34-86` (zaproszenie na dowolny adres, nazwa firmy w temacie i treści);
  `FakturowanieKSeF.Api/Program.cs:25-34` (rejestracja bez potwierdzenia e-mail)
- **Problem:** dzienny limit 100 wiadomości dotyczy jednej firmy, a firmę może mieć każde nowe konto (rejestracja 10/min
  z IP, bez weryfikacji adresu). Treść jest w dużej części dowolna (wiadomość własna, nazwa firmy, numer faktury).
  Dodatkowo limit jest sprawdzany przed dodaniem rekordu, więc równoległe żądania go przekraczają.
- **Scenariusz:** skrypt rejestruje N kont, każde zakłada firmę o nazwie będącej treścią reklamy/phishingu, tworzy proformę
  i wysyła 100 wiadomości `POST /api/invoices/{id}/email` na dowolne adresy (plus zaproszenia) - N × 100 wiadomości dziennie
  z domeny aplikacji (SPF/DKIM aplikacji), co kończy się zablokowaniem nadawcy u dostawcy SMTP dla wszystkich firm.
  W obrębie jednej firmy 50 równoległych żądań przy liczniku 99 wysyła ok. 50 wiadomości ponad limit.
- **Propozycja poprawki:** wymagać potwierdzonego adresu e-mail konta przed wysyłką do osób trzecich (Identity to obsługuje);
  dodać limit globalny aplikacji i limit na konto/IP; licznik firmy zwiększać atomowo (np. `pg_advisory_xact_lock(companyId)`
  albo wiersz licznika z `UPDATE ... WHERE count < limit`). Niższy limit dla firm bez tokenu KSeF.
- **Test, który to wykryje:** `EmailApiTests.LimitDzienny_RownolegleZadania_NiePrzekraczajaLimitu` (20 równoległych żądań
  przy limicie 5 → dokładnie 5 × 202) oraz `LimitGlobalny_WieleFirm_Zatrzymuje`.

### SEC-5. Wspólny dzienny limit wykazu VAT (90 wyszukiwań) może wyczerpać kilka świeżo założonych kont
- **Waga:** średnie
- **Rodzaj:** bezpieczeństwo (dostępność funkcji dla wszystkich użytkowników)
- **Pewność:** potwierdzone analizą kodu (limity domyślne 90 łącznie / 20 na użytkownika, rejestracja bez weryfikacji)
- **Miejsce:** `FakturowanieKSeF.Api/Integrations/VatWhiteList/VatRegistryLookup.cs:92-120` (`VatRegistryLimiter`),
  `FakturowanieKSeF.Api/Endpoints/VatRegistryEndpoints.cs:17-40`
- **Problem:** ochrona „jeden użytkownik nie wyczerpie limitu pozostałym” (D28) opiera się na identyfikatorze konta,
  a konto zakłada się bez żadnej weryfikacji. Pięć kont × 20 różnych poprawnych NIP = cały dzienny limit aplikacji.
- **Scenariusz:** atakujący rejestruje 5 kont (pół minuty przy limicie 10/min), z każdego pyta `GET /api/vat-registry/{nip}`
  o 20 różnych numerów z poprawną sumą kontrolną. Do końca dnia wszyscy użytkownicy dostają „Dzienny limit zapytań aplikacji
  został wyczerpany”; to samo (2000/200) dla sprawdzania rachunków przy fakturach otrzymanych.
- **Propozycja poprawki:** limit dodatkowo na adres IP i na firmę; niższy limit dla kont młodszych niż np. doba lub bez
  firmy; rezerwa części puli dla kont z historią; docelowo potwierdzanie adresu e-mail przy rejestracji.
- **Test, który to wykryje:** `VatRegistryLimitTests.NoweKonta_NieWyczerpujaWspolnegoLimitu` - 5 nowych kont z jednego
  adresu nie może zużyć więcej niż ustalona część puli.

### SEC-6. Podgląd kursu NBP: dowolna data → nieograniczone zapytania wychodzące, rosnąca pamięć podręczna, błąd 500
- **Waga:** niskie
- **Rodzaj:** bezpieczeństwo / błąd
- **Pewność:** potwierdzone analizą kodu; 500 dla skrajnej daty - do weryfikacji testem
- **Miejsce:** `FakturowanieKSeF.Api/Endpoints/ExchangeRateEndpoints.cs:14-28`,
  `FakturowanieKSeF.Api/Integrations/Nbp/NbpExchangeRates.cs:72-86` (`ForTaxPointAsync`)
- **Problem:** parametr `date` nie ma zakresu. Każda nowa data to zapytanie do `api.nbp.pl` z adresu serwera i wpis
  w `IMemoryCache` na 12 h (bez limitu rozmiaru); wynik negatywny (brak kursu, data z przyszłości) nie jest buforowany.
  `taxPoint.AddDays(-14)` dla dat `0001-01-01…0001-01-14` rzuca `ArgumentOutOfRangeException` → 500.
- **Scenariusz:** zalogowany użytkownik pętlą `GET /api/exchange-rates/EUR?date=<kolejne dni>` generuje do 600 zapytań/min
  do NBP (ryzyko zablokowania adresu serwera - wtedy nie da się zapisać żadnej faktury walutowej, D43) i dziesiątki tysięcy
  wpisów w pamięci. `?date=0001-01-05` → 500.
- **Propozycja poprawki:** akceptować daty z zakresu np. [2002-01-02; dziś + 1 dzień], inaczej 400; buforować także brak
  kursu (krótko); klucz pamięci podręcznej po dacie notowania zamiast po dniu podatkowym; `SizeLimit` dla cache.
- **Test, który to wykryje:** `CurrencyTests.KursDlaDatySpozaZakresu_Zwraca400` (data 0001-01-05 i data za rok).

### SEC-7. Weryfikacja i generowanie tokenu KSeF bez ograniczenia częstotliwości
- **Waga:** niskie
- **Rodzaj:** bezpieczeństwo (limity API KSeF)
- **Pewność:** potwierdzone analizą kodu (brak ograniczenia poza globalnym 600/min na użytkownika)
- **Miejsce:** `FakturowanieKSeF.Api/Endpoints/KsefSettingsEndpoints.cs:27-71` (`PUT /company/ksef/token`, `POST /company/ksef/test-token`)
- **Problem:** każde żądanie wykonuje pełne uwierzytelnienie w KSeF (kilka wywołań API) z adresu serwera. Ręczna
  synchronizacja ma limit „raz na minutę”, te endpointy - nie (por. KSEF-9 w raporcie obszaru 03 o limitach godzinowych).
- **Scenariusz:** właściciel firmy (dowolne konto) w pętli wysyła `PUT /api/company/ksef/token` z losowym tokenem → setki
  prób uwierzytelnienia na minutę z IP serwera; KSeF zaczyna odpowiadać 429 całej aplikacji (wysyłka i odbiór faktur innych firm).
- **Propozycja poprawki:** limit na firmę/użytkownika (np. 5 prób na 10 minut) dla obu endpointów, 429 z `Retry-After`.
- **Test, który to wykryje:** `KsefSettingsTests.WeryfikacjaTokenu_PoKilkuProbach_Zwraca429` (z `FakeKsefGateway` liczącym wywołania).

### SEC-8. Surowe komunikaty wyjątków infrastruktury w odpowiedziach API
- **Waga:** niskie
- **Rodzaj:** bezpieczeństwo (ujawnienie informacji)
- **Pewność:** potwierdzone analizą kodu
- **Miejsce:** `FakturowanieKSeF.Api/Services/Email/EmailOutbox.cs:98` (`LastError = ex.Message`) → `EmailOutbox.ToDto` →
  `GET /api/invoices/{id}/emails`; `FakturowanieKSeF.Api/Endpoints/KsefSettingsEndpoints.cs:60-63` (`detail: ex.Message` dla `HttpRequestException`)
- **Problem:** treść wyjątku SMTP/sieci (nazwa i port serwera pocztowego, odpowiedź serwera na uwierzytelnienie, adresy
  wewnętrzne, komunikaty TLS) trafia do użytkownika. Hasło SMTP nie jest w komunikatach MailKit, ale szczegóły konfiguracji - tak.
- **Scenariusz:** przy błędnej konfiguracji SMTP każdy użytkownik widzi w historii wiadomości np.
  „535 5.7.8 Authentication failed for user faktury@…” albo „Connection refused (smtp.wewn.local:587)”.
- **Propozycja poprawki:** do bazy/DTO zapisywać komunikat ogólny („Serwer poczty odrzucił wiadomość”, „Brak połączenia
  z serwerem poczty”) zależny od typu wyjątku; szczegóły tylko w logu serwera. Analogicznie dla `test-token`.
- **Test, który to wykryje:** `EmailTests.BladWysylki_NieUjawniaSzczegolowSerwera` - atrapa nadawcy rzuca wyjątek z nazwą
  hosta, DTO nie zawiera tej nazwy.

### SEC-9. Token zaproszenia w adresie URL trafia do logów proxy
- **Waga:** niskie
- **Rodzaj:** bezpieczeństwo
- **Pewność:** potwierdzone analizą konfiguracji (Caddy loguje pełny URI żądania)
- **Miejsce:** `deploy/Caddyfile:6-9` (`log`), `FakturowanieKSeF.Api/Endpoints/MemberEndpoints.cs:62` (link `/zaproszenie?token=…`),
  `:158` (`GET /api/account/invitations/{token}`)
- **Problem:** w bazie jest tylko skrót tokenu („wyciek bazy nie pozwala przyjąć zaproszenia”), ale ten sam token w postaci
  jawnej zapisują logi dostępu proxy - przy otwarciu linku i przy podglądzie zaproszenia. `Referrer-Policy` chroni przed
  wyciekiem do innych serwisów, nie przed logami.
- **Scenariusz:** osoba z dostępem do logów (`docker compose logs proxy`, kopie logów) odczytuje nieprzyjęty token; ponieważ
  rejestracja nie weryfikuje adresu, a zapraszany często nie ma jeszcze konta, zakłada konto na zapraszany adres i przyjmuje
  zaproszenie (dostęp do faktur firmy).
- **Propozycja poprawki:** podgląd zaproszenia metodą POST z tokenem w treści; token w linku we fragmencie (`/zaproszenie#token=…`
  - fragment nie jest wysyłany do serwera) albo filtr logów Caddy (`log { format filter { request>uri query { delete token } } }`).
- **Test, który to wykryje:** test E2E/hostingowy: po otwarciu linku zaproszenia żadne żądanie do serwera nie zawiera tokenu
  w ścieżce ani w query (przechwycenie żądań w Playwright).

### SEC-10. Endpointy Identity wystawione bez działającej poczty: brak resetu hasła, a po podpięciu nadawcy - zmiana e-maila bez hasła
- **Waga:** niskie
- **Rodzaj:** bezpieczeństwo / błąd
- **Pewność:** potwierdzone analizą kodu (w projekcie nie ma rejestracji `IEmailSender<ApplicationUser>` - Identity używa nadawcy pustego)
- **Miejsce:** `FakturowanieKSeF.Api/Program.cs:240-242` (`MapIdentityApi`), brak `AddTransient<IEmailSender<ApplicationUser>>`
- **Problem:** `forgotPassword`, `resendConfirmationEmail` i zmiana adresu w `POST manage/info` odpowiadają 200, ale nic nie
  wysyłają - użytkownik, który zapomniał hasła (albo zgubił telefon i kody 2FA), nie ma żadnej ścieżki odzyskania konta.
  Jednocześnie wbudowana zmiana adresu e-mail nie wymaga hasła ani kodu 2FA: gdy tylko ktoś podepnie nadawcę (naturalna
  poprawka braku resetu), przejęta sesja pozwoli zmienić adres konta, potem zresetować hasło i - bez 2FA - trwale przejąć konto;
  zmiana adresu zmienia też to, które zaproszenia konto może przyjąć.
- **Scenariusz:** dziś: `POST /api/auth/forgotPassword` → 200 i brak wiadomości. Po podpięciu nadawcy: skradziona sesja →
  `POST /api/auth/manage/info {"newEmail":"atakujacy@x"}` → link potwierdzający na adres atakującego → reset hasła.
- **Propozycja poprawki:** świadomie zdecydować: albo zablokować nieużywane ścieżki (`forgotPassword`, `resetPassword`,
  `confirmEmail`, `resendConfirmationEmail`, zmiana e-maila) tak jak `manage/2fa`, albo wdrożyć reset hasła przez `EmailOutbox`
  i własny endpoint zmiany adresu wymagający hasła (i kodu 2FA) z powiadomieniem na stary adres.
- **Test, który to wykryje:** `AuthTests.ZmianaEmaila_BezHasla_JestOdrzucana` oraz `ResetHasla_WysylaWiadomosc` (albo test,
  że zablokowane ścieżki zwracają 404).

### SEC-11. HSTS z ustawieniami domyślnymi (30 dni, bez subdomen)
- **Waga:** niskie
- **Rodzaj:** bezpieczeństwo (konfiguracja)
- **Pewność:** potwierdzone analizą kodu (brak `AddHsts` z opcjami; Caddy sam nie dodaje HSTS)
- **Miejsce:** `FakturowanieKSeF.Api/Program.cs:203-204`
- **Problem:** `UseHsts()` bez konfiguracji wysyła `max-age=2592000` bez `includeSubDomains` - krótka ochrona przed
  obniżeniem do HTTP po dłuższej przerwie w używaniu aplikacji.
- **Scenariusz:** użytkownik wraca po ponad 30 dniach w sieci kontrolowanej przez atakującego, wpisuje adres bez `https://` -
  pierwsze żądanie HTTP można przechwycić (ciasteczko `Secure` nie wycieknie, ale stronę logowania da się podmienić).
- **Propozycja poprawki:** `builder.Services.AddHsts(o => { o.MaxAge = TimeSpan.FromDays(365); o.IncludeSubDomains = true; })`.
- **Test, który to wykryje:** `SecurityHeadersTests.Hsts_MaRocznyOkres` (środowisko Production, żądanie z `X-Forwarded-Proto: https`).

## Sprawdzone bez uwag

- **Ciasteczko sesji i CSRF:** `ksef.auth` - `HttpOnly`, `SameSite=Strict`, `Secure` poza Development, 8 h; `ksef.company` -
  te same flagi, a wartość jest tylko wskazaniem weryfikowanym przez `CurrentCompany.AccessAsync`. API przyjmuje JSON, brak CORS.
  Przełączenie firmy i przyjęcie zaproszenia nie są podatne na CSRF.
- **Logowanie z 2FA:** `/api/auth/login` zlicza błędne hasła i błędne kody do blokady konta; blokada wbudowanego
  `/api/auth/manage/2fa` (`StartsWithSegments`, bez rozróżniania wielkości liter, przed routingiem) jest szczelna.
- **X-Forwarded-For:** `ForwardLimit = 1` (brany jest ostatni wpis, dopisany przez proxy), Caddy domyślnie nie ufa nagłówkowi
  od klienta, aplikacja nie ma wystawionego portu - limitu nie da się ominąć nagłówkiem. (Limit na pełny adres IPv6 można
  obejść pulą /64 - akceptowalne przy blokadzie konta.)
- **Zaproszenia:** 32 bajty z `RandomNumberGenerator`, w bazie skrót SHA-256 z unikalnym indeksem, ważność 7 dni,
  jednorazowość (`AcceptedAt`), odwołanie, wiązanie z adresem konta, rola `Owner` odrzucana przy zaproszeniu i zmianie roli;
  równoczesne przyjęcie zatrzymuje unikalny indeks `(CompanyId, UserId)`; podgląd i przyjęcie pod limitem `auth`.
- **XML:** `Fa3InvoiceReader` i `Fa3SchemaValidator` - `DtdProcessing.Prohibit`, `XmlResolver = null` (bez XXE i „billion
  laughs”); resolver schematów dopuszcza tylko trzy osadzone pliki. Dane z XML są wyświetlane przez Blazor (kodowanie) i jako
  tekst w PDF.
- **E-mail - nagłówki:** temat i nazwa nadawcy czyszczone ze znaków sterujących, adresy przez `MailboxAddress.Parse`,
  treść wyłącznie tekstowa; nadawcą jest adres aplikacji.
- **CSV:** `CsvBuilder.Escape` poprzedza apostrofem pola zaczynające się od `=`, `+`, `-`, `@`, tabulatora i CR (poza
  liczbami ujemnymi), poprawnie cytuje `;`, `"` i końce linii.
- **Pliki:** nazwy w `Content-Disposition` budowane z liter/cyfr (`InvoiceFiles.FileName`, `SafeName`) i kodowane przez
  `Results.File`; nazwy wpisów ZIP bez separatorów ścieżek z danych użytkownika.
- **Wstrzyknięcia:** brak `FromSql`/`ExecuteSql`; jedyny `MarkupString` to stałe ścieżki ikon (`Icon.razor`); JS bez `eval`
  i `innerHTML`; `ILIKE` z parametrami.
- **SSRF:** adresy NBP, wykazu VAT i KSeF są stałymi w kodzie, SMTP i `App:PublicUrl` z konfiguracji serwera; parametry
  użytkownika trafiają do ścieżek przez `Uri.EscapeDataString` po walidacji (NIP, kod waluty z białej listy).
- **KsefTestOnlyHandler:** jedyny klient HTTP do KSeF to `IRestClient` biblioteki MF i ma dopięty handler; fabryka
  `KSeF.Client.ClientFactory` (z klientem „Prod”) nie jest referencjonowana. Luka z przekierowaniami HTTP jest już opisana
  jako **KSEF-17** w raporcie obszaru 03 - nie powielam.
- **DoS:** `pageSize` ograniczony do 100, listy kontrahentów/produktów do 500/1000, pozycje faktury do 1000, eksport do 366
  dni (budowanie paczki w pamięci opisane jako **PERF-2** w obszarze 05); jedyne endpointy anonimowe to `/api/auth/*`
  (limit `auth`) i `/api/health` (limit globalny na IP).
- **Sekrety:** token KSeF nie pojawia się w DTO ani w logach (`KsefSettingsDto` zwraca tylko flagę i datę); hasło SMTP
  i certyfikatu tylko ze zmiennych środowiskowych; `ExceptionHandler` nie dopisuje szczegółów wyjątków; Swagger tylko
  w Development; produkcyjny start z hasłem deweloperskim jest blokowany.
- **CSP i nagłówki:** `script-src 'self' 'wasm-unsafe-eval'`, `frame-ancestors 'none'`, `Referrer-Policy: no-referrer`,
  `nosniff`; `'unsafe-inline'` tylko w `style-src` (uzasadnione atrybutami `style`, D47).
- **Klient:** brak tokenów w `localStorage` (tylko motyw); service worker pomija `/api/` i sprawdza skróty SHA-256 plików.
- **Docker/CI:** `.dockerignore` obecny, obraz uruchamiany jako `$APP_UID`, sekrety spoza obrazu (`.env`, wolumen
  `secrets` tylko do odczytu), baza i aplikacja bez wystawionych portów, `AllowedHosts` = domena; CI nie używa sekretów
  repozytorium (tylko hasło deweloperskie usługi testowej).
- **Zależności:** `dotnet list FakturowanieKSeF.Api package --vulnerable --include-transitive` → brak podatnych pakietów
  (w grafie aplikacji `System.Security.Cryptography.Pkcs/Xml` = 10.0.14 wymuszone w `FakturowanieKSeF.Ksef.csproj`).
  Ostrzeżenia NU1902/NU1903 pochodzą z odtwarzania wielocelowego projektu submodułu (net8/net9 i wersja 10.0.10) i nie
  dotyczą wdrażanych bibliotek; można je wyciszyć bez zmiany submodułu przez `Directory.Build.props` w katalogu głównym
  z `NuGetAuditSuppress` dla tych doradztw albo centralne przypięcie wersji.

## Poza obszarem

- **HEALTHCHECK kontenera a `AllowedHosts` (do weryfikacji uruchomieniem):** `Dockerfile:19-20` pyta o `/api/health`
  z nagłówkiem `Host: localhost`, a `deploy/docker-compose.prod.yml:38` ustawia `AllowedHosts` na domenę - filtr hostów
  ASP.NET Core odpowie 400, więc kontener będzie stale oznaczony `unhealthy` (aplikacja działa, ale stan w `docker ps`
  i ewentualne automaty oparte na zdrowiu są mylące). Poprawka: `AllowedHosts: "${DOMAIN};localhost"` albo nagłówek
  `Host` z domeną w kontroli stanu.

## Pytania do autora

1. Czy brak resetu hasła (SEC-10) jest świadomy? Jeśli tak - czy nieużywane ścieżki Identity mają zostać zablokowane?
2. Czy logowanie tokenem Bearer (D18) ma pozostać dostępne w produkcji? Tokeny odświeżania żyją 14 dni i nie są
   unieważniane przez `/api/auth/logout` (wylogowanie usuwa tylko ciasteczko).
3. Czy planowane jest potwierdzanie adresu e-mail przy rejestracji? Zamyka ono naraz SEC-4, SEC-5 i scenariusz z SEC-9.
4. Czy wyłączenie 2FA powinno dodatkowo wymagać hasła (SEC-1)?
