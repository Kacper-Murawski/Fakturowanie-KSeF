namespace FakturowanieKSeF.Ksef;

/// <summary>
/// Środowiska KSeF, z którymi aplikacja może się łączyć.
/// <para>
/// CELOWO brak wartości dla środowiska produkcyjnego (api.ksef.mf.gov.pl) oraz przedprodukcyjnego (DEMO).
/// Aplikacja w obecnej fazie (projekt inżynierski / MVP) może komunikować się wyłącznie ze środowiskiem
/// testowym MF, w którym faktury nie mają mocy prawnej. Dodanie produkcji wymaga świadomej zmiany kodu.
/// </para>
/// </summary>
public enum KsefEnvironment
{
    /// <summary>Środowisko testowe MF (TE): https://api-test.ksef.mf.gov.pl</summary>
    Test
}

public static class KsefEnvironments
{
    /// <summary>Jedyny host KSeF, do którego aplikacja może wysyłać żądania.</summary>
    public const string TestHost = "api-test.ksef.mf.gov.pl";

    public static Uri GetBaseUri(KsefEnvironment environment) => environment switch
    {
        KsefEnvironment.Test => new Uri($"https://{TestHost}"),
        _ => throw new ArgumentOutOfRangeException(nameof(environment), environment,
            "Nieobsługiwane środowisko KSeF. Dozwolone jest wyłącznie środowisko testowe.")
    };

    /// <summary>
    /// Sprawdza, czy adres wskazuje na środowisko testowe KSeF. Każdy inny adres (w szczególności
    /// produkcyjny api.ksef.mf.gov.pl) powoduje wyjątek - żądanie nie zostanie wysłane.
    /// </summary>
    public static void EnsureTestEnvironment(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri)
            throw new KsefProductionBlockedException("Brak bezwzględnego adresu żądania do KSeF.");

        if (uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, TestHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new KsefProductionBlockedException(
                $"Zablokowano żądanie do '{uri.Scheme}://{uri.Host}'. " +
                $"Aplikacja może łączyć się wyłącznie ze środowiskiem testowym KSeF (https://{TestHost}).");
        }
    }
}

public sealed class KsefProductionBlockedException(string message) : InvalidOperationException(message);
