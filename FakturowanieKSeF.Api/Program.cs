using Microsoft.EntityFrameworkCore;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Endpoints;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Api.Services;
using FakturowanieKSeF.Models;

var builder = WebApplication.CreateBuilder(args);

// 1. Konfiguracja Bazy Danych (PostgreSQL)
// Connection string odczytywany przy tworzeniu kontekstu (a nie raz przy starcie),
// dzięki czemu testy integracyjne mogą go nadpisać własną, tymczasową bazą.
builder.Services.AddDbContext<AppDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")));

// 2. Konfiguracja Systemu Użytkowników (Identity)
builder.Services.AddIdentityApiEndpoints<ApplicationUser>()
    .AddEntityFrameworkStores<AppDbContext>();
builder.Services.AddAuthorization();

// 3. Podstawowe usługi API (Swagger do testowania)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 4. Logika biznesowa API
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentCompany>();
builder.Services.AddScoped<InvoiceNumbering>();

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
api.MapGroup("/auth").WithTags("Auth").MapIdentityApi<ApplicationUser>();
api.MapHealthChecks("/health");
api.MapCompanyEndpoints();
api.MapCustomerEndpoints();
api.MapInvoiceEndpoints();

// Nieznany adres pod /api zwraca 404, zamiast strony klienta
api.Map("/{**slug}", () => Results.NotFound());

// 8. Każdy inny adres obsługuje routing po stronie klienta (Blazor)
app.MapFallbackToFile("index.html");

app.Run();

// Udostępnienie klasy Program dla testów integracyjnych (WebApplicationFactory)
public partial class Program { }
