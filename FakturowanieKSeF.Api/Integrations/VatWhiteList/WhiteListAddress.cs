using System.Globalization;
using System.Text.RegularExpressions;
using FakturowanieKSeF.Contracts.Dtos;

namespace FakturowanieKSeF.Api.Integrations.VatWhiteList;

/// <summary>
/// Rozbiór adresu z wykazu podatników VAT na pola formularza. Wykaz zwraca adres jako jeden tekst
/// wielkimi literami, np. <c>"UL. KWIATOWA 1/2, 00-001 WARSZAWA"</c> albo <c>"ZAWADA 23, 39-200 DĘBICA"</c>.
/// </summary>
public static partial class WhiteListAddress
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    /// <summary>Skróty pisane małą literą (ul. Kwiatowa, al. Jana Pawła II).</summary>
    private static readonly HashSet<string> LowercasePrefixes = ["UL.", "AL.", "PL.", "OS.", "RONDO"];

    /// <summary>Adres jako pola formularza albo <c>null</c>, gdy tekstu nie da się jednoznacznie rozpoznać.</summary>
    public static AddressDto? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Ostatni przecinek oddziela ulicę od „kod pocztowy miejscowość”
        var comma = text.LastIndexOf(',');
        if (comma <= 0) return null;

        var street = text[..comma].Trim();
        var match = PostalAndCity().Match(text[(comma + 1)..].Trim());
        if (street.Length == 0 || !match.Success) return null;

        return new AddressDto
        {
            Street = ToTitle(street),
            PostalCode = match.Groups["postal"].Value,
            City = ToTitle(match.Groups["city"].Value),
            CountryCode = "PL"
        };
    }

    /// <summary>„JANA PAWŁA II 5” → „Jana Pawła II 5”, „UL. NOWY ŚWIAT 1” → „ul. Nowy Świat 1”.</summary>
    public static string ToTitle(string text) =>
        string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word =>
            LowercasePrefixes.Contains(word) ? word.ToLowerInvariant()
            : RomanNumeral().IsMatch(word) ? word
            : TitleWord(word)));

    // Myślnik w nazwach złożonych: „BIELSKO-BIAŁA” → „Bielsko-Biała”
    private static string TitleWord(string word) =>
        string.Join('-', word.Split('-').Select(part => Pl.TextInfo.ToTitleCase(part.ToLower(Pl))));

    [GeneratedRegex(@"^(?<postal>\d{2}-\d{3})\s+(?<city>.+)$")]
    private static partial Regex PostalAndCity();

    // Liczby rzymskie w nazwach ulic (np. Jana Pawła II, Bolesława III) pozostają wielkimi literami
    [GeneratedRegex(@"^(?=[IVXL]+$)X{0,3}(IX|IV|V?I{0,3})$")]
    private static partial Regex RomanNumeral();
}
