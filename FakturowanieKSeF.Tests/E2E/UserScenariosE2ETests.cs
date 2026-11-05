using System.Text.RegularExpressions;
using FakturowanieKSeF.Ksef;
using FakturowanieKSeF.Tests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace FakturowanieKSeF.Tests.E2E;

/// <summary>
/// Testy E2E głównych scenariuszy użytkownika w prawdziwej przeglądarce (Chromium, ustawienia pl-PL):
/// cały stos - interfejs Blazor, API, baza PostgreSQL, usługi w tle; KSeF zastąpiony atrapą.
/// </summary>
[Collection("E2E")]
public class UserScenariosE2ETests(E2EFixture fx)
{
    // ------------------------------------------------------------------ kroki wielokrotnego użytku

    private static async Task RegisterAsync(IPage page)
    {
        await page.GotoAsync("/register");
        await page.FillAsync("#email", $"e2e_{Guid.NewGuid():N}@example.com");
        await page.FillAsync("#password", "Haslo123!");
        await page.FillAsync("#password2", "Haslo123!");
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(new Regex("/firma$"));
    }

    private static async Task CreateCompanyAsync(IPage page, string nip)
    {
        await page.FillAsync("#name", "Pracownia Testowa E2E");
        await page.FillAsync("#nip", nip);
        await page.FillAsync("#company-street", "ul. Testowa 1");
        await page.FillAsync("#company-postal", "00-001");
        await page.FillAsync("#company-city", "Warszawa");
        await page.FillAsync("#bank", "61 1090 1014 0000 0712 1981 2874");
        await page.ClickAsync("button[type=submit]");
        await Expect(page.Locator(".alert-success")).ToContainTextAsync("Zapisano dane firmy");
    }

    private static async Task<string> NewUserWithCompanyAsync(IPage page)
    {
        var nip = TestUsers.RandomNip();
        await RegisterAsync(page);
        await CreateCompanyAsync(page, nip);
        return nip;
    }

    private static ILocator Row(IPage page, int index) => page.Locator(".items-table tbody tr").Nth(index);

    private static async Task FillItemAsync(IPage page, int index, string name, string qty, string price, string? vatRate = null)
    {
        var row = Row(page, index);
        await row.Locator("input[aria-label^='Nazwa pozycji']").FillAsync(name);
        await row.Locator("input[aria-label='Ilość']").FillAsync(qty);
        await row.Locator("input[aria-label='Cena netto']").FillAsync(price);
        if (vatRate is not null) await row.Locator("select[aria-label='Stawka VAT']").SelectOptionAsync(vatRate);
        await row.Locator("input[aria-label='Cena netto']").PressAsync("Tab");
    }

    private static async Task FillManualBuyerAsync(IPage page, string name, string? nip = null)
    {
        await page.FillAsync("#buyer-name", name);
        if (nip is not null) await page.FillAsync("#buyer-nip", nip);
        await page.FillAsync("#buyer-street", "ul. Leśna 3");
        await page.FillAsync("#buyer-postal", "80-001");
        await page.FillAsync("#buyer-city", "Gdańsk");
    }

    // ------------------------------------------------------------------ scenariusze

    [E2EFact]
    public async Task NiezalogowanyUzytkownik_JestPrzekierowanyNaLogowanie_IWracaPoZalogowaniu()
    {
        var page = await fx.NewPageAsync();
        await page.GotoAsync("/faktury");
        await page.WaitForURLAsync(new Regex("/login\\?returnUrl=faktury"));

        // Rejestracja ze słabym hasłem - polskie komunikaty
        await page.GotoAsync("/register");
        await page.FillAsync("#email", $"e2e_{Guid.NewGuid():N}@example.com");
        await page.FillAsync("#password", "haslohaslo");
        await page.FillAsync("#password2", "haslohaslo");
        await page.ClickAsync("button[type=submit]");
        await Expect(page.Locator(".alert-danger")).ToContainTextAsync("Hasło musi zawierać cyfrę");
    }

    [E2EFact]
    public async Task GlownyScenariusz_Firma_Kontrahent_Faktura_Edycja_Lista_Wylogowanie()
    {
        var page = await fx.NewPageAsync();
        await RegisterAsync(page);

        // Walidacja w przeglądarce (NIP z sumą kontrolną, format kodu pocztowego)
        await page.FillAsync("#name", "Pracownia Testowa E2E");
        await page.FillAsync("#nip", "1234567890");
        await page.FillAsync("#company-street", "ul. Testowa 1");
        await page.FillAsync("#company-city", "Warszawa");
        await page.FillAsync("#company-postal", "00001");
        await page.ClickAsync("button[type=submit]");
        await Expect(page.GetByText("Nieprawidłowy numer NIP.")).ToBeVisibleAsync();
        await Expect(page.GetByText("Polski kod pocztowy ma format 00-000.")).ToBeVisibleAsync();
        await CreateCompanyAsync(page, TestUsers.RandomNip());

        // Kontrahent
        await page.GotoAsync("/kontrahenci/nowy");
        await page.FillAsync("#name", "Kontrahent E2E S.A.");
        await page.FillAsync("#nip", "1234563218");
        await page.FillAsync("#customer-street", "ul. Polna 5");
        await page.FillAsync("#customer-postal", "30-001");
        await page.FillAsync("#customer-city", "Kraków");
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(new Regex("/kontrahenci$"));
        await Expect(page.Locator("tbody")).ToContainTextAsync("Kontrahent E2E S.A.");

        // Faktura z sumami liczonymi na żywo
        await page.GotoAsync("/faktury/nowa");
        await page.SelectOptionAsync("#customer", new SelectOptionValue { Label = "Kontrahent E2E S.A. (NIP 123-456-32-18)" });
        await FillItemAsync(page, 0, "Usługa programistyczna", "10", "150");
        await page.ClickAsync("text=+ Dodaj pozycję");
        await FillItemAsync(page, 1, "Książka", "1", "50", "Rate5");
        await Expect(page.Locator("[data-testid=total-gross]")).ToContainTextAsync("1 897,50");

        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(new Regex("/faktury/\\d+$"));
        await Expect(page.Locator("h1")).ToContainTextAsync("FV/");
        await Expect(page.Locator(".fs-5.text-end")).ToContainTextAsync("1 897,50 PLN");

        // Edycja: usunięcie pozycji
        await page.ClickAsync("text=Edytuj");
        await Row(page, 1).Locator("button[title='Usuń pozycję']").ClickAsync();
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(new Regex("/faktury/\\d+$"));
        await Expect(page.Locator(".fs-5.text-end")).ToContainTextAsync("1 845,00 PLN");

        // Lista i wyszukiwanie (filtr w adresie strony)
        await page.GotoAsync("/faktury");
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(1);
        await page.FillAsync("#q", "nie-ma-takiej");
        await page.WaitForURLAsync(new Regex("q=nie-ma-takiej"));
        await Expect(page.GetByText("Brak faktur spełniających kryteria.")).ToBeVisibleAsync();

        // Wylogowanie - strony chronione niedostępne
        await page.ClickAsync("text=Wyloguj");
        await page.WaitForURLAsync(new Regex("/login"));
        await page.GotoAsync("/faktury");
        await page.WaitForURLAsync(new Regex("/login"));
    }

    /// <summary>
    /// Regresja D20 (walidacja ilości w kulturze pl-PL) i D22 (poprawienie pola i natychmiastowy zapis):
    /// oba błędy były niewidoczne w testach API.
    /// </summary>
    [E2EFact]
    public async Task Regresja_WalidacjaWKulturzePl_IZapisPoPoprawieniuPola()
    {
        var page = await fx.NewPageAsync();
        await NewUserWithCompanyAsync(page);

        await page.GotoAsync("/faktury/nowa");
        await FillManualBuyerAsync(page, "Jan Kowalski");
        await FillItemAsync(page, 0, "Zabieg", "0", "200", "Exempt");

        await page.ClickAsync("button[type=submit]");
        await Expect(page.GetByText("Ilość musi być większa od zera.").First).ToBeVisibleAsync();
        await Expect(page.GetByText("Podaj podstawę prawną zwolnienia z VAT").First).ToBeVisibleAsync();

        // Poprawienie pól i od razu „Zapisz” (bez opuszczania pola) - zapis musi się wykonać
        await Row(page, 0).Locator("input[aria-label='Ilość']").FillAsync("1,5".Replace(',', '.'));
        await page.FillAsync("#exemption", "art. 43 ust. 1 pkt 19 ustawy o VAT");
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(new Regex("/faktury/\\d+$"));
        await Expect(page.GetByText("Podstawa zwolnienia z VAT:")).ToBeVisibleAsync();
    }

    [E2EFact]
    public async Task WysylkaDoKsef_NumerKsef_Upo_IPdfZKodemQr()
    {
        var page = await fx.NewPageAsync();
        var nip = await NewUserWithCompanyAsync(page);

        await page.ClickAsync("text=Wygeneruj token testowy");
        await Expect(page.GetByText("Wygenerowano token w testowym KSeF.")).ToBeVisibleAsync();

        await page.GotoAsync("/faktury/nowa");
        await FillManualBuyerAsync(page, "Kontrahent Testowy S.A.", "1234563218");
        await FillItemAsync(page, 0, "Usługa programistyczna", "12", "160");
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(new Regex("/faktury/\\d+$"));

        await page.ClickAsync("text=Wyślij do KSeF");
        // Usługa w tle (atrapa KSeF) przetwarza wysyłkę; strona odświeża status automatycznie
        await Expect(page.Locator("[data-testid=status-badge]")).ToContainTextAsync("Przyjęta w KSeF", new() { Timeout = 60_000 });
        await Expect(page.GetByText("Pobierz UPO")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(page.Locator("h1 + span + span")).ToContainTextAsync(nip + "-");

        var upo = await page.RunAndWaitForDownloadAsync(() => page.ClickAsync("text=Pobierz UPO"));
        Assert.StartsWith("UPO_" + nip, upo.SuggestedFilename);

        var pdf = await page.RunAndWaitForDownloadAsync(() => page.ClickAsync("text=Pobierz PDF"));
        var path = await pdf.PathAsync();
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path!)[..4]));

        // Faktura przyjęta jest nienaruszalna
        await Expect(page.GetByText("Edytuj")).Not.ToBeVisibleAsync();

        // Rozliczenie: wpłata częściowa (brutto 2 361,60), potem „Oznacz jako opłaconą”
        var panel = page.Locator("[data-testid=payment-panel]");
        await Expect(panel.Locator("[data-testid=payment-badge]")).ToHaveTextAsync("Nieopłacona");
        await panel.Locator("#paid-amount").FillAsync("1000.50"); // pole liczbowe: wartość w formacie HTML (kropka)
        await panel.Locator("#paid-note").FillAsync("zaliczka");
        await panel.Locator("button:has-text('Zarejestruj')").ClickAsync();
        await Expect(panel.Locator("[data-testid=payment-badge]")).ToHaveTextAsync("Częściowo opłacona");
        await Expect(panel.Locator("[data-testid=remaining]")).ToContainTextAsync("1 361,10");
        await E2EFixture.ScreenshotAsync(page, "17-rozliczenie-faktury");

        // Nadpłata - komunikat przy polu kwoty
        await panel.Locator("#paid-amount").FillAsync("5000");
        await panel.Locator("button:has-text('Zarejestruj')").ClickAsync();
        await Expect(panel.GetByText("Kwota przekracza pozostałą do zapłaty")).ToBeVisibleAsync();

        await panel.Locator("button:has-text('Oznacz jako opłaconą')").ClickAsync();
        await Expect(panel.Locator("[data-testid=payment-badge]")).ToHaveTextAsync("Opłacona");
        await Expect(panel.Locator("tbody tr")).ToHaveCountAsync(2);

        // Lista z filtrem rozliczenia i pulpit (brak należności)
        await page.GotoAsync("/faktury?payment=Paid");
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(1);
        await Expect(page.Locator("tbody [data-testid=payment-badge]")).ToHaveTextAsync("Opłacona");
        await page.GotoAsync("/faktury?payment=Unpaid");
        await Expect(page.GetByText("Brak faktur spełniających kryteria.")).ToBeVisibleAsync();
        await page.GotoAsync("/");
        await Expect(page.Locator("[data-testid=receivables]")).ToContainTextAsync("0,00 PLN");
    }

    [E2EFact]
    public async Task FakturyOtrzymane_PobranieZKsef_ListaIPodglad()
    {
        var page = await fx.NewPageAsync();
        var nip = await NewUserWithCompanyAsync(page);
        await page.ClickAsync("text=Wygeneruj token testowy");
        await Expect(page.GetByText("Wygenerowano token w testowym KSeF.")).ToBeVisibleAsync();

        // Inny podatnik wystawił fakturę na nasz NIP (atrapa KSeF)
        var stored = DateTimeOffset.UtcNow.AddMinutes(-5);
        var meta = new KsefInvoiceMetadata($"1234563218-20261030-E2E{nip[..6]}-01", "FV/E2E/10/2026", new DateOnly(2026, 10, 30),
            stored, stored, "1234563218", "Hurtownia Dostawca sp. z o.o.", 115m, 19.25m, 134.25m, "PLN");
        lock (fx.App.Ksef.Received) fx.App.Ksef.Received[nip] = [(meta, ReceivedInvoicesTests.ForeignInvoiceXml("1234563218", nip, "FV/E2E/10/2026", 3))];
        fx.App.Ksef.Hwm = DateTimeOffset.UtcNow;

        await page.ClickAsync("text=Faktury otrzymane");
        await page.ClickAsync("button:has-text('Pobierz z KSeF')");
        await Expect(page.Locator(".alert-success")).ToContainTextAsync("Pobrano nowe faktury: 1");
        await Expect(page.Locator("tbody")).ToContainTextAsync("Hurtownia Dostawca sp. z o.o.");

        await page.ClickAsync("tbody tr a");
        await Expect(page.Locator("h1")).ToContainTextAsync("FV/E2E/10/2026");
        await Expect(page.Locator(".invoice-sheet")).ToContainTextAsync("Papier A4");

        // Weryfikacja rachunku sprzedawcy na białej liście (atrapa API MF)
        await page.ClickAsync("text=Sprawdź rachunek na białej liście");
        await Expect(page.Locator("[data-testid=bank-check] .alert-success")).ToContainTextAsync("Rachunek jest na białej liście");
        await Expect(page.Locator("[data-testid=bank-check]")).ToContainTextAsync("identyfikator zapytania MF");
        await E2EFixture.ScreenshotAsync(page, "16-faktura-otrzymana-biala-lista");
    }

    [E2EFact]
    public async Task UzytkownikB_NieWidziFakturyUzytkownikaA_PoAdresieStrony()
    {
        var a = await fx.NewPageAsync();
        await NewUserWithCompanyAsync(a);
        await a.GotoAsync("/faktury/nowa");
        await FillManualBuyerAsync(a, "Tajny Kontrahent");
        await FillItemAsync(a, 0, "Usługa", "1", "100");
        await a.ClickAsync("button[type=submit]");
        await a.WaitForURLAsync(new Regex("/faktury/\\d+$"));
        var url = a.Url;

        var b = await fx.NewPageAsync();
        await NewUserWithCompanyAsync(b);
        await b.GotoAsync(url);
        await Expect(b.Locator(".alert-danger")).ToContainTextAsync("Nie znaleziono");
        await Expect(b.GetByText("Tajny Kontrahent")).Not.ToBeVisibleAsync();
    }

    [E2EFact]
    public async Task DaneKontrahentaZWykazuVat_UzupelnianePoNip()
    {
        var page = await fx.NewPageAsync();
        await NewUserWithCompanyAsync(page);

        // Formularz kontrahenta: przycisk aktywny dopiero przy poprawnym NIP
        await page.GotoAsync("/kontrahenci/nowy");
        await page.ClickAsync("button[type=submit]");
        await Expect(page.GetByText("Podaj miejscowość.")).ToBeVisibleAsync();
        await page.FillAsync("#nip", "1234567890");
        await Expect(page.Locator("[data-testid=vat-lookup]")).ToBeDisabledAsync();

        // Podmiot z wykazu (atrapa API MF): nazwa i adres rozbity na pola, stare komunikaty błędów znikają
        await page.FillAsync("#nip", "123-456-32-18");
        await page.ClickAsync("[data-testid=vat-lookup]");
        await Expect(page.Locator("[data-testid=vat-lookup-status]")).ToContainTextAsync("czynny podatnik VAT");
        await Expect(page.Locator("#name")).ToHaveValueAsync("PRZYKŁADOWA SPÓŁKA Z OGRANICZONĄ ODPOWIEDZIALNOŚCIĄ");
        await Expect(page.Locator("#customer-street")).ToHaveValueAsync("ul. Jana Pawła II 15/3");
        await Expect(page.Locator("#customer-postal")).ToHaveValueAsync("61-139");
        await Expect(page.Locator("#customer-city")).ToHaveValueAsync("Poznań");
        await Expect(page.GetByText("Podaj miejscowość.")).Not.ToBeVisibleAsync();
        await E2EFixture.ScreenshotAsync(page, "15-kontrahent-z-wykazu-vat");

        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(new Regex("/kontrahenci$"));
        await Expect(page.Locator("tbody")).ToContainTextAsync("PRZYKŁADOWA SPÓŁKA");

        // NIP spoza wykazu - komunikat, dane wpisywane ręcznie
        await page.GotoAsync("/faktury/nowa");
        await page.FillAsync("#buyer-nip", TestUsers.RandomNip());
        await page.ClickAsync("[data-testid=vat-lookup]");
        await Expect(page.Locator("[data-testid=vat-lookup-status]")).ToContainTextAsync("nie ma w wykazie podatników VAT");
        await Expect(page.Locator("#buyer-name")).ToHaveValueAsync("");
    }

    [E2EFact]
    public async Task WystawPodobna_KopiujeNabywcePozycjeIWarunki_ZNowymNumerem()
    {
        var page = await fx.NewPageAsync();
        await NewUserWithCompanyAsync(page);

        // Faktura-wzór: nabywca wpisany ręcznie, dwie pozycje (w tym zwolniona), termin 7 dni
        await page.GotoAsync("/faktury/nowa");
        await FillManualBuyerAsync(page, "Stały Klient sp. z o.o.", "1234563218");
        await FillItemAsync(page, 0, "Abonament serwisowy", "1", "500");
        await page.ClickAsync("text=+ Dodaj pozycję");
        await FillItemAsync(page, 1, "Szkolenie", "2", "300", "Exempt");
        await page.FillAsync("#exemption", "art. 43 ust. 1 pkt 29 ustawy o VAT");
        await page.ClickAsync("button:has-text('7 dni')");
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(new Regex("/faktury/\\d+$"));
        var templateUrl = page.Url;
        var templateNumber = await page.Locator("h1").InnerTextAsync();

        await page.ClickAsync("text=Wystaw podobną");
        await page.WaitForURLAsync(new Regex("/faktury/nowa\\?wzor=\\d+$"));
        await Expect(page.Locator("[data-testid=template-info]")).ToContainTextAsync(templateNumber.Replace("Faktura VAT ", ""));
        await Expect(page.Locator("#buyer-name")).ToHaveValueAsync("Stały Klient sp. z o.o.");
        await Expect(page.Locator("#buyer-nip")).ToHaveValueAsync("1234563218");
        await Expect(page.Locator(".items-table tbody tr")).ToHaveCountAsync(2);
        await Expect(Row(page, 1).Locator("input[aria-label^='Nazwa pozycji']")).ToHaveValueAsync("Szkolenie");
        await Expect(page.Locator("#exemption")).ToHaveValueAsync("art. 43 ust. 1 pkt 29 ustawy o VAT");
        await Expect(page.Locator("[data-testid=total-gross]")).ToContainTextAsync("1 215,00");
        await Expect(page.Locator("#number")).ToHaveValueAsync(""); // numer nadany automatycznie przy zapisie

        // Termin zachowuje odstęp 7 dni od daty wystawienia
        var issue = DateOnly.Parse(await page.Locator("#issue").InputValueAsync());
        var due = DateOnly.Parse(await page.Locator("#due").InputValueAsync());
        Assert.Equal(7, due.DayNumber - issue.DayNumber);
        await E2EFixture.ScreenshotAsync(page, "18-wystaw-podobna");

        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(new Regex("/faktury/\\d+$"));
        Assert.NotEqual(templateUrl, page.Url);
        await Expect(page.Locator("h1")).Not.ToHaveTextAsync(templateNumber);
        await Expect(page.Locator(".fs-5.text-end")).ToContainTextAsync("1 215,00 PLN");

        // Zwykła „Nowa faktura” po „Wystaw podobną” jest pusta
        await page.ClickAsync("text=Wystaw podobną");
        await Expect(page.Locator("[data-testid=template-info]")).ToBeVisibleAsync();
        await page.ClickAsync("nav >> text=Nowa faktura");
        await Expect(page.Locator("[data-testid=template-info]")).Not.ToBeVisibleAsync();
        await Expect(page.Locator("#buyer-name")).ToHaveValueAsync("");
    }

    [E2EFact]
    public async Task FakturaKorygujaca_ObnizkaCeny_WysylkaDoKsef_INaleznoscPoKorekcie()
    {
        var page = await fx.NewPageAsync();
        await NewUserWithCompanyAsync(page);
        await page.ClickAsync("text=Wygeneruj token testowy");
        await Expect(page.GetByText("Wygenerowano token w testowym KSeF.")).ToBeVisibleAsync();

        // Faktura pierwotna: 10 × 100 zł + 23% = 1 230,00 zł, przyjęta w KSeF
        await page.GotoAsync("/faktury/nowa");
        await FillManualBuyerAsync(page, "Odbiorca Korekty sp. z o.o.", "1234563218");
        await FillItemAsync(page, 0, "Usługa wdrożeniowa", "10", "100");
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(new Regex("/faktury/\\d+$"));
        var originalUrl = page.Url;
        await page.ClickAsync("text=Wyślij do KSeF");
        await Expect(page.Locator("[data-testid=status-badge]")).ToContainTextAsync("Przyjęta w KSeF", new() { Timeout = 60_000 });

        // Korekta: obniżka ceny do 80 zł - stan przed korektą i różnica liczona na żywo
        await page.ClickAsync("text=Wystaw korektę");
        await Expect(page.Locator("h1")).ToHaveTextAsync("Nowa faktura korygująca");
        await Expect(page.Locator("[data-testid=correction-header]")).ToContainTextAsync("Odbiorca Korekty sp. z o.o.");
        await Expect(page.Locator("[data-testid=items-before] tbody tr")).ToHaveCountAsync(1);
        await page.FillAsync("#reason", "Rabat za opóźnienie wdrożenia");
        await Row(page, 0).Locator("input[aria-label='Cena netto']").FillAsync("80");
        await Row(page, 0).Locator("input[aria-label='Cena netto']").PressAsync("Tab");
        await Expect(page.Locator("[data-testid=correction-difference]")).ToContainTextAsync("-246,00");
        await E2EFixture.ScreenshotAsync(page, "19-korekta-formularz");

        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(new Regex("/faktury/\\d+$"));
        await Expect(page.Locator("h1")).ToContainTextAsync("Faktura korygująca KOR/1/");
        await Expect(page.Locator("[data-testid=correction-info]")).ToContainTextAsync("Rabat za opóźnienie wdrożenia");
        await Expect(page.Locator("[data-testid=correction-total]")).ToContainTextAsync("Do zwrotu: 246,00 PLN");

        await page.ClickAsync("text=Wyślij do KSeF");
        await Expect(page.Locator("[data-testid=status-badge]")).ToContainTextAsync("Przyjęta w KSeF", new() { Timeout = 60_000 });
        await E2EFixture.ScreenshotAsync(page, "20-korekta-przyjeta");

        // Faktura pierwotna: lista korekt i należność po korekcie (1 230,00 - 246,00)
        await page.GotoAsync(originalUrl);
        await Expect(page.Locator("[data-testid=corrections]")).ToContainTextAsync("KOR/1/");
        await Expect(page.Locator("[data-testid=payment-panel] [data-testid=remaining]")).ToContainTextAsync("984,00");

        await page.GotoAsync("/faktury");
        await Expect(page.Locator("tbody .badge:has-text('KOR')")).ToHaveCountAsync(1);
    }

    [E2EFact]
    public async Task ZaplaconoPrzyWystawieniu_TrafiaDoKsef_IFakturaJestOplacona()
    {
        var page = await fx.NewPageAsync();
        await NewUserWithCompanyAsync(page);
        await page.ClickAsync("text=Wygeneruj token testowy");
        await Expect(page.GetByText("Wygenerowano token w testowym KSeF.")).ToBeVisibleAsync();

        // Sprzedaż za gotówkę: zapłacono w dniu wystawienia, bez terminu płatności
        await page.GotoAsync("/faktury/nowa");
        await FillManualBuyerAsync(page, "Klient Gotówkowy", "1234563218");
        await FillItemAsync(page, 0, "Naprawa sprzętu", "1", "200");
        await page.SelectOptionAsync("#payment", "Cash");
        await page.CheckAsync("#paid");
        await Expect(page.Locator("#due")).ToHaveCountAsync(0); // termin zbędny - faktura zapłacona
        await Expect(page.Locator("#paid-on")).ToHaveValueAsync(await page.Locator("#issue").InputValueAsync());
        await E2EFixture.ScreenshotAsync(page, "21-zaplacono-przy-wystawieniu");

        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(new Regex("/faktury/\\d+$"));
        await Expect(page.Locator("[data-testid=paid-at-issue]")).ToBeVisibleAsync();

        await page.ClickAsync("text=Wyślij do KSeF");
        await Expect(page.Locator("[data-testid=status-badge]")).ToContainTextAsync("Przyjęta w KSeF", new() { Timeout = 60_000 });

        // Rozliczenie: zapłata z faktury, bez możliwości usunięcia
        var panel = page.Locator("[data-testid=payment-panel]");
        await Expect(panel).ToContainTextAsync("w fakturze");
        await Expect(panel.Locator("button[title='Usuń płatność']")).ToHaveCountAsync(0);
        await Expect(panel.Locator("text=Zarejestruj")).ToHaveCountAsync(0);
    }
}

