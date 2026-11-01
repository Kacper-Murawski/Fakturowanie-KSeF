namespace FakturowanieKSeF.Api.Ksef;

/// <summary>
/// Ostatnia linia obrony przed połączeniem z produkcyjnym KSeF: handler HTTP podpinany do każdego
/// klienta HTTP używanego do komunikacji z KSeF. Sprawdza adres KAŻDEGO wychodzącego żądania
/// (także adresów zwróconych przez samo API, np. do pobrania UPO) i blokuje wszystko,
/// co nie jest środowiskiem testowym - zanim jakiekolwiek dane opuszczą serwer.
/// </summary>
public sealed class KsefTestOnlyHandler : DelegatingHandler
{
    public KsefTestOnlyHandler() { }

    public KsefTestOnlyHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        KsefEnvironments.EnsureTestEnvironment(request.RequestUri);
        return base.SendAsync(request, cancellationToken);
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        KsefEnvironments.EnsureTestEnvironment(request.RequestUri);
        return base.Send(request, cancellationToken);
    }
}
