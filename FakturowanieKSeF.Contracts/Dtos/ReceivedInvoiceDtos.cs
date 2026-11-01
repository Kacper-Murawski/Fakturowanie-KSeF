namespace FakturowanieKSeF.Contracts.Dtos;

/// <summary>Wiersz listy faktur otrzymanych (zakupowych) pobranych z KSeF.</summary>
public sealed record ReceivedInvoiceListItemDto(
    int Id,
    string KsefNumber,
    string InvoiceNumber,
    DateOnly IssueDate,
    string SellerNip,
    string SellerName,
    decimal TotalNet,
    decimal TotalVat,
    decimal TotalGross,
    string Currency,
    DateTime KsefAcceptedAt,
    bool? BankAccountOnWhiteList = null,
    DateOnly? DueDate = null,
    PaymentStatus PaymentStatus = PaymentStatus.Unpaid,
    decimal PaidAmount = 0,
    bool IsOverdue = false);

/// <summary>Faktura otrzymana: dane z KSeF i treść odczytana z XML.</summary>
public sealed record ReceivedInvoiceDto(
    int Id,
    string KsefNumber,
    DateTime KsefAcceptedAt,
    DateTime DownloadedAt,
    InvoiceDocumentDto? Document,
    string? DocumentError,
    BankAccountCheckDto? BankAccountCheck = null,
    PaymentInfoDto? Payment = null);

/// <summary>Wynik sprawdzenia rachunku z faktury w wykazie podatników VAT (Biała lista MF).</summary>
/// <param name="OnWhiteList">Czy rachunek jest przypisany do sprzedawcy w wykazie na dzień <paramref name="CheckedOn"/>.</param>
/// <param name="RequestId">Identyfikator zapytania MF - potwierdzenie sprawdzenia (warto go zachować przy przelewie).</param>
public sealed record BankAccountCheckDto(string Account, bool OnWhiteList, DateOnly CheckedOn, string? RequestId)
{
    /// <summary>
    /// Kwota brutto, powyżej której płatność między przedsiębiorcami musi przejść przez rachunek płatniczy,
    /// a zapłata na rachunek spoza wykazu ma skutki podatkowe (art. 19 Prawa przedsiębiorców, art. 15d ustawy o CIT,
    /// art. 22p ustawy o PIT, art. 117ba Ordynacji podatkowej).
    /// </summary>
    public const decimal ThresholdGross = 15_000m;
}

/// <summary>Strona faktury odczytana z XML (adres w postaci wierszy - tak jak w strukturze FA).</summary>
public sealed record DocumentPartyDto(string Name, string? Nip, string? AddressLine1, string? AddressLine2, string? CountryCode);

public sealed record DocumentLineDto(int Number, string Name, string? Unit, decimal? Quantity, decimal? UnitNetPrice, decimal? NetAmount, string? VatRate);

public sealed record DocumentVatSummaryDto(string Label, decimal Net, decimal? Vat);

/// <summary>Treść faktury odczytana z dokumentu XML w strukturze FA.</summary>
public sealed record InvoiceDocumentDto(
    string InvoiceNumber,
    string InvoiceType,
    DateOnly IssueDate,
    DateOnly? SaleDate,
    string Currency,
    DocumentPartyDto Seller,
    DocumentPartyDto Buyer,
    IReadOnlyList<DocumentLineDto> Lines,
    IReadOnlyList<DocumentVatSummaryDto> VatSummary,
    decimal TotalGross,
    DateOnly? DueDate,
    string? PaymentMethod,
    string? BankAccount);

/// <summary>Stan synchronizacji faktur otrzymanych.</summary>
public sealed record ReceivedSyncStatusDto(bool HasToken, DateTime? LastSyncAt, DateTime? SyncedUntil, string? LastError);

public sealed record SyncResultDto(int Downloaded, bool Complete);
