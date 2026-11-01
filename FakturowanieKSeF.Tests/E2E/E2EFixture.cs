using FakturowanieKSeF.Tests.Infrastructure;
using Microsoft.Playwright;

namespace FakturowanieKSeF.Tests.E2E;

/// <summary>
/// Test E2E w przeglądarce - uruchamiany, gdy ustawiono E2E_TESTS=1 (w CI zawsze; lokalnie opcjonalnie,
/// bo wymaga zainstalowanej przeglądarki Chromium dla Playwright).
/// </summary>
public sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("E2E_TESTS") != "1")
            Skip = "Test E2E w przeglądarce - uruchom z E2E_TESTS=1 (wymaga Chromium dla Playwright).";
    }
}

/// <summary>
/// Aplikacja uruchomiona na prawdziwym serwerze HTTP (Kestrel), żeby przeglądarka mogła pobrać klienta Blazor
/// WebAssembly. KSeF zastąpiony atrapą; usługa wysyłki w tle włączona (krótki interwał).
/// </summary>
public sealed class E2EAppFactory : ApiFactory
{
    public E2EAppFactory() => UseKestrel(0); // losowy wolny port

    protected override IEnumerable<KeyValuePair<string, string?>> AdditionalSettings() =>
    [
        new("Ksef:Worker:Enabled", "true"),
        new("Ksef:Worker:IntervalSeconds", "1")
    ];
}

/// <summary>Współdzielone przez testy klasy: uruchomiona aplikacja i przeglądarka (Chromium, polskie ustawienia regionalne).</summary>
public sealed class E2EFixture : IAsyncLifetime
{
    public E2EAppFactory App { get; } = new();
    public string BaseUrl { get; private set; } = "";
    private IPlaywright? _playwright;
    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("E2E_TESTS") != "1") return;

        await ((IAsyncLifetime)App).InitializeAsync();
        App.StartServer();
        BaseUrl = App.ClientOptions.BaseAddress.ToString().TrimEnd('/');

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            // Opcjonalnie wskazanie własnej przeglądarki (np. preinstalowanej w kontenerze)
            ExecutablePath = Environment.GetEnvironmentVariable("E2E_CHROMIUM_PATH")
        });
    }

    /// <summary>Nowa, niezależna sesja przeglądarki (osobne ciasteczka) z polskimi ustawieniami regionalnymi.</summary>
    public async Task<IPage> NewPageAsync()
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = "pl-PL",
            TimezoneId = "Europe/Warsaw",
            BaseURL = BaseUrl,
            AcceptDownloads = true,
            ViewportSize = new ViewportSize { Width = 1366, Height = 900 }
        });
        var page = await context.NewPageAsync();
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync(); // potwierdzenia (usuń, wyślij)
        page.SetDefaultTimeout(30_000);
        return page;
    }

    /// <summary>
    /// Zrzut ekranu do dokumentacji - tylko gdy ustawiono <c>E2E_SCREENSHOTS_DIR</c>
    /// (np. <c>E2E_SCREENSHOTS_DIR=docs/zrzuty</c>); w zwykłym przebiegu testów nic nie robi.
    /// </summary>
    public static async Task ScreenshotAsync(IPage page, string name)
    {
        var dir = Environment.GetEnvironmentVariable("E2E_SCREENSHOTS_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, $"{name}.png") });
    }

    public async Task DisposeAsync()
    {
        if (_playwright is null) return;
        await Browser.DisposeAsync();
        _playwright.Dispose();
        await ((IAsyncLifetime)App).DisposeAsync();
    }
}

[CollectionDefinition("E2E")]
public sealed class E2ECollection : ICollectionFixture<E2EFixture>;
