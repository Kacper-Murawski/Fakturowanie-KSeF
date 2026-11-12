# Raport: Obszar 07 - testy, odporność na błędy i ogólna jakość kodu
Status: W TOKU
Ostatnia aktualizacja: 2026-11-11 (start)

## Postęp
- [x] Kontekst: 00-zasady.md, 07-testy-i-jakosc.md, docs/testy.md
- [ ] Infrastruktura testów: ApiFactory, TestUsers, atrapy (FakeKsefGateway, FakeNbpClient, FakeVatWhiteList, FakeEmailSender), E2EFixture
- [ ] CI: .github/workflows/ci.yml
- [ ] Jakość testów: klasy testów integracyjnych i jednostkowych (asercje, kruchość, czas, Task.Delay)
- [ ] Jakość testów E2E: UserScenariosE2ETests (selektory, oczekiwania)
- [ ] Mapa pokrycia ryzyk (tabela)
- [ ] Obsługa błędów w kodzie produkcyjnym (catch, async void, .Result/.Wait, CancellationToken)
- [ ] Jakość ogólna: martwy kod, duplikaty, appsettings vs kod
- [ ] Build: liczba i rodzaje ostrzeżeń
- [ ] Uruchomienie `dotnet test` (na końcu - równolegle pracują obszary 04 i 06)
- [ ] Uruchomienie E2E (`E2E_TESTS=1`)
- [ ] Podsumowanie, sekcje końcowe, status ZAKOŃCZONY
Następny krok: przeczytać FakturowanieKSeF.Tests/Infrastructure/* i E2E/E2EFixture.cs.

## Znaleziska
(dopisywane na bieżąco)
