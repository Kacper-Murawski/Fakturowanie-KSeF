using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Api.Services;
using FakturowanieKSeF.Contracts.Calculation;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Ksef.Fa3;
using FakturowanieKSeF.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FakturowanieKSeF.Api.Endpoints;

/// <summary>
/// Faktury sprzedażowe. Wszystkie zapytania są zawężone do firmy zalogowanego użytkownika.
/// Reguły statusów: edycja możliwa tylko dla Draft i Rejected; usunięcie tylko dla Draft i Rejected;
/// faktury Pending (w KSeF) i Accepted (z numerem KSeF) są nienaruszalne.
/// </summary>
public static class InvoiceEndpoints
{
    private const int MaxPageSize = 100;

    public static void MapInvoiceEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/invoices").WithTags("Faktury").RequireAuthorization();

        group.MapGet("/", async (
                InvoiceStatus? status, DateOnly? from, DateOnly? to, string? q, int? page, int? pageSize,
                CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var query = db.Invoices.AsNoTracking().Where(i => i.CompanyId == company.Id);
                if (status is not null) query = query.Where(i => i.Status == status);
                if (from is not null) query = query.Where(i => i.IssueDate >= from);
                if (to is not null) query = query.Where(i => i.IssueDate <= to);
                if (!string.IsNullOrWhiteSpace(q))
                {
                    var pattern = $"%{q.Trim()}%";
                    query = query.Where(i => EF.Functions.ILike(i.Number, pattern)
                                             || EF.Functions.ILike(i.Buyer.Name, pattern)
                                             || EF.Functions.ILike(i.Buyer.Nip!, pattern)
                                             || EF.Functions.ILike(i.KsefNumber!, pattern));
                }

                var p = Math.Max(page ?? 1, 1);
                var size = Math.Clamp(pageSize ?? 20, 1, MaxPageSize);
                var total = await query.CountAsync(ct);
                var items = await query
                    .OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.Id)
                    .Skip((p - 1) * size).Take(size)
                    .ToListAsync(ct);

                return Results.Ok(new PagedResult<InvoiceListItemDto>(items.Select(i => i.ToListItemDto()).ToList(), total, p, size));
            })
            .WithSummary("Lista faktur (filtry: status, zakres dat, wyszukiwanie; stronicowanie)");

        group.MapGet("/{id:int}", async (int id, CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var invoice = await db.Invoices.AsNoTracking().Include(i => i.Items)
                    .SingleOrDefaultAsync(i => i.Id == id && i.CompanyId == company.Id, ct);
                return invoice is null ? ApiErrors.NotFound("faktura") : Results.Ok(invoice.ToDto());
            })
            .WithSummary("Szczegóły faktury");

        group.MapGet("/{id:int}/xml", async (int id, CurrentCompany current, AppDbContext db,
                Fa3XmlGenerator generator, Fa3SchemaValidator validator, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var invoice = await db.Invoices.AsNoTracking().Include(i => i.Items)
                    .SingleOrDefaultAsync(i => i.Id == id && i.CompanyId == company.Id, ct);
                if (invoice is null) return ApiErrors.NotFound("faktura");

                byte[] xml;
                try
                {
                    xml = generator.GenerateBytes(invoice);
                }
                catch (Fa3GenerationException ex)
                {
                    return Results.Problem(title: "Faktury nie można przedstawić w strukturze FA(3).", detail: ex.Message,
                        statusCode: StatusCodes.Status422UnprocessableEntity);
                }

                // Każdy wygenerowany dokument jest sprawdzany względem oficjalnego schematu przed wydaniem
                var validation = validator.Validate(xml);
                if (!validation.IsValid)
                    return Results.Problem(title: "Wygenerowany XML nie jest zgodny ze schematem FA(3).",
                        detail: string.Join(Environment.NewLine, validation.Errors),
                        statusCode: StatusCodes.Status422UnprocessableEntity);

                var fileName = string.Concat(invoice.Number.Select(c => char.IsLetterOrDigit(c) ? c : '_')) + ".xml";
                return Results.File(xml, "application/xml", fileName);
            })
            .WithSummary("XML faktury w strukturze FA(3), zweryfikowany względem schematu XSD");

        group.MapGet("/next-number", async (DateOnly? issueDate, CurrentCompany current, InvoiceNumbering numbering, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var number = await numbering.NextAsync(company.Id, issueDate ?? DateOnly.FromDateTime(DateTime.Today), ct);
                return Results.Ok(new { number });
            })
            .WithSummary("Propozycja kolejnego numeru faktury");

        group.MapPost("/", async (SaveInvoiceRequest request, CurrentCompany current, AppDbContext db, InvoiceNumbering numbering, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var autoNumber = string.IsNullOrWhiteSpace(request.Number);

                // Przy numeracji automatycznej równoczesne utworzenie dwóch faktur może dać ten sam numer -
                // indeks unikalny to wykryje, a my nadajemy kolejny numer i ponawiamy zapis.
                for (var attempt = 1; ; attempt++)
                {
                    var invoice = new Invoice { CompanyId = company.Id };
                    var error = await ApplyAsync(invoice, request, company, db, ct);
                    if (error is not null) return error;

                    if (autoNumber) invoice.Number = await numbering.NextAsync(company.Id, invoice.IssueDate, ct);

                    db.Invoices.Add(invoice);
                    try
                    {
                        await db.SaveChangesAsync(ct);
                        return Results.Created($"/api/invoices/{invoice.Id}", invoice.ToDto());
                    }
                    catch (DbUpdateException ex) when (autoNumber && attempt < 3 && IsDuplicateNumber(ex))
                    {
                        db.ChangeTracker.Clear();
                    }
                }
            })
            .WithSummary("Utworzenie faktury (szkicu)");

        group.MapPut("/{id:int}", async (int id, SaveInvoiceRequest request, CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var invoice = await db.Invoices.Include(i => i.Items)
                    .SingleOrDefaultAsync(i => i.Id == id && i.CompanyId == company.Id, ct);
                if (invoice is null) return ApiErrors.NotFound("faktura");

                if (invoice.Status is not (InvoiceStatus.Draft or InvoiceStatus.Rejected))
                    return ApiErrors.Conflict("Faktury wysłanej do KSeF nie można edytować.");

                if (request.Version is null)
                    return ApiErrors.Validation(nameof(request.Version), "Brak wersji faktury - odśwież dane i spróbuj ponownie.");

                // Kontrola współbieżności: zapis tylko wtedy, gdy nikt nie zmienił faktury od jej odczytu
                db.Entry(invoice).Property(i => i.Version).OriginalValue = request.Version.Value;

                var error = await ApplyAsync(invoice, request, company, db, ct);
                if (error is not null) return error;

                invoice.Status = InvoiceStatus.Draft; // poprawiona faktura odrzucona wraca do szkicu

                await db.SaveChangesAsync(ct);
                return Results.Ok(invoice.ToDto());
            })
            .WithSummary("Edycja faktury (tylko szkic lub faktura odrzucona przez KSeF)");

        group.MapDelete("/{id:int}", async (int id, CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == id && i.CompanyId == company.Id, ct);
                if (invoice is null) return ApiErrors.NotFound("faktura");

                if (invoice.Status is not (InvoiceStatus.Draft or InvoiceStatus.Rejected))
                    return ApiErrors.Conflict("Faktury wysłanej do KSeF nie można usunąć.");

                db.Invoices.Remove(invoice); // miękkie usunięcie
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .WithSummary("Usunięcie faktury (tylko szkic lub faktura odrzucona przez KSeF)");
    }

    /// <summary>
    /// Przenosi dane z żądania na encję: kopiuje aktualne dane sprzedawcy (firmy) i nabywcy, zastępuje pozycje
    /// i przelicza kwoty wspólnym kalkulatorem. Zwraca błąd albo null.
    /// </summary>
    private static async Task<IResult?> ApplyAsync(Invoice invoice, SaveInvoiceRequest request, Company company,
        AppDbContext db, CancellationToken ct)
    {
        // Nabywca: z książki adresowej (tylko kontrahent własnej firmy!) albo podany bezpośrednio
        InvoiceParty buyer;
        if (request.CustomerId is { } customerId)
        {
            var customer = await db.Customers.AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == customerId && c.CompanyId == company.Id, ct);
            if (customer is null) return ApiErrors.Validation(nameof(request.CustomerId), "Nie znaleziono wskazanego kontrahenta.");

            buyer = new InvoiceParty { Name = customer.Name, Nip = customer.Nip, Address = customer.Address.Copy() };
        }
        else
        {
            var b = request.Buyer!;
            buyer = new InvoiceParty { Name = b.Name.Trim(), Nip = Mapping.NormalizeNip(b.Nip), Address = b.Address.ToEntity() };
        }

        if (!string.IsNullOrWhiteSpace(request.Number)) invoice.Number = request.Number.Trim();
        invoice.IssueDate = request.IssueDate;
        invoice.IssuePlace = Mapping.EmptyToNull(request.IssuePlace);
        invoice.SaleDate = request.SaleDate;
        invoice.Currency = request.Currency;
        invoice.PaymentMethod = request.PaymentMethod;
        invoice.DueDate = request.DueDate;
        invoice.ExemptionBasis = request.Items.Any(i => i.VatRate == VatRate.Exempt)
            ? Mapping.EmptyToNull(request.ExemptionBasis)
            : null;
        invoice.BankAccountNumber = Mapping.NormalizeBankAccount(request.BankAccountNumber)
                                    ?? (request.PaymentMethod == PaymentMethod.Transfer ? company.BankAccountNumber : null);

        // Kopia aktualnych danych sprzedawcy - utrwalana w chwili wysyłki faktury do KSeF
        invoice.Seller = new InvoiceParty { Name = company.Name, Nip = company.Nip, Address = company.Address.Copy() };
        invoice.Buyer = buyer;
        invoice.CustomerId = request.CustomerId;

        // Pozycje i kwoty
        var totals = InvoiceCalculator.Calculate(request.Items.Select(i => new LineInput(i.Quantity, i.UnitNetPrice, i.VatRate)));

        invoice.Items.Clear();
        for (var n = 0; n < request.Items.Count; n++)
        {
            var item = request.Items[n];
            var line = totals.Lines[n];
            invoice.Items.Add(new InvoiceItem
            {
                Position = n + 1,
                Name = item.Name.Trim(),
                Unit = Mapping.EmptyToNull(item.Unit),
                Quantity = item.Quantity,
                UnitNetPrice = item.UnitNetPrice,
                VatRate = item.VatRate,
                NetAmount = line.NetAmount,
                VatAmount = line.VatAmount,
                GrossAmount = line.GrossAmount
            });
        }

        invoice.TotalNet = totals.TotalNet;
        invoice.TotalVat = totals.TotalVat;
        invoice.TotalGross = totals.TotalGross;

        return null;
    }

    private static bool IsDuplicateNumber(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Invoices_CompanyId_Number" };
}
