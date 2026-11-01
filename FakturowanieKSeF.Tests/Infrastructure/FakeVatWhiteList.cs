using System.Collections.Concurrent;
using FakturowanieKSeF.Api.Integrations.VatWhiteList;

namespace FakturowanieKSeF.Tests.Infrastructure;

/// <summary>Atrapa wykazu podatników VAT (Biała lista MF) - testy nie wymagają dostępu do sieci.</summary>
public sealed class FakeVatWhiteList : IVatWhiteListClient
{
    private readonly ConcurrentDictionary<string, WhiteListSubject> _subjects = new();
    private int _searches;

    /// <summary>Liczba zapytań, które dotarły do „API MF” (bez trafień w pamięć podręczną).</summary>
    public int Searches => _searches;

    /// <summary>Symulacja awarii API MF.</summary>
    public bool Fail { get; set; }

    public FakeVatWhiteList()
    {
        // Podmiot przykładowy dostępny w każdym teście (m.in. E2E)
        Add(new WhiteListSubject(
            "PRZYKŁADOWA SPÓŁKA Z OGRANICZONĄ ODPOWIEDZIALNOŚCIĄ", "1234563218", "Czynny", "123456785", "0000123456",
            "UL. JANA PAWŁA II 15/3, 61-139 POZNAŃ", null,
            ["61109010140000071219812874", "27114020040000300201355387"], "Ab12c-3de4f5g"));
    }

    /// <summary>Liczba sprawdzeń rachunków, które dotarły do „API MF”.</summary>
    public int AccountChecks => _accountChecks;
    private int _accountChecks;

    public Task<WhiteListAccountCheck> CheckBankAccountAsync(string nip, string account, DateOnly date, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _accountChecks);
        if (Fail) throw new VatWhiteListException("Wykaz podatników VAT nie odpowiada. Spróbuj ponownie później.");
        var assigned = _subjects.TryGetValue(nip, out var s) && s.AccountNumbers.Contains(account);
        return Task.FromResult(new WhiteListAccountCheck(assigned, $"chk-{_accountChecks}"));
    }

    public void Add(WhiteListSubject subject) => _subjects[subject.Nip] = subject;

    public Task<WhiteListSubject?> FindByNipAsync(string nip, DateOnly date, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _searches);
        if (Fail) throw new VatWhiteListException("Wykaz podatników VAT nie odpowiada. Spróbuj ponownie później.");
        return Task.FromResult(_subjects.GetValueOrDefault(nip));
    }
}
