using System.Globalization;
using QRCoder;

namespace FakturowanieKSeF.Ksef;

/// <summary>
/// Kod QR weryfikacji faktury w KSeF (KOD I) - umieszczany na wizualizacji faktury (PDF, wydruk). Po zeskanowaniu
/// prowadzi do serwisu MF, który potwierdza obecność faktury w KSeF i pozwala pobrać jej XML.
/// Link: {adres QR środowiska}/invoice/{NIP sprzedawcy}/{data wystawienia DD-MM-RRRR}/{SHA-256 pliku faktury, Base64URL}.
/// Wyłącznie środowisko testowe (D8).
/// </summary>
public static class KsefQr
{
    /// <summary>Adres serwisu weryfikacji kodów QR środowiska testowego KSeF.</summary>
    public const string TestBaseUrl = "https://qr-test.ksef.mf.gov.pl";

    /// <summary>Oznaczenie pod kodem QR, gdy numer KSeF nie został jeszcze nadany.</summary>
    public const string OfflineLabel = "OFFLINE";

    public static string InvoiceVerificationUrl(string sellerNip, DateOnly issueDate, string sha256Base64)
    {
        var hash = Convert.FromBase64String(sha256Base64.Replace('-', '+').Replace('_', '/').PadRight((sha256Base64.Length + 3) / 4 * 4, '='));
        var base64Url = Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{TestBaseUrl}/invoice/{sellerNip}/{issueDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)}/{base64Url}";
    }

    /// <summary>Obraz PNG kodu QR (ISO/IEC 18004, poziom korekcji błędów M).</summary>
    public static byte[] GeneratePng(string url, int pixelsPerModule = 6)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }
}
