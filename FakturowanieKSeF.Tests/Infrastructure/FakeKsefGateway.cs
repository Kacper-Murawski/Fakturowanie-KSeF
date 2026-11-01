using FakturowanieKSeF.Ksef;

namespace FakturowanieKSeF.Tests.Infrastructure;

/// <summary>
/// Atrapa KSeF do testów automatycznych (bez sieci). Pozwala zasymulować przyjęcie, odrzucenie, przetwarzanie
/// i awarie komunikacji. Rejestruje wywołania, żeby testy mogły sprawdzić, co zostało wysłane.
/// </summary>
public sealed class FakeKsefGateway : IKsefGateway
{
    public const string ValidToken = "token-testowy-poprawny";

    private int _counter;

    /// <summary>Kolejne odpowiedzi na odpytanie o status (gdy pusta - faktura przyjęta).</summary>
    public Queue<KsefInvoiceStatus> StatusResponses { get; } = new();

    /// <summary>Wyjątek rzucany przy wysyłce (symulacja awarii sieci).</summary>
    public Exception? SendFailure { get; set; }

    /// <summary>Wyjątek rzucany przy odpytaniu o status.</summary>
    public Exception? StatusFailure { get; set; }

    public bool UpoReady { get; set; } = true;

    public List<(string Nip, byte[] Xml)> SentInvoices { get; } = [];
    public List<string> ClosedSessions { get; } = [];

    public Task VerifyTokenAsync(string nip, string ksefToken, CancellationToken ct = default) =>
        ksefToken == ValidToken
            ? Task.CompletedTask
            : throw new KsefAuthenticationException("KSeF odrzucił token (sprawdź, czy token jest aktywny i wystawiony dla tego NIP).");

    public Task<string> GenerateTestTokenAsync(string nip, CancellationToken ct = default) => Task.FromResult(ValidToken);

    public Task<KsefSendResult> SendInvoiceAsync(string nip, string ksefToken, byte[] invoiceXml, CancellationToken ct = default)
    {
        if (ksefToken != ValidToken) throw new KsefAuthenticationException("Nieprawidłowy token.");
        if (SendFailure is not null) throw SendFailure;

        SentInvoices.Add((nip, invoiceXml));
        var n = Interlocked.Increment(ref _counter);
        return Task.FromResult(new KsefSendResult($"SESJA-{n}", $"FAKTURA-{n}"));
    }

    public Task<KsefInvoiceStatus> GetInvoiceStatusAsync(string nip, string ksefToken, string sessionReferenceNumber,
        string invoiceReferenceNumber, CancellationToken ct = default)
    {
        if (StatusFailure is not null) throw StatusFailure;

        return Task.FromResult(StatusResponses.Count > 0
            ? StatusResponses.Dequeue()
            : new KsefInvoiceStatus(200, "Sukces", [], $"{nip}-20261101-{invoiceReferenceNumber}-00", DateTimeOffset.UtcNow));
    }

    public Task CloseSessionAsync(string nip, string ksefToken, string sessionReferenceNumber, CancellationToken ct = default)
    {
        ClosedSessions.Add(sessionReferenceNumber);
        return Task.CompletedTask;
    }

    public Task<string?> GetUpoAsync(string nip, string ksefToken, string sessionReferenceNumber, string ksefNumber,
        CancellationToken ct = default) =>
        Task.FromResult(UpoReady ? $"<Potwierdzenie><NumerKSeFDokumentu>{ksefNumber}</NumerKSeFDokumentu></Potwierdzenie>" : null);
}
