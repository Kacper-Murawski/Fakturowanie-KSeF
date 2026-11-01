using System.Globalization;
using FakturowanieKSeF.Client;
using FakturowanieKSeF.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Polskie formatowanie liczb i dat niezależnie od języka przeglądarki
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("pl-PL");

// API jest pod tym samym adresem co aplikacja - ciasteczko sesji dołączane automatycznie
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<ApiClient>();

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AuthService>());

await builder.Build().RunAsync();
