using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Endpoints;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Api.Services;
using FakturowanieKSeF.Ksef;
using FakturowanieKSeF.Ksef.Fa3;
using Microsoft.AspNetCore.DataProtection;
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
builder.Services.AddSingleton<InvoicePdfRenderer>();
builder.Services.AddHostedService<ReceivedInvoicesWorker>();

// Klucze szyfrujące (tokeny KSeF, ciasteczka sesji) trwale w bazie - przetrwają restart i wdrożenie nowej wersji
builder.Services.AddDataProtection()
    .SetApplicationName("FakturowanieKSeF")
    .PersistKeysToDbContext<AppDbContext>();

// Walidacja żądań na podstawie atrybutów DataAnnotations i IValidatableObject w DTO (wbudowana w .NET 10);
// błędy zwracane jako ValidationProblemDetails (400). Te same reguły działają w formularzach Blazor.
builder.Services.AddValidation();

// Jednolity format błędów (ProblemDetails) i zamiana wyjątków bazy (konflikty) na odpowiedzi 409
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiErrors.ExceptionHandler>();

// 5. Kontrola stanu aplikacji (m.in. dostępność bazy danych) - używana przy wdrożeniu
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseWebAssemblyDebugging();
}

app.UseHttpsRedirection();

// 6. Serwowanie aplikacji klienckiej (Blazor WebAssembly) z tego samego serwera co API
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// 7. Endpointy API - wszystkie pod prefiksem /api, żeby nie kolidowały z adresami stron klienta
var api = app.MapGroup("/api");
var auth = api.MapGroup("/auth").WithTags("Auth");
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
api.MapInvoiceEndpoints();
api.MapKsefSettingsEndpoints();
api.MapReceivedInvoiceEndpoints();

// Nieznany adres pod /api zwraca 404, zamiast strony klienta
api.Map("/{**slug}", () => Results.NotFound());

// 8. Każdy inny adres obsługuje routing po stronie klienta (Blazor)
app.MapFallbackToFile("index.html");

app.Run();

// Udostępnienie klasy Program dla testów integracyjnych (WebApplicationFactory)
public partial class Program { }
