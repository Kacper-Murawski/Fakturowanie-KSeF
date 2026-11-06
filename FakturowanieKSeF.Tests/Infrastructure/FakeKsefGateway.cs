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

    /// <summary>Faktury „wystawione przez innych” na dany NIP nabywcy: metadane + XML.</summary>
    public Dictionary<string, List<(KsefInvoiceMetadata Meta, string Xml)>> Received { get; } = [];

    /// <summary>HWM zwracany przez KSeF (null - brak).</summary>
    public DateTimeOffset? Hwm { get; set; }

    public Exception? DownloadFailure { get; set; }

    public List<(string Nip, DateTimeOffset From, int Page)> Queries { get; } = [];
    public int Downloads { get; private set; }
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
            : new KsefInvoiceStatus(200, "Sukces", [], KsefNumber(nip, invoiceReferenceNumber), DateTimeOffset.UtcNow));
    }

    public Task CloseSessionAsync(string nip, string ksefToken, string sessionReferenceNumber, CancellationToken ct = default)
    {
        ClosedSessions.Add(sessionReferenceNumber);
        return Task.CompletedTask;
    }

    public Task<KsefMetadataPage> QueryReceivedInvoicesAsync(string nip, string ksefToken, DateTimeOffset from, int pageOffset,
        int pageSize, CancellationToken ct = default)
    {
        lock (Queries) Queries.Add((nip, from, pageOffset));
        var all = (Received.TryGetValue(nip, out var list) ? list : [])
            .Where(x => x.Meta.PermanentStorageDate >= from && (Hwm is null || x.Meta.PermanentStorageDate <= Hwm))
            .OrderBy(x => x.Meta.PermanentStorageDate)
            .Select(x => x.Meta)
            .ToList();
        var page = all.Skip(pageOffset * pageSize).Take(pageSize).ToList();
        return Task.FromResult(new KsefMetadataPage(page, all.Count > (pageOffset + 1) * pageSize, false, Hwm));
    }

    /// <summary>Faktury wystawione przez firmę (NIP sprzedawcy) - np. w innym programie (D44).</summary>
    public Dictionary<string, List<(KsefInvoiceMetadata Meta, string Xml)>> Issued { get; } = [];

    public Task<KsefMetadataPage> QueryIssuedInvoicesAsync(string nip, string ksefToken, DateTimeOffset from, int pageOffset,
        int pageSize, CancellationToken ct = default)
    {
        lock (Queries) Queries.Add((nip, from, pageOffset));
        var all = (Issued.TryGetValue(nip, out var list) ? list : [])
            .Where(x => x.Meta.PermanentStorageDate >= from && (Hwm is null || x.Meta.PermanentStorageDate <= Hwm))
            .OrderBy(x => x.Meta.PermanentStorageDate)
            .Select(x => x.Meta)
            .ToList();
        var page = all.Skip(pageOffset * pageSize).Take(pageSize).ToList();
        return Task.FromResult(new KsefMetadataPage(page, all.Count > (pageOffset + 1) * pageSize, false, Hwm));
    }

    public Task<string> DownloadInvoiceAsync(string nip, string ksefToken, string ksefNumber, CancellationToken ct = default)
    {
        if (DownloadFailure is not null) throw DownloadFailure;
        Downloads++;
        var source = Received.TryGetValue(nip, out var received) && received.Any(x => x.Meta.KsefNumber == ksefNumber)
            ? received
            : Issued[nip];
        return Task.FromResult(source.Single(x => x.Meta.KsefNumber == ksefNumber).Xml);
    }

    public Task<string?> GetUpoAsync(string nip, string ksefToken, string sessionReferenceNumber, string ksefNumber,
        CancellationToken ct = default) =>
        Task.FromResult(UpoReady ? $"<Potwierdzenie><NumerKSeFDokumentu>{ksefNumber}</NumerKSeFDokumentu></Potwierdzenie>" : null);

    /// <summary>Numer KSeF w formacie zgodnym z typem TNumerKSeF (NIP-RRRRMMDD-12 znaków hex-2 znaki hex).</summary>
    private static string KsefNumber(string nip, string invoiceReferenceNumber) =>
        $"{nip}-20261101-{int.Parse(invoiceReferenceNumber["FAKTURA-".Length..]):X12}-00";
}
