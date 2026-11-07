using FakturowanieKSeF.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FakturowanieKSeF.Tests.Infrastructure;

/// <summary>
/// Uruchamia całą aplikację w pamięci na potrzeby testów integracyjnych.
/// Każde uruchomienie testów dostaje własną, tymczasową bazę PostgreSQL (tworzoną przez migracje
/// i usuwaną po zakończeniu), więc testy nie naruszają bazy deweloperskiej.
/// Adres serwera bazy pochodzi z konfiguracji API lub zmiennej środowiskowej
/// <c>ConnectionStrings__DefaultConnection</c> (ustawianej np. w CI).
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _connectionString;

    public FakeKsefGateway Ksef { get; } = new();

    public FakeVatWhiteList VatWhiteList { get; } = new();

    public FakeEmailSender Email { get; } = new();

    public FakeNbpClient Nbp { get; } = new();

    /// <summary>Jeden przebieg kolejki e-mail (tak, jak zrobiłaby to usługa w tle).</summary>
    public async Task<int> ProcessEmailsAsync()
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FakturowanieKSeF.Api.Services.Email.EmailOutbox>().ProcessAsync();
    }

    /// <summary>Dodatkowa konfiguracja w klasach pochodnych (np. testy E2E z włączoną usługą w tle).</summary>
    protected virtual IEnumerable<KeyValuePair<string, string?>> AdditionalSettings() => [];

    /// <summary>Jeden przebieg usługi wysyłki do KSeF (tak, jak zrobiłaby to usługa w tle).</summary>
    public async Task<int> ProcessKsefAsync()
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FakturowanieKSeF.Api.Services.KsefSubmissionProcessor>().ProcessAsync();
    }

    public ApiFactory()
    {
        var baseConnectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=ksef_database;Username=ksef_user;Password=ksef_password";

        _connectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = $"ksef_test_{Guid.NewGuid():N}"
        }.ConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = _connectionString,
            // Usługa w tle wyłączona - testy wywołują procesor wysyłek jawnie, w kontrolowanych krokach
            ["Ksef:Worker:Enabled"] = "false",
            ["Ksef:Receive:Enabled"] = "false",
            // Poczta: atrapa zamiast serwera SMTP, kolejka przetwarzana jawnie w testach
            ["Email:SmtpHost"] = "smtp.atrapa.invalid",
            ["Email:From"] = "faktury@fakturowanie.test",
            ["Email:Worker:Enabled"] = "false",
            ["Reminders:Worker:Enabled"] = "false",
            ["Recurring:Worker:Enabled"] = "false",
            // Limity żądań wyłączone - testy tworzą wielu użytkowników z jednego adresu (osobny test z limitami)
            ["RateLimiting:Enabled"] = "false"
        };
        foreach (var (key, value) in AdditionalSettings()) settings[key] = value;

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));

        // Atrapa KSeF zamiast prawdziwego połączenia - testy nie wymagają sieci
        // (także wykazu podatników VAT)
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<FakturowanieKSeF.Ksef.IKsefGateway>(Ksef);
            services.AddSingleton<FakturowanieKSeF.Api.Integrations.VatWhiteList.IVatWhiteListClient>(VatWhiteList);
            services.AddSingleton<FakturowanieKSeF.Api.Services.Email.IEmailSender>(Email);
            services.AddSingleton<FakturowanieKSeF.Api.Integrations.Nbp.INbpClient>(Nbp);
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var database = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).Database;
        if (database is null || !database.StartsWith("ksef_test_"))
            throw new InvalidOperationException($"Testy muszą działać na bazie tymczasowej, a nie na '{database}'.");

        await db.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Bezpiecznik: usuwamy wyłącznie tymczasową bazę testową, nigdy deweloperską/produkcyjną
            var database = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).Database;
            if (database is null || !database.StartsWith("ksef_test_"))
                throw new InvalidOperationException($"Odmowa usunięcia bazy '{database}', która nie jest bazą testową.");

            await db.Database.EnsureDeletedAsync();
        }
        await base.DisposeAsync();
    }
}
