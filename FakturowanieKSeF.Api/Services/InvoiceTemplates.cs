using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Models;

namespace FakturowanieKSeF.Api.Services;

/// <summary>
/// Nowa faktura na wzór istniejącej (faktury cykliczne D39, faktura VAT z proformy D42): nabywca, pozycje i warunki
/// płatności wzoru, nowe daty. Wynikowe żądanie przechodzi tę samą ścieżkę co formularz (<c>ApplyAsync</c>).
/// </summary>
public static class InvoiceTemplates
{
    /// <param name="customerExists">Kontrahent wzoru nadal jest w książce adresowej - wtedy faktura dostaje jego aktualne dane.</param>
    /// <param name="dueDate">Termin płatności; domyślnie ten sam odstęp od daty wystawienia co we wzorze.</param>
    public static SaveInvoiceRequest RequestFrom(Invoice template, bool customerExists, DateOnly issueDate, DateOnly? dueDate = null) => new()
    {
        IssueDate = issueDate,
        IssuePlace = template.IssuePlace,
        Currency = template.Currency,
        PaymentMethod = template.PaymentMethod,
        DueDate = dueDate ?? (template.DueDate is { } d ? issueDate.AddDays(d.DayNumber - template.IssueDate.DayNumber) : null),
        BankAccountNumber = template.BankAccountNumber,
        ExemptionBasis = template.ExemptionBasis,
        CustomerId = customerExists ? template.CustomerId : null,
        Buyer = customerExists ? null : new SaveInvoiceBuyerRequest
        {
            Name = template.Buyer.Name, Nip = template.Buyer.Nip, Address = template.Buyer.Address.ToDto()
        },
        Items = template.Items.Where(i => !i.BeforeCorrection).OrderBy(i => i.Position).Select(i => new SaveInvoiceItemRequest
        {
            Name = i.Name, Unit = i.Unit, Quantity = i.Quantity, UnitNetPrice = i.UnitNetPrice, VatRate = i.VatRate
        }).ToList()
    };
}
