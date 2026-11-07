namespace FakturowanieKSeF.Contracts;

/// <summary>Rodzaj wiadomości e-mail wysyłanej przez aplikację.</summary>
public enum EmailKind
{
    /// <summary>Faktura wysłana nabywcy (PDF + XML w załączniku).</summary>
    Invoice,
    /// <summary>Przypomnienie o płatności dla nabywcy.</summary>
    PaymentReminder,
    /// <summary>Powiadomienie użytkownika o nowych fakturach kosztowych z KSeF.</summary>
    ReceivedInvoices,
    /// <summary>Powiadomienie użytkownika o przygotowanych fakturach cyklicznych.</summary>
    RecurringInvoices,
    /// <summary>Zaproszenie do firmy (D46).</summary>
    Invitation
}

/// <summary>Stan wiadomości w kolejce wysyłki.</summary>
public enum EmailStatus
{
    /// <summary>Oczekuje na wysyłkę (także po nieudanej próbie, przed kolejną).</summary>
    Pending,
    Sent,
    /// <summary>Nie udało się wysłać po wszystkich próbach.</summary>
    Failed
}
