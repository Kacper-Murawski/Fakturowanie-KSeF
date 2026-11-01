using System.Text.Json.Serialization;

namespace FakturowanieKSeF.Contracts;

/// <summary>Rodzaj faktury (pole RodzajFaktury struktury FA(3)) w zakresie obsługiwanym przez aplikację.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<InvoiceKind>))]
public enum InvoiceKind
{
    /// <summary>Faktura VAT podstawowa (VAT).</summary>
    Vat,

    /// <summary>Faktura korygująca (KOR) - zmienia kwoty lub pozycje faktury przyjętej w KSeF.</summary>
    Correction
}

/// <summary>Typ skutku korekty w ewidencji VAT (pole TypKorekty, TTypKorekty w FA(3)).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CorrectionType>))]
public enum CorrectionType
{
    /// <summary>Korekta skutkująca w dacie ujęcia faktury pierwotnej (np. błąd na fakturze).</summary>
    OriginalDate = 1,

    /// <summary>Korekta skutkująca w dacie wystawienia faktury korygującej (np. rabat, zwrot towaru).</summary>
    CorrectionDate = 2,

    /// <summary>Korekta skutkująca w innej dacie.</summary>
    OtherDate = 3
}
