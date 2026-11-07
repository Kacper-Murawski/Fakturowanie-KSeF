using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Endpoints;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Api.Integrations.VatWhiteList;
using FakturowanieKSeF.Api.Services;
using FakturowanieKSeF.Api.Services.Email;
using FakturowanieKSeF.Ksef;
using FakturowanieKSeF.Ksef.Fa3;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using System.Security.Cryptography.X509Certificates;
using FakturowanieKSeF.Models;

var builder = WebApplication.CreateBuilder(args);

// 1. Konfiguracja Bazy Danych (PostgreSQL)
// Connection string odczytywany przy tworzeniu kontekstu (a nie raz przy starcie),
// dzięki czemu testy integracyjne mogą go nadpisać własną, tymczasową bazą.
builder.Services.AddDbContext<AppDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")));

// 2. Konfiguracja Systemu Użytkowników (Identity)
builder.Services.AddIdentityApiEndpoints<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.User.RequireUniqueEmail = true;
        // Blokada konta po 5 nieudanych próbach logowania na 15 minut (ochrona przed zgadywaniem haseł)
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddErrorDescriber<PolishIdentityErrorDescriber>();
builder.Services.AddAuthorization();

// Przeglądarka loguje się ciasteczkiem (/api/auth/login?useCookies=true): HttpOnly - niedostępne dla JavaScriptu
// (ochrona sesji przed XSS), SameSite=Strict - niewysyłane z innych witryn (ochrona przed CSRF).
// Tokeny Bearer pozostają dostępne dla innych klientów (testy, ewentualna aplikacja mobilna).
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "ksef.auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    // API zwraca kody HTTP zamiast przekierowań na stronę logowania
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
});

// 3. Podstawowe usługi API (Swagger do testowania)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 4. Logika biznesowa API
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentCompany>();
builder.Services.AddScoped<InvoiceNumbering>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Fa3XmlGenerator>();
builder.Services.AddSingleton<Fa3SchemaValidator>();

// Integracja z KSeF - WYŁĄCZNIE środowisko testowe (D8): klient MF + bramka + usługa wysyłki w tle
builder.Services.AddKsefTestClient();
builder.Services.AddScoped<KsefSubmissionProcessor>();
builder.Services.AddSingleton<KsefWorkSignal>();
builder.Services.AddHostedService<KsefSubmissionWorker>();
builder.Services.AddSingleton<KsefTokenProtector>();
builder.Services.AddScoped<ReceivedInvoiceSync>();
builder.Services.AddScoped<IssuedInvoiceImport>();
builder.Services.AddSingleton<InvoicePdfRenderer>();
builder.Services.AddScoped<AccountingExport>();

// Poczta: kolejka wiadomości w bazie + usługa wysyłki w tle (D36). Lokalnie Mailpit z docker compose.
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddSingleton<EmailWorkSignal>();
builder.Services.AddScoped<EmailOutbox>();
builder.Services.AddHostedService<EmailWorker>();
builder.Services.AddScoped<PaymentReminders>();
builder.Services.AddScoped<ReceivedInvoiceNotifier>();
builder.Services.AddScoped<RecurringInvoices>();
builder.Services.AddHostedService<RecurringInvoiceWorker>();
builder.Services.AddHostedService<PaymentReminderWorker>();
builder.Services.AddHostedService<ReceivedInvoicesWorker>();

// Wykaz podatników VAT (Biała lista MF) - podpowiadanie danych po NIP, z pamięcią podręczną i limitami (D28)
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<IVatWhiteListClient, VatWhiteListClient>(http =>
{
    http.BaseAddress = new Uri(VatWhiteListClient.BaseAddress);
    http.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton<VatRegistryLimiter>();

// Kursy średnie walut NBP (tabela A) - faktury w walutach obcych (D43)
builder.Services.AddHttpClient<FakturowanieKSeF.Api.Integrations.Nbp.INbpClient, FakturowanieKSeF.Api.Integrations.Nbp.NbpClient>(http =>
{
    http.BaseAddress = new Uri(FakturowanieKSeF.Api.Integrations.Nbp.NbpClient.BaseAddress);
    http.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton<FakturowanieKSeF.Api.Integrations.Nbp.ExchangeRateLookup>();
builder.Services.AddScoped<VatRegistryLookup>();

// Klucze szyfrujące (tokeny KSeF, ciasteczka sesji) trwale w bazie - przetrwają restart i wdrożenie nowej wersji
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("FakturowanieKSeF")
    .PersistKeysToDbContext<AppDbContext>();

// Produkcyjnie klucze w bazie są dodatkowo szyfrowane certyfikatem spoza bazy - kopia bazy bez certyfikatu
// nie pozwala odszyfrować tokenów KSeF (D23, D48)
if (builder.Configuration["DataProtection:CertificatePath"] is { Length: > 0 } certificatePath)
    dataProtection.ProtectKeysWithCertificate(
        X509CertificateLoader.LoadPkcs12FromFile(certificatePath, builder.Configuration["DataProtection:CertificatePassword"]));

// Za odwrotnym proxy (Caddy z HTTPS): adres klienta i schemat https z nagłówków X-Forwarded-* (D48).
// Aplikacja nie jest wtedy dostępna bezpośrednio z internetu - tylko przez proxy w sieci kontenerów.
var behindProxy = builder.Configuration.GetValue("ReverseProxy:Enabled", false);
if (behindProxy)
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        options.ForwardLimit = 1;
    });

// Walidacja żądań na podstawie atrybutów DataAnnotations i IValidatableObject w DTO (wbudowana w .NET 10);
// błędy zwracane jako ValidationProblemDetails (400). Te same reguły działają w formularzach Blazor.
builder.Services.AddValidation();

// Jednolity format błędów (ProblemDetails) i zamiana wyjątków bazy (konflikty) na odpowiedzi 409
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiErrors.ExceptionHandler>();

// Limity liczby żądań: logowanie/rejestracja na adres IP, całe API na użytkownika (D47)
builder.Services.AddApiRateLimits();

// 5. Kontrola stanu aplikacji (m.in. dostępność bazy danych) - używana przy wdrożeniu
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

var app = builder.Build();

// Produkcja: odmowa startu z deweloperskim hasłem do bazy z repozytorium (sekrety tylko w zmiennych środowiskowych)
if (app.Environment.IsProduction()
    && app.Configuration.GetConnectionString("DefaultConnection") is { } connection && connection.Contains("Password=ksef_password"))
    throw new InvalidOperationException(
        "Produkcyjnie ustaw hasło do bazy w zmiennej ConnectionStrings__DefaultConnection - nie używaj hasła deweloperskiego.");

// Migracje przy starcie (wdrożenie w kontenerze): baza może startować chwilę dłużej niż aplikacja
if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
            break;
        }
        catch (Exception ex) when (attempt < 10 && ex is Npgsql.NpgsqlException or System.Net.Sockets.SocketException)
        {
            app.Logger.LogWarning("Baza danych niedostępna (próba {Attempt}) - ponowienie za 3 s", attempt);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}

if (behindProxy) app.UseForwardedHeaders();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSecurityHeaders();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseWebAssemblyDebugging();
}

if (!app.Environment.IsDevelopment())
    app.UseHsts(); // przeglądarka łączy się później wyłącznie przez HTTPS

// Za proxy HTTPS i przekierowanie z HTTP obsługuje proxy
if (!behindProxy)
    app.UseHttpsRedirection();

// 6. Serwowanie aplikacji klienckiej (Blazor WebAssembly) z tego samego serwera co API
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

// Wbudowany endpoint 2FA Identity pozwala wyłączyć 2FA bez kodu (wystarczy sesja) - zastępuje go /api/account/2fa (D41)
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/auth/manage/2fa"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next();
});

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// 7. Endpointy API - wszystkie pod prefiksem /api, żeby nie kolidowały z adresami stron klienta
var api = app.MapGroup("/api");
var auth = api.MapGroup("/auth").WithTags("Auth").RequireRateLimiting(RateLimits.Auth);
auth.MapIdentityApi<ApplicationUser>();
auth.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
    {
        await signInManager.SignOutAsync();
        return Results.NoContent();
    })
    .RequireAuthorization()
    .WithSummary("Wylogowanie (usunięcie ciasteczka sesji)");
api.MapHealthChecks("/health");
api.MapCompanyEndpoints();
api.MapCustomerEndpoints();
api.MapProductEndpoints();
api.MapInvoiceEndpoints();
api.MapKsefSettingsEndpoints();
api.MapReceivedInvoiceEndpoints();
api.MapVatRegistryEndpoints();
api.MapPaymentEndpoints();
api.MapDashboardEndpoints();
api.MapAccountEndpoints();
api.MapMemberEndpoints();
api.MapExchangeRateEndpoints();
api.MapExportEndpoints();
api.MapEmailEndpoints();
api.MapNotificationEndpoints();
api.MapRecurringInvoiceEndpoints();

// Nieznany adres pod /api zwraca 404, zamiast strony klienta
api.Map("/{**slug}", () => Results.NotFound());

// 8. Każdy inny adres obsługuje routing po stronie klienta (Blazor)
app.MapFallbackToFile("index.html");

app.Run();

// Udostępnienie klasy Program dla testów integracyjnych (WebApplicationFactory)
public partial class Program { }
