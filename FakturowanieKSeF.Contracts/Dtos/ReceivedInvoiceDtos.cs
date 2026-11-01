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
    DateTime KsefAcceptedAt);

/// <summary>Faktura otrzymana: dane z KSeF i treść odczytana z XML.</summary>
public sealed record ReceivedInvoiceDto(
    int Id,
    string KsefNumber,
    DateTime KsefAcceptedAt,
    DateTime DownloadedAt,
    InvoiceDocumentDto? Document,
    string? DocumentError);

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
