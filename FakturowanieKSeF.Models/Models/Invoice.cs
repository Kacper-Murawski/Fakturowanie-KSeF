using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FakturowanieKSeF.Models;

/// <summary>
/// Faktura sprzedażowa wystawiana przez firmę użytkownika (w MVP: faktura VAT podstawowa, RodzajFaktury = VAT).
/// Prawnie wiążącą postacią faktury jest XML przesłany do KSeF (<see cref="KsefSubmission.SentXml"/>);
/// rekord w bazie jest jego źródłem i odzwierciedleniem.
/// </summary>
public class Invoice : BaseEntity
{
    /// <summary>Numer faktury nadany przez wystawcę (FA(3): P_2), np. "FV/1/11/2026". Unikalny w ramach firmy.</summary>
    [Required, MaxLength(256)]
    public string Number { get; set; } = string.Empty;

    /// <summary>Data wystawienia (P_1).</summary>
    public DateOnly IssueDate { get; set; }

    /// <summary>Miejsce wystawienia (P_1M), opcjonalne.</summary>
    [MaxLength(100)]
    public string? IssuePlace { get; set; }

    /// <summary>Data sprzedaży / wykonania usługi (P_6), jeśli różna od daty wystawienia.</summary>
    public DateOnly? SaleDate { get; set; }

    /// <summary>Kod waluty ISO 4217 (KodWaluty).</summary>
    [Required, MaxLength(3)]
    public string Currency { get; set; } = "PLN";

    // --- Płatność ---

    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Transfer;

    public DateOnly? DueDate { get; set; }

    /// <summary>Numer rachunku bankowego (NrRB, 10-34 znaki).</summary>
    [MaxLength(34)]
    public string? BankAccountNumber { get; set; }

    // --- Strony faktury (kopie danych z chwili wystawienia) ---

    public InvoiceParty Seller { get; set; } = new();

    public InvoiceParty Buyer { get; set; } = new();

    // --- Sumy (wyliczane z pozycji przez serwis obliczeń; FA(3): P_13_x, P_14_x, P_15) ---

    [Column(TypeName = "numeric(18,2)")]
    public decimal TotalNet { get; set; }

    [Column(TypeName = "numeric(18,2)")]
    public decimal TotalVat { get; set; }

    [Column(TypeName = "numeric(18,2)")]
    public decimal TotalGross { get; set; }

    // --- Status i KSeF ---

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    /// <summary>Numer KSeF nadany po przyjęciu faktury (kopia z udanej wysyłki, dla szybkiego wyszukiwania).</summary>
    [MaxLength(40)]
    public string? KsefNumber { get; set; }

    /// <summary>Data nadania numeru KSeF - od niej faktura uznawana jest za wystawioną i otrzymaną.</summary>
    public DateTime? KsefAcceptedAt { get; set; }

    /// <summary>
    /// Znacznik współbieżności (kolumna systemowa xmin PostgreSQL). Chroni m.in. przed podwójną wysyłką
    /// tej samej faktury przy równoczesnych żądaniach.
    /// </summary>
    [Timestamp]
    public uint Version { get; set; }

    // --- Relacje ---

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>Kontrahent z książki adresowej, z którego skopiowano dane nabywcy (opcjonalny).</summary>
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public List<InvoiceItem> Items { get; set; } = [];

    /// <summary>Historia prób wysyłki do KSeF (np. odrzucenie, poprawka, ponowna wysyłka).</summary>
    public List<KsefSubmission> KsefSubmissions { get; set; } = [];
}
