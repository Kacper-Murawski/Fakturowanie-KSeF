namespace FakturowanieKSeF.Contracts.Validation;

/// <summary>
/// Adres powrotu po zalogowaniu (<c>/login?returnUrl=…</c>). Przyjmowana jest wyłącznie ścieżka wewnątrz aplikacji
/// względna wobec adresu bazowego (np. <c>faktury/5?status=Draft</c>) - inaczej link wysłany ofierze mógłby po
/// prawdziwym logowaniu przekierować ją na obcą stronę wyłudzającą dane (open redirect, SEC-2, D52).
/// Odrzucane są m.in. <c>https://obca</c>, <c>//obca</c>, <c>\\obca</c>, <c>/\obca</c>, <c>http:obca</c> (inny schemat
/// bez ukośników) i znaki sterujące, które przeglądarka po cichu usuwa z adresu.
/// </summary>
public static class ReturnUrl
{
    /// <summary>Bezpieczny adres względny albo pusty napis (= strona główna).</summary>
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2000) return "";

        // Ścieżka względna wobec adresu bazowego aplikacji - nie może zaczynać się od ukośnika (ani odwrotnego)
        if (value[0] is '/' or '\\') return "";

        foreach (var c in value)
        {
            // Dozwolone tylko znaki adresu URL bez schematu: litery i cyfry ASCII oraz -._~/?=&%#+,;!*'()@
            // Dwukropek (schemat „http:…”), odwrotny ukośnik, spacje i znaki sterujące - odrzucane
            var allowed = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'
                || "-._~/?=&%#+,;!*'()@".Contains(c);
            if (!allowed) return "";
        }

        return value;
    }
}
