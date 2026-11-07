using FakturowanieKSeF.Contracts;
using FakturowanieKSeF.Contracts.Dtos;

namespace FakturowanieKSeF.Client.Services;

/// <summary>
/// Bieżąca firma i rola użytkownika w interfejsie (D46): przełącznik firm i ukrywanie przycisków, do których rola nie
/// ma uprawnień. To tylko wygoda - uprawnienia sprawdza serwer przy każdym żądaniu.
/// </summary>
public sealed class CompanyContext(ApiClient api)
{
    private Task? _loading;

    public IReadOnlyList<AccessibleCompanyDto> Companies { get; private set; } = [];

    public AccessibleCompanyDto? Active => Companies.FirstOrDefault(c => c.Active);

    /// <summary>Rola w bieżącej firmie; bez firmy - właściciel (zakłada własną firmę).</summary>
    public CompanyRole Role => Active?.Role ?? CompanyRole.Owner;

    public bool IsOwner => Role == CompanyRole.Owner;

    /// <summary>Wystawianie i wysyłka faktur, kontrahenci, katalog, e-maile.</summary>
    public bool CanIssue => Role is CompanyRole.Owner or CompanyRole.Employee;

    public Task EnsureLoadedAsync() => _loading ??= LoadAsync();

    public async Task LoadAsync()
    {
        Companies = (await api.GetAsync<List<AccessibleCompanyDto>>("api/account/companies")).Value ?? [];
    }

    public void Reset() => _loading = null;

    public static string RoleName(CompanyRole role) => role switch
    {
        CompanyRole.Owner => "właściciel",
        CompanyRole.Employee => "pracownik",
        _ => "księgowa"
    };
}
