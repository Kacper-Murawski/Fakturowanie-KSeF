using KSeF.Client.DI;
using Microsoft.Extensions.DependencyInjection;

namespace FakturowanieKSeF.Ksef;

public static class KsefServiceCollectionExtensions
{
    /// <summary>
    /// Rejestruje oficjalnego klienta KSeF (biblioteka MF) skonfigurowanego WYŁĄCZNIE na środowisko testowe.
    /// <para>
    /// Adres bazowy pochodzi z <see cref="KsefEnvironments"/> (nie z konfiguracji), a do klienta HTTP biblioteki
    /// dopinany jest <see cref="KsefTestOnlyHandler"/>, który blokuje każde żądanie spoza środowiska testowego.
    /// Wszystkie połączenia biblioteki MF (w tym pobieranie certyfikatów MF i UPO) przechodzą przez tego klienta.
    /// </para>
    /// </summary>
    public static IServiceCollection AddKsefTestClient(this IServiceCollection services)
    {
        var baseUri = KsefEnvironments.GetBaseUri(KsefEnvironment.Test);
        KsefEnvironments.EnsureTestEnvironment(baseUri);

        services.AddKSeFClient(
            options => options.BaseUrl = baseUri.ToString().TrimEnd('/'),
            httpClientBuilder => httpClientBuilder.AddHttpMessageHandler(() => new KsefTestOnlyHandler()));

        services.AddCryptographyClient();

        return services;
    }
}
