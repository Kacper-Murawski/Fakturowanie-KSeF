using System.Text;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Ksef;
using FakturowanieKSeF.Ksef.Fa3;
using FakturowanieKSeF.Models;

namespace FakturowanieKSeF.Api.Services;

/// <summary>
/// Pliki faktury wystawionej: XML i wizualizacja PDF. Wspólne dla pobierania w aplikacji i załączników e-mail.
/// Dla faktury przekazanej do KSeF używany jest DOKŁADNIE wysłany XML (to on ma moc prawną).
/// </summary>
public static class InvoiceFiles
{
    /// <summary>Ostatnia wysyłka faktury przekazanej do KSeF (wysłana lub przyjęta), inaczej <c>null</c>.</summary>
    public static KsefSubmission? Submitted(Invoice invoice) =>
        invoice.Status is InvoiceStatus.Pending or InvoiceStatus.Accepted
            ? invoice.KsefSubmissions.OrderByDescending(s => s.Id).FirstOrDefault()
            : null;

    /// <summary>
    /// PDF faktury (wymaga załadowanych <see cref="Invoice.Items"/> i <see cref="Invoice.KsefSubmissions"/>).
    /// Faktura wysłana - wydruk wysłanego dokumentu z KODEM I; szkic lub odrzucona - wydruk z ostrzeżeniem.
    /// </summary>
    /// <exception cref="Fa3GenerationException">Szkicu nie da się przedstawić w strukturze FA(3).</exception>
    public static byte[] Pdf(Invoice invoice, Fa3XmlGenerator generator, InvoicePdfRenderer renderer)
    {
        if (Submitted(invoice) is { } submitted)
        {
            var qr = new PdfVerificationQr(
                KsefQr.InvoiceVerificationUrl(invoice.Seller.Nip!, invoice.IssueDate, submitted.InvoiceHash),
                submitted.KsefNumber ?? KsefQr.OfflineLabel);
            return renderer.Render(Fa3InvoiceReader.Read(submitted.SentXml), qr, null);
        }

        InvoiceDocumentDto document = Fa3InvoiceReader.Read(generator.Generate(invoice));
        var notice = invoice.Status == InvoiceStatus.Rejected
            ? "FAKTURA ODRZUCONA PRZEZ KSeF - dokument nie ma mocy prawnej faktury."
            : "SZKIC - dokument nie został wysłany do KSeF i nie jest fakturą.";
        return renderer.Render(document, null, notice);
    }

    /// <summary>XML wysłany do KSeF albo <c>null</c>, gdy faktury nie wysłano.</summary>
    public static byte[]? SentXml(Invoice invoice) =>
        Submitted(invoice) is { } s ? Encoding.UTF8.GetBytes(s.SentXml) : null;

    /// <summary>Nazwa pliku z numeru faktury, np. „FV_1_11_2026.pdf”.</summary>
    public static string FileName(string number, string extension) =>
        string.Concat(number.Select(c => char.IsLetterOrDigit(c) ? c : '_')) + "." + extension;
}
