# Decyzje projektowe

Rejestr decyzji architektonicznych i technologicznych, prowadzony na bieżąco jako materiał
do pracy inżynierskiej. Każdy wpis: kontekst, decyzja, rozważane alternatywy, uzasadnienie, konsekwencje.

---

## D1. Aplikacja webowa na serwerze (SaaS) zamiast aplikacji lokalnej

**Kontekst:** system ma umożliwiać wystawianie i odbieranie faktur przez KSeF wielu niezależnym użytkownikom.

**Decyzja:** aplikacja webowa wdrażana centralnie na serwerze. Użytkownicy korzystają z niej przez przeglądarkę
po zalogowaniu na własne konto.

**Alternatywa:** aplikacja desktopowa instalowana na komputerze użytkownika, z lokalną bazą danych.

**Uzasadnienie:**
- brak instalacji i dostęp z dowolnego urządzenia z przeglądarką (komputer, tablet, telefon),
- jedna wersja programu: aktualizacje (np. zmiany schemy FA w KSeF) wdraża się raz, dla wszystkich,
- dane w centralnej bazie z kopiami zapasowymi, a nie na pojedynczym dysku użytkownika,
- serwer może w tle odpytywać KSeF o statusy, UPO i faktury przychodzące, także gdy użytkownik nie jest zalogowany.

**Konsekwencje:**
- konieczna izolacja danych między użytkownikami (każde zapytanie filtrowane po właścicielu),
- operator serwera odpowiada za bezpieczeństwo danych i tokenów KSeF (HTTPS, szyfrowanie sekretów, RODO),
- koszty utrzymania serwera i uzależnienie od dostępności serwera i internetu.

---

## D2. Stos technologiczny: .NET 8, Blazor WebAssembly, PostgreSQL

**Decyzja:** backend ASP.NET Core Web API (.NET 8 LTS), frontend Blazor WebAssembly, baza PostgreSQL 16, ORM EF Core 8.

**Uzasadnienie:**
- jeden język (C#) po stronie serwera i klienta, wspólny projekt modeli,
- .NET 8 ma długoterminowe wsparcie (LTS),
- PostgreSQL jest darmowy i open source, dobrze współpracuje z EF Core (Npgsql),
- uruchamianie bazy w Dockerze upraszcza development i wdrożenie.

---

## D3. Uwierzytelnianie: ASP.NET Core Identity z endpointami API

**Decyzja:** `AddIdentityApiEndpoints` + `MapIdentityApi`, gotowe endpointy `/register`, `/login`, `/refresh`,
tokeny Bearer. Użytkownicy przechowywani w tej samej bazie PostgreSQL.

**Alternatywy:** własna implementacja JWT; zewnętrzny dostawca tożsamości (Keycloak, Auth0, Entra ID).

**Uzasadnienie:** sprawdzone i bezpieczne mechanizmy (hashowanie haseł, blokada konta) bez zewnętrznej infrastruktury.

**Konsekwencje:** wbudowany `/register` przyjmuje tylko e-mail i hasło, więc imię i nazwisko (`FirstName`, `LastName`)
są opcjonalne i uzupełniane w profilu.
