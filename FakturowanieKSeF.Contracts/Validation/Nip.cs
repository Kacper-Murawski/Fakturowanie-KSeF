using System.ComponentModel.DataAnnotations;

namespace FakturowanieKSeF.Contracts.Validation;

/// <summary>Walidacja polskiego numeru NIP (10 cyfr, cyfra kontrolna liczona sumą ważoną modulo 11).</summary>
public static class NipValidator
{
    private static readonly int[] Weights = [6, 5, 7, 2, 3, 4, 5, 6, 7];

    /// <summary>Usuwa separatory (myślniki, spacje) oraz opcjonalny prefiks „PL”.</summary>
    public static string Normalize(string nip)
    {
        var s = nip.Trim().ToUpperInvariant();
        if (s.StartsWith("PL")) s = s[2..];
        return new string(s.Where(c => c is not ('-' or ' ')).ToArray());
    }

    public static bool IsValid(string? nip)
    {
        if (string.IsNullOrWhiteSpace(nip)) return false;

        var digits = Normalize(nip);
        if (digits.Length != 10 || !digits.All(char.IsAsciiDigit)) return false;

        var sum = 0;
        for (var i = 0; i < 9; i++) sum += (digits[i] - '0') * Weights[i];

        var checksum = sum % 11;
        return checksum != 10 && checksum == digits[9] - '0';
    }
}

/// <summary>
/// Atrybut walidacji NIP (puste wartości przepuszcza - wymagalność określa <see cref="RequiredAttribute"/>).
/// Działa zarówno w API, jak i w formularzach Blazor.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NipAttribute : ValidationAttribute
{
    public NipAttribute() : base("Nieprawidłowy numer NIP.") { }

    public override bool IsValid(object? value) =>
        value is null || value is string { Length: 0 } || (value is string s && NipValidator.IsValid(s));
}
