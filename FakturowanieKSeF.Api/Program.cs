using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Models;

var builder = WebApplication.CreateBuilder(args);

// 1. Konfiguracja Bazy Danych (PostgreSQL)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// 2. Konfiguracja Systemu Użytkowników (Identity)
builder.Services.AddIdentityApiEndpoints<ApplicationUser>()
    .AddEntityFrameworkStores<AppDbContext>();

// 3. Podstawowe usługi API (Swagger do testowania)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// 4. Uruchomienie gotowych endpointów do logowania i rejestracji
app.MapIdentityApi<IdentityUser>();

app.Run();