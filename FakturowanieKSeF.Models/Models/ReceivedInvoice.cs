using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FakturowanieKSeF.Models;

/// <summary>
/// Faktura zakupowa - wystawiona przez inny podmiot na NIP firmy użytkownika i pobrana z KSeF (etap 7).
/// Źródłem prawdy jest XML pobrany z KSeF (<see cref="Xml"/>); pozostałe pola to dane wyciągnięte
/// z metadanych / XML na potrzeby list i wyszukiwania. Rekord tylko do odczytu.
/// </summary>
public class ReceivedInvoice
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>Numer KSeF - unikalny w ramach firmy (zapobiega podwójnemu pobraniu).</summary>
    [Required, MaxLength(40)]
    public string KsefNumber { get; set; } = string.Empty;

    /// <summary>Numer faktury nadany przez sprzedawcę (P_2).</summary>
    [Required, MaxLength(256)]
    public string InvoiceNumber { get; set; } = string.Empty;

    [Required, MaxLength(10)]
    public string SellerNip { get; set; } = string.Empty;

    [Required, MaxLength(250)]
    public string SellerName { get; set; } = string.Empty;

    public DateOnly IssueDate { get; set; }

    [Required, MaxLength(3)]
    public string Currency { get; set; } = "PLN";

    [Column(TypeName = "numeric(18,2)")]
    public decimal TotalNet { get; set; }

    [Column(TypeName = "numeric(18,2)")]
    public decimal TotalVat { get; set; }

    [Column(TypeName = "numeric(18,2)")]
    public decimal TotalGross { get; set; }

    /// <summary>Data przyjęcia faktury w KSeF (nadania numeru KSeF).</summary>
    public DateTime KsefAcceptedAt { get; set; }

    /// <summary>Skrót SHA-256 pliku faktury (Base64) z metadanych KSeF - do kodu QR weryfikacji na wydruku.</summary>
    [MaxLength(64)]
    public string? InvoiceHash { get; set; }

    /// <summary>Pełny XML faktury pobrany z KSeF.</summary>
    [Required]
    public string Xml { get; set; } = string.Empty;

    /// <summary>Moment pobrania faktury do aplikacji (UTC).</summary>
    public DateTime DownloadedAt { get; set; }
}
