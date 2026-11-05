using System.Globalization;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Ksef;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FakturowanieKSeF.Api.Services;

/// <summary>Kod QR weryfikacji (KOD I) z oznaczeniem: numer KSeF albo „OFFLINE”.</summary>
public sealed record PdfVerificationQr(string Url, string Label);

/// <summary>
/// Wizualizacja faktury w PDF (QuestPDF). Źródłem danych jest dokument XML w strukturze FA (odczytany przez
/// <c>Fa3InvoiceReader</c>) - ten sam renderer obsługuje faktury wystawione i otrzymane, a wydruk pokazuje dokładnie
/// treść dokumentu przekazanego do KSeF. Na fakturze przyjętej przez KSeF umieszczany jest kod QR weryfikacji (KOD I)
/// z numerem KSeF - wymagany, gdy faktura trafia do odbiorcy poza KSeF (PDF, wydruk, e-mail).
/// </summary>
public sealed class InvoicePdfRenderer
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    static InvoicePdfRenderer()
    {
        // Licencja Community: bezpłatna dla projektów akademickich i podmiotów z przychodem < 1 mln USD (D27)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Render(InvoiceDocumentDto doc, PdfVerificationQr? qr, string? notice)
    {
        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(32);
            page.DefaultTextStyle(t => t.FontSize(9));

            page.Header().Element(h => Header(h, doc, notice));
            page.Content().PaddingVertical(12).Element(c => Content(c, doc));
            page.Footer().Element(f => Footer(f, qr));
        })).WithMetadata(new DocumentMetadata
        {
            Title = $"Faktura {doc.InvoiceNumber}",
            Creator = "Fakturowanie KSeF (projekt inżynierski)",
            Subject = "Wizualizacja faktury ustrukturyzowanej FA(3)"
        }).GeneratePdf();
    }

    private static void Header(IContainer container, InvoiceDocumentDto doc, string? notice)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text($"Faktura {TypeLabel(doc.InvoiceType)}").FontSize(18).Bold();
                    c.Item().Text($"nr {doc.InvoiceNumber}").FontSize(12).SemiBold();
                });
                row.ConstantItem(200).AlignRight().Column(c =>
                {
                    c.Item().AlignRight().Text($"Data wystawienia: {Date(doc.IssueDate)}");
                    c.Item().AlignRight().Text($"Data sprzedaży: {Date(doc.SaleDate ?? doc.IssueDate)}");
                    c.Item().AlignRight().Text($"Waluta: {doc.Currency}");
                });
            });

            if (notice is not null)
                col.Item().PaddingTop(8).Background(Colors.Orange.Lighten4).Padding(6).Text(notice).Bold().FontColor(Colors.Orange.Darken4);

            // Faktura korygująca: której faktury dotyczy i dlaczego
            if (doc.IsCorrection)
            {
                col.Item().PaddingTop(8).Background(Colors.Grey.Lighten4).Padding(6).Column(c =>
                {
                    foreach (var k in doc.CorrectedInvoices ?? [])
                    {
                        c.Item().Text(t =>
                        {
                            t.Span("Korekta faktury nr ");
                            t.Span(k.Number).SemiBold();
                            if (k.IssueDate is { } d) t.Span($" z dnia {Date(d)}");
                            if (k.KsefNumber is not null)
                            {
                                t.Span(", numer KSeF ");
                                t.Span(k.KsefNumber).SemiBold();
                            }
                        });
                    }
                    if (doc.CorrectionReason is not null) c.Item().Text($"Przyczyna korekty: {doc.CorrectionReason}");
                });
            }

            col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
        });
    }

    private static void Content(IContainer container, InvoiceDocumentDto doc)
    {
        container.Column(col =>
        {
            col.Spacing(14);

            col.Item().Row(row =>
            {
                row.RelativeItem().Element(c => Party(c, "Sprzedawca", doc.Seller));
                row.ConstantItem(20);
                row.RelativeItem().Element(c => Party(c, "Nabywca", doc.Buyer));
            });

            if (doc.IsCorrection)
            {
                // Metoda „przed i po”: osobne zestawienia stanu przed korektą i po korekcie
                var before = doc.Lines.Where(l => l.BeforeCorrection).ToList();
                var after = doc.Lines.Where(l => !l.BeforeCorrection).ToList();
                col.Item().Column(c =>
                {
                    c.Item().Text("Stan przed korektą").SemiBold();
                    c.Item().Element(e => LinesTable(e, before));
                });
                col.Item().Column(c =>
                {
                    c.Item().Text("Stan po korekcie").SemiBold();
                    if (after.Count == 0) c.Item().Text("brak pozycji (korekta do zera)").Italic();
                    else c.Item().Element(e => LinesTable(e, after));
                });
            }
            else
            {
                col.Item().Element(e => LinesTable(e, doc.Lines));
            }

            col.Item().Row(row =>
            {
                row.RelativeItem().Element(c => Payment(c, doc));
                row.ConstantItem(20);
                row.ConstantItem(230).Column(c =>
                {
                    c.Item().Table(t =>
                    {
                        t.ColumnsDefinition(cd => { cd.RelativeColumn(); cd.ConstantColumn(70); cd.ConstantColumn(60); });
                        t.Header(h =>
                        {
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Stawka").SemiBold();
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text("Netto").SemiBold();
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text("VAT").SemiBold();
                        });
                        foreach (var s in doc.VatSummary)
                        {
                            Cell(t).Text(s.Label);
                            Cell(t).AlignRight().Text(s.Net.ToString("N2", Pl));
                            Cell(t).AlignRight().Text(s.Vat is { } v ? v.ToString("N2", Pl) : "—");
                        }
                    });
                    if (doc.IsCorrection) c.Item().PaddingTop(2).Text("Różnica (po korekcie - przed korektą)").FontSize(8).FontColor(Colors.Grey.Darken1);
                    c.Item().PaddingTop(8).AlignRight().Text(t =>
                    {
                        // Korekta: kwota ujemna oznacza zwrot dla nabywcy
                        var refund = doc.IsCorrection && doc.TotalGross < 0;
                        t.Span(refund ? "Do zwrotu: " : doc.IsCorrection ? "Do dopłaty: " : "Do zapłaty: ").FontSize(11);
                        t.Span($"{Math.Abs(doc.TotalGross).ToString("N2", Pl)} {doc.Currency}").FontSize(13).Bold();
                    });
                });
            });
        });
    }

    private static void LinesTable(IContainer container, IReadOnlyList<DocumentLineDto> lines)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(24);   // Lp.
                c.RelativeColumn(5);    // nazwa
                c.ConstantColumn(36);   // j.m.
                c.ConstantColumn(48);   // ilość
                c.ConstantColumn(64);   // cena netto
                c.ConstantColumn(70);   // wartość netto
                c.ConstantColumn(40);   // VAT
            });

            table.Header(h =>
            {
                foreach (var (label, right) in new[] { ("Lp.", false), ("Nazwa towaru lub usługi", false), ("J.m.", false),
                             ("Ilość", true), ("Cena netto", true), ("Wartość netto", true), ("VAT", false) })
                {
                    var cell = h.Cell().Background(Colors.Grey.Lighten3).Padding(4);
                    (right ? cell.AlignRight() : cell).Text(label).SemiBold();
                }
            });

            foreach (var line in lines)
            {
                Cell(table).Text(line.Number.ToString(Pl));
                Cell(table).Text(line.Name);
                Cell(table).Text(line.Unit ?? "");
                Cell(table).AlignRight().Text(line.Quantity is { } q ? q.ToString("0.######", Pl) : "");
                Cell(table).AlignRight().Text(line.UnitNetPrice is { } p ? p.ToString("N2", Pl) : "");
                Cell(table).AlignRight().Text(line.NetAmount is { } n ? n.ToString("N2", Pl) : "");
                Cell(table).Text(VatLabel(line.VatRate));
            }
        });
    }

    private static void Party(IContainer container, string title, DocumentPartyDto party)
    {
        container.Column(c =>
        {
            c.Item().Text(title.ToUpperInvariant()).FontSize(8).FontColor(Colors.Grey.Darken1).SemiBold();
            c.Item().PaddingTop(2).Text(party.Name).Bold();
            if (party.AddressLine1 is not null) c.Item().Text(party.AddressLine1);
            if (party.AddressLine2 is not null) c.Item().Text(party.AddressLine2);
            if (party.CountryCode is not null && party.CountryCode != "PL") c.Item().Text(party.CountryCode);
            if (party.Nip is not null) c.Item().Text($"NIP: {Nip(party.Nip)}");
        });
    }

    private static void Payment(IContainer container, InvoiceDocumentDto doc)
    {
        container.Column(c =>
        {
            c.Item().Text("PŁATNOŚĆ").FontSize(8).FontColor(Colors.Grey.Darken1).SemiBold();
            if (doc.PaymentMethod is not null) c.Item().Text($"Forma płatności: {doc.PaymentMethod}");
            if (doc.PaidOn is { } paid) c.Item().Text($"ZAPŁACONO dnia {Date(paid)}").SemiBold();
            if (doc.DueDate is { } due) c.Item().Text($"Termin płatności: {Date(due)}");
            if (doc.BankAccount is not null) c.Item().Text($"Rachunek: {BankAccount(doc.BankAccount)}");
        });
    }

    private static void Footer(IContainer container, PdfVerificationQr? qr)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
            col.Item().PaddingTop(6).Row(row =>
            {
                if (qr is not null)
                {
                    // Oznaczenie pod kodem (numer KSeF - 35 znaków) w jednej linii, zgodnie z wzorem MF
                    row.ConstantItem(160).Column(c =>
                    {
                        c.Item().AlignCenter().Width(100).Height(100).Image(KsefQr.GeneratePng(qr.Url));
                        c.Item().AlignCenter().Text(qr.Label).FontSize(7).SemiBold();
                    });
                    row.RelativeItem().PaddingLeft(8).AlignMiddle().Column(c =>
                    {
                        c.Item().Text("Weryfikacja faktury w KSeF (środowisko testowe)").SemiBold();
                        c.Item().Text("Zeskanuj kod QR, aby sprawdzić, czy faktura znajduje się w Krajowym Systemie e-Faktur, i pobrać jej wersję XML.");
                        c.Item().PaddingTop(2).Text(qr.Url).FontSize(6).FontColor(Colors.Grey.Darken1);
                    });
                }
                else
                {
                    row.RelativeItem().Text("Dokument nie został przyjęty w KSeF - brak kodu weryfikacji.").FontColor(Colors.Grey.Darken1);
                }
            });
            col.Item().PaddingTop(6).AlignCenter().Text(t =>
            {
                t.Span("Wygenerowano w aplikacji Fakturowanie KSeF (projekt inżynierski) - strona ").FontSize(7).FontColor(Colors.Grey.Medium);
                t.CurrentPageNumber().FontSize(7).FontColor(Colors.Grey.Medium);
                t.Span(" z ").FontSize(7).FontColor(Colors.Grey.Medium);
                t.TotalPages().FontSize(7).FontColor(Colors.Grey.Medium);
            });
        });
    }

    private static IContainer Cell(TableDescriptor table) =>
        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4);

    private static string Date(DateOnly date) => date.ToString("dd.MM.yyyy", Pl);

    private static string Nip(string nip) => nip.Length == 10 ? $"{nip[..3]}-{nip[3..6]}-{nip[6..8]}-{nip[8..]}" : nip;

    private static string BankAccount(string account) =>
        account.Length == 26 && account.All(char.IsAsciiDigit)
            ? account[..2] + " " + string.Join(' ', Enumerable.Range(0, 6).Select(i => account.Substring(2 + i * 4, 4)))
            : account;

    private static string TypeLabel(string type) => type switch
    {
        "VAT" => "VAT",
        "KOR" => "korygująca",
        "ZAL" => "zaliczkowa",
        "ROZ" => "rozliczeniowa",
        "UPR" => "uproszczona",
        _ => type
    };

    private static string VatLabel(string? code) => code switch
    {
        null => "",
        "zw" or "oo" or "np I" or "np II" => code,
        _ when code.StartsWith("0 ") => "0% " + code[2..],
        _ => code + "%"
    };
}
