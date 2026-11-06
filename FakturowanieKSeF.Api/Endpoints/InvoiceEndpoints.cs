using FakturowanieKSeF.Api.Integrations.Nbp;
using FakturowanieKSeF.Contracts.Calculation;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Infrastructure;
using FakturowanieKSeF.Api.Services;
using FakturowanieKSeF.Contracts.Calculation;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Ksef;
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
        var group = api.MapGroup("/invoices").WithTags("Faktury").RequireAuthorization()
            .RequireRoleForChanges(CompanyRoles.Issuers); // księgowa - tylko odczyt (D46)

        group.MapGet("/", async (
                InvoiceStatus? status, PaymentFilter? payment, DateOnly? from, DateOnly? to, string? q, int? page, int? pageSize,
                CurrentCompany current, AppDbContext db, TimeProvider time, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var query = db.Invoices.AsNoTracking().Where(i => i.CompanyId == company.Id);
                if (status is not null) query = query.Where(i => i.Status == status);

                // Rozliczenie: tylko faktury przekazane do KSeF (szkice i odrzucone nie są należnością)
                var today = PolishDate.Today(time);
                // Kwota należna = brutto faktury VAT + korekty przekazane do KSeF (wyrażenie tłumaczone na podzapytanie SQL)
                if (payment is not null)
                {
                    query = query.Where(i => i.Kind == InvoiceKind.Vat && (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.Accepted));
                    query = payment switch
                    {
                        PaymentFilter.Paid => query.Where(i => i.Payments.Sum(p => p.Amount) >= i.TotalGross
                            + i.Corrections.Where(c => c.Status == InvoiceStatus.Pending || c.Status == InvoiceStatus.Accepted).Sum(c => c.TotalGross)),
                        PaymentFilter.Unpaid => query.Where(i => i.Payments.Sum(p => p.Amount) < i.TotalGross
                            + i.Corrections.Where(c => c.Status == InvoiceStatus.Pending || c.Status == InvoiceStatus.Accepted).Sum(c => c.TotalGross)),
                        _ => query.Where(i => i.DueDate < today && i.Payments.Sum(p => p.Amount) < i.TotalGross
                            + i.Corrections.Where(c => c.Status == InvoiceStatus.Pending || c.Status == InvoiceStatus.Accepted).Sum(c => c.TotalGross))
                    };
                }
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
                var rows = await query
                    .OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.Id)
                    .Skip((p - 1) * size).Take(size)
                    .Select(i => new
                    {
                        Invoice = i,
                        Paid = i.Payments.Sum(x => x.Amount),
                        Due = i.TotalGross + i.Corrections
                            .Where(c => c.Status == InvoiceStatus.Pending || c.Status == InvoiceStatus.Accepted).Sum(c => c.TotalGross)
                    })
                    .ToListAsync(ct);

                return Results.Ok(new PagedResult<InvoiceListItemDto>(
                    rows.Select(r => r.Invoice.ToListItemDto(r.Paid, today, r.Due)).ToList(), total, p, size));
            })
            .WithSummary("Lista faktur (filtry: status, rozliczenie, zakres dat, wyszukiwanie; stronicowanie)");

        group.MapGet("/{id:int}", async (int id, CurrentCompany current, AppDbContext db, TimeProvider time, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var invoice = await db.Invoices.AsNoTracking().Include(i => i.Items).Include(i => i.KsefSubmissions)
                    .Include(i => i.Payments).Include(i => i.Corrections).Include(i => i.Proforma)
                    .AsSplitQuery()
                    .SingleOrDefaultAsync(i => i.Id == id && i.CompanyId == company.Id, ct);
                if (invoice is null) return ApiErrors.NotFound("faktura");

                // Proforma: faktura VAT wystawiona na jej podstawie (D42)
                var issued = invoice.Kind == InvoiceKind.Proforma
                    ? await db.Invoices.AsNoTracking().Where(i => i.ProformaId == invoice.Id && i.CompanyId == company.Id)
                        .Select(i => new LinkedDocumentDto(i.Id, i.Number, i.Status)).FirstOrDefaultAsync(ct)
                    : null;
                return Results.Ok(invoice.ToDto(PolishDate.Today(time)) with
                {
                    Proforma = invoice.Proforma is { } p ? new LinkedDocumentDto(p.Id, p.Number, p.Status) : null,
                    IssuedFromProforma = issued
                });
            })
            .WithSummary("Szczegóły faktury");

        group.MapGet("/{id:int}/xml", async (int id, CurrentCompany current, AppDbContext db,
                Fa3XmlGenerator generator, Fa3SchemaValidator validator, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var invoice = await db.Invoices.AsNoTracking().Include(i => i.Items).Include(i => i.KsefSubmissions)
                    .SingleOrDefaultAsync(i => i.Id == id && i.CompanyId == company.Id, ct);
                if (invoice is null) return ApiErrors.NotFound("faktura");
                if (invoice.Kind == InvoiceKind.Proforma)
                    return ApiErrors.Conflict("Faktura proforma nie jest fakturą ustrukturyzowaną - nie ma postaci XML FA(3).");

                var fileName = string.Concat(invoice.Number.Select(c => char.IsLetterOrDigit(c) ? c : '_')) + ".xml";

                // Faktura wysłana do KSeF: zwracamy DOKŁADNIE wysłany dokument (to on ma moc prawną), a nie nowo wygenerowany
                if (invoice.Status is InvoiceStatus.Pending or InvoiceStatus.Accepted
                    && invoice.KsefSubmissions.OrderByDescending(x => x.Id).FirstOrDefault() is { } submitted)
                    return Results.File(System.Text.Encoding.UTF8.GetBytes(submitted.SentXml), "application/xml", fileName);

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

                return Results.File(xml, "application/xml", fileName);
            })
            .WithSummary("XML faktury w strukturze FA(3), zweryfikowany względem schematu XSD");

        group.MapPost("/{id:int}/send", async (int id, SendInvoiceRequest request, CurrentCompany current, AppDbContext db,
                Fa3XmlGenerator generator, Fa3SchemaValidator validator, KsefWorkSignal signal, TimeProvider time, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var invoice = await db.Invoices.Include(i => i.Items).Include(i => i.KsefSubmissions).Include(i => i.Payments)
                    .SingleOrDefaultAsync(i => i.Id == id && i.CompanyId == company.Id, ct);
                if (invoice is null) return ApiErrors.NotFound("faktura");

                if (invoice.Kind == InvoiceKind.Proforma)
                    return ApiErrors.Conflict("Faktura proforma nie jest wysyłana do KSeF - wystaw na jej podstawie fakturę VAT.");

                if (invoice.Status is not (InvoiceStatus.Draft or InvoiceStatus.Rejected))
                    return ApiErrors.Conflict("Faktura została już wysłana do KSeF.");

                if (company.KsefTokenEncrypted is null)
                    return ApiErrors.Conflict("Najpierw skonfiguruj token KSeF w ustawieniach firmy.");

                if (invoice.Kind == InvoiceKind.Correction && await CheckCorrectionStateAsync(db, invoice, ct) is { } outdated)
                    return outdated;

                // Kontrola współbieżności: chroni przed podwójną wysyłką (np. dwukrotne kliknięcie, dwie karty)
                db.Entry(invoice).Property(i => i.Version).OriginalValue = request.Version;

                // Chwila wysyłki = chwila wystawienia: utrwalamy aktualne dane sprzedawcy
                invoice.Seller = new InvoiceParty { Name = company.Name, Nip = company.Nip, Address = company.Address.Copy() };

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

                var validation = validator.Validate(xml);
                if (!validation.IsValid)
                    return Results.Problem(title: "Wygenerowany XML nie jest zgodny ze schematem FA(3).",
                        detail: string.Join(Environment.NewLine, validation.Errors), statusCode: StatusCodes.Status422UnprocessableEntity);

                invoice.KsefSubmissions.Add(new KsefSubmission
                {
                    SentAt = time.GetUtcNow().UtcDateTime,
                    SentXml = System.Text.Encoding.UTF8.GetString(xml),
                    InvoiceHash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(xml))
                });
                invoice.Status = InvoiceStatus.Pending;
                RecordPaymentFromInvoice(db, invoice, time);

                await db.SaveChangesAsync(ct);
                signal.Pulse(); // usługa w tle wysyła fakturę od razu

                return Results.Accepted($"/api/invoices/{invoice.Id}", invoice.ToDto());
            })
            .WithSummary("Wysłanie faktury do KSeF (asynchronicznie; status Pending do czasu odpowiedzi KSeF)");

        group.MapGet("/{id:int}/upo", async (int id, CurrentCompany current, AppDbContext db, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var upo = await db.KsefSubmissions.AsNoTracking()
                    .Where(s => s.InvoiceId == id && s.Invoice.CompanyId == company.Id && s.UpoXml != null)
                    .OrderByDescending(s => s.Id)
                    .Select(s => new { s.UpoXml, s.KsefNumber })
                    .FirstOrDefaultAsync(ct);

                return upo is null
                    ? ApiErrors.NotFound("UPO (faktura nie została jeszcze przyjęta przez KSeF lub UPO nie jest jeszcze dostępne)")
                    : Results.File(System.Text.Encoding.UTF8.GetBytes(upo.UpoXml!), "application/xml", $"UPO_{upo.KsefNumber}.xml");
            })
            .WithSummary("UPO - urzędowe poświadczenie odbioru faktury przez KSeF");

        group.MapGet("/{id:int}/pdf", async (int id, CurrentCompany current, AppDbContext db, Fa3XmlGenerator generator,
                InvoicePdfRenderer renderer, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var invoice = await db.Invoices.AsNoTracking().Include(i => i.Items).Include(i => i.KsefSubmissions)
                    .SingleOrDefaultAsync(i => i.Id == id && i.CompanyId == company.Id, ct);
                if (invoice is null) return ApiErrors.NotFound("faktura");

                byte[] pdf;
                try
                {
                    pdf = InvoiceFiles.Pdf(invoice, generator, renderer);
                }
                catch (Fa3GenerationException ex)
                {
                    return Results.Problem(title: "Nie można przygotować wydruku faktury.", detail: ex.Message,
                        statusCode: StatusCodes.Status422UnprocessableEntity);
                }
                return Results.File(pdf, "application/pdf", InvoiceFiles.FileName(invoice.Number, "pdf"));
            })
            .WithSummary("Wizualizacja faktury w PDF (z kodem QR weryfikacji KSeF dla faktur wysłanych)");

        group.MapPost("/{id:int}/convert", async (int id, CurrentCompany current, AppDbContext db, InvoiceNumbering numbering,
                ExchangeRateLookup rates, TimeProvider time, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var proforma = await db.Invoices.AsNoTracking().Include(i => i.Items)
                    .SingleOrDefaultAsync(i => i.Id == id && i.CompanyId == company.Id, ct);
                if (proforma is null) return ApiErrors.NotFound("faktura proforma");
                if (proforma.Kind != InvoiceKind.Proforma)
                    return ApiErrors.Conflict("Na fakturę VAT można zamienić tylko fakturę proformę.");
                if (await db.Invoices.Where(i => i.ProformaId == id && i.CompanyId == company.Id).Select(i => i.Number).FirstOrDefaultAsync(ct) is { } existing)
                    return ApiErrors.Conflict($"Na podstawie tej proformy wystawiono już fakturę {existing}.");

                var today = PolishDate.Today(time);
                var customerExists = proforma.CustomerId is { } cid && await db.Customers.AnyAsync(c => c.Id == cid && c.CompanyId == company.Id, ct);
                // Termin z proformy, jeśli jeszcze nie minął; inaczej ten sam odstęp od dzisiejszej daty
                var due = proforma.DueDate is { } d && d >= today ? d : (DateOnly?)null;
                var request = InvoiceTemplates.RequestFrom(proforma, customerExists, today, due);

                for (var attempt = 1; ; attempt++)
                {
                    var invoice = new Invoice { CompanyId = company.Id, ProformaId = proforma.Id };
                    if (await ApplyAsync(invoice, request, company, db, rates, ct) is { } error) return error;
                    invoice.Number = await numbering.NextAsync(company.Id, today, InvoiceKind.Vat, ct);
                    db.Invoices.Add(invoice);
                    try
                    {
                        await db.SaveChangesAsync(ct);
                        return Results.Created($"/api/invoices/{invoice.Id}", invoice.ToDto());
                    }
                    catch (DbUpdateException ex) when (attempt < 3 && IsDuplicateNumber(ex))
                    {
                        db.ChangeTracker.Clear();
                    }
                }
            })
            .WithSummary("Faktura VAT (szkic) na podstawie faktury proformy - D42");

        group.MapPost("/import-from-ksef", async (CurrentCompany current, IssuedInvoiceImport import, TimeProvider time, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();
                if (company.KsefTokenEncrypted is null)
                    return ApiErrors.Conflict("Najpierw skonfiguruj token KSeF w ustawieniach firmy.");
                if (company.IssuedInvoicesLastSyncAt is { } last && time.GetUtcNow().UtcDateTime - last < TimeSpan.FromMinutes(1))
                    return Results.Problem(title: "Pobieranie było uruchomione przed chwilą. Spróbuj ponownie za minutę.",
                        statusCode: StatusCodes.Status429TooManyRequests);

                var result = await import.SyncAsync(company, ct);
                return company.IssuedInvoicesLastSyncError is { } error
                    ? Results.Problem(title: "Pobieranie faktur z KSeF nie powiodło się.", detail: error, statusCode: StatusCodes.Status502BadGateway)
                    : Results.Ok(result);
            })
            .WithSummary("Pobranie z KSeF faktur sprzedażowych wystawionych poza aplikacją (nie częściej niż raz na minutę) - D44");

        group.MapGet("/next-number", async (DateOnly? issueDate, InvoiceKind? kind, CurrentCompany current, InvoiceNumbering numbering, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var number = await numbering.NextAsync(company.Id, issueDate ?? DateOnly.FromDateTime(DateTime.Today), kind ?? InvoiceKind.Vat, ct);
                return Results.Ok(new { number });
            })
            .WithSummary("Propozycja kolejnego numeru faktury");

        group.MapPost("/", async (SaveInvoiceRequest request, CurrentCompany current, AppDbContext db, InvoiceNumbering numbering,
                ExchangeRateLookup rates, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var autoNumber = string.IsNullOrWhiteSpace(request.Number);

                // Przy numeracji automatycznej równoczesne utworzenie dwóch faktur może dać ten sam numer -
                // indeks unikalny to wykryje, a my nadajemy kolejny numer i ponawiamy zapis.
                for (var attempt = 1; ; attempt++)
                {
                    var invoice = new Invoice { CompanyId = company.Id };
                    var error = await ApplyAsync(invoice, request, company, db, rates, ct);
                    if (error is not null) return error;

                    if (autoNumber) invoice.Number = await numbering.NextAsync(company.Id, invoice.IssueDate, invoice.Kind, ct);

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

        group.MapPut("/{id:int}", async (int id, SaveInvoiceRequest request, CurrentCompany current, AppDbContext db,
                ExchangeRateLookup rates, CancellationToken ct) =>
            {
                if (await current.FindAsync(ct) is not { } company) return ApiErrors.CompanyRequired();

                var invoice = await db.Invoices.Include(i => i.Items).Include(i => i.KsefSubmissions)
                    .SingleOrDefaultAsync(i => i.Id == id && i.CompanyId == company.Id, ct);
                if (invoice is null) return ApiErrors.NotFound("faktura");

                if (invoice.Status is not (InvoiceStatus.Draft or InvoiceStatus.Rejected))
                    return ApiErrors.Conflict("Faktury wysłanej do KSeF nie można edytować.");

                if (request.Version is null)
                    return ApiErrors.Validation(nameof(request.Version), "Brak wersji faktury - odśwież dane i spróbuj ponownie.");

                // Kontrola współbieżności: zapis tylko wtedy, gdy nikt nie zmienił faktury od jej odczytu
                db.Entry(invoice).Property(i => i.Version).OriginalValue = request.Version.Value;

                var error = await ApplyAsync(invoice, request, company, db, rates, ct);
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

                if (await db.RecurringInvoices.AnyAsync(r => r.TemplateInvoiceId == invoice.Id, ct))
                    return ApiErrors.Conflict("Ta faktura jest wzorem faktury cyklicznej - najpierw usuń harmonogram.");

                db.Invoices.Remove(invoice); // miękkie usunięcie
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .WithSummary("Usunięcie faktury (tylko szkic lub faktura odrzucona przez KSeF)");
    }

    /// <summary>
    /// Przenosi dane z żądania na encję: kopiuje aktualne dane sprzedawcy (firmy) i nabywcy, zastępuje pozycje
    /// i przelicza kwoty wspólnym kalkulatorem. Dla faktury korygującej kopiuje dane faktury korygowanej i stan
    /// przed korektą (wiersze StanPrzed), a kwoty faktury to różnica po - przed. Zwraca błąd albo null.
    /// </summary>
    /// <summary>
    /// Zapłata oznaczona w XML faktury (Zaplacono) staje się płatnością w rozliczeniu. Wcześniejszy zapis z odrzuconej
    /// wysyłki jest zastępowany - ponowna wysyłka po poprawkach nie dubluje płatności (D33).
    /// </summary>
    private static void RecordPaymentFromInvoice(AppDbContext db, Invoice invoice, TimeProvider time)
    {
        foreach (var old in invoice.Payments.Where(p => p.FromInvoice).ToList())
        {
            db.Payments.Remove(old);
            invoice.Payments.Remove(old);
        }
        if (invoice.PaidOn is not { } paidOn || invoice.Kind != InvoiceKind.Vat) return;

        invoice.Payments.Add(new Payment
        {
            CompanyId = invoice.CompanyId,
            PaidOn = paidOn,
            Amount = invoice.TotalGross,
            Note = "Zapłacono przy wystawieniu (oznaczone w fakturze w KSeF)",
            FromInvoice = true,
            CreatedAt = time.GetUtcNow().UtcDateTime
        });
    }

    internal static async Task<IResult?> ApplyAsync(Invoice invoice, SaveInvoiceRequest request, Company company,
        AppDbContext db, ExchangeRateLookup rates, CancellationToken ct)
    {
        var kind = request.IsCorrection ? InvoiceKind.Correction : request.IsProforma ? InvoiceKind.Proforma : InvoiceKind.Vat;
        if (invoice.Id != 0 && (invoice.Kind != kind || invoice.CorrectedInvoiceId != request.CorrectedInvoiceId))
            return ApiErrors.Validation(nameof(request.CorrectedInvoiceId), "Nie można zmienić rodzaju faktury ani faktury korygowanej.");

        InvoiceParty buyer;
        List<InvoiceItem>? before = null;
        string? originalExemptionBasis = null;

        if (request.CorrectedInvoiceId is { } correctedId)
        {
            var original = await db.Invoices.AsNoTracking().Include(i => i.Items)
                .SingleOrDefaultAsync(i => i.Id == correctedId && i.CompanyId == company.Id, ct);
            if (original is null)
                return ApiErrors.Validation(nameof(request.CorrectedInvoiceId), "Nie znaleziono faktury korygowanej.");
            if (original.Kind != InvoiceKind.Vat)
                return ApiErrors.Validation(nameof(request.CorrectedInvoiceId), "Kolejną korektę wystaw do faktury pierwotnej, a nie do korekty.");
            if (original.Status != InvoiceStatus.Accepted)
                return ApiErrors.Validation(nameof(request.CorrectedInvoiceId), "Korygować można wyłącznie fakturę przyjętą w KSeF (z numerem KSeF).");
            if (request.IssueDate < original.IssueDate)
                return ApiErrors.Validation(nameof(request.IssueDate), "Data wystawienia korekty nie może być wcześniejsza niż data faktury korygowanej.");

            var state = await CurrentStateAsync(db, original, invoice.Id, ct);
            if (state.PendingCorrection)
                return ApiErrors.Conflict("Poprzednia korekta tej faktury jest przetwarzana przez KSeF. Wystaw kolejną po jej przyjęciu.");
            before = state.Items;

            // Korekta „bez zmian” nie jest korektą
            if (SameItems(before, request.Items))
                return ApiErrors.Validation(nameof(request.Items), "Pozycje po korekcie są takie same jak przed korektą - zmień co najmniej jedną pozycję.");

            buyer = new InvoiceParty { Name = original.Buyer.Name, Nip = original.Buyer.Nip, Address = original.Buyer.Address.Copy() };
            invoice.CorrectedInvoiceId = original.Id;
            invoice.CorrectedInvoiceNumber = original.Number;
            invoice.CorrectedInvoiceIssueDate = original.IssueDate;
            invoice.CorrectedInvoiceKsefNumber = original.KsefNumber;
            invoice.CorrectionReason = request.CorrectionReason!.Trim();
            invoice.CorrectionType = request.CorrectionType;
            invoice.CustomerId = original.CustomerId;
            invoice.Currency = original.Currency;
            // Korekta faktury walutowej - kurs faktury korygowanej (D43)
            invoice.ExchangeRate = original.ExchangeRate;
            invoice.ExchangeRateDate = original.ExchangeRateDate;
            invoice.ExchangeRateTable = original.ExchangeRateTable;
            originalExemptionBasis = original.ExemptionBasis;
        }
        else
        {
            // Nabywca: z książki adresowej (tylko kontrahent własnej firmy!) albo podany bezpośrednio
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
            invoice.CustomerId = request.CustomerId;
            invoice.Currency = request.Currency;
        }

        invoice.Kind = kind;
        if (!string.IsNullOrWhiteSpace(request.Number)) invoice.Number = request.Number.Trim();
        invoice.IssueDate = request.IssueDate;
        invoice.IssuePlace = Mapping.EmptyToNull(request.IssuePlace);
        invoice.SaleDate = request.SaleDate;
        invoice.PaymentMethod = request.PaymentMethod;
        invoice.DueDate = request.DueDate;
        invoice.PaidOn = kind == InvoiceKind.Vat ? request.PaidOn : null;
        var anyExempt = request.Items.Any(i => i.VatRate == VatRate.Exempt) || before?.Any(i => i.VatRate == VatRate.Exempt) == true;
        invoice.ExemptionBasis = anyExempt ? Mapping.EmptyToNull(request.ExemptionBasis) ?? originalExemptionBasis : null;
        invoice.BankAccountNumber = Mapping.NormalizeBankAccount(request.BankAccountNumber)
                                    ?? (request.PaymentMethod == PaymentMethod.Transfer ? company.BankAccountNumber : null);

        // Kopia aktualnych danych sprzedawcy - utrwalana w chwili wysyłki faktury do KSeF
        invoice.Seller = new InvoiceParty { Name = company.Name, Nip = company.Nip, Address = company.Address.Copy() };
        invoice.Buyer = buyer;

        // Pozycje: na korekcie najpierw wiersze stanu przed korektą, potem stan po korekcie (osobna numeracja)
        var afterInputs = request.Items.Select(i => new LineInput(i.Quantity, i.UnitNetPrice, i.VatRate)).ToList();
        var totals = InvoiceCalculator.Calculate(afterInputs);

        invoice.Items.Clear();
        var position = 1;
        foreach (var b in before ?? [])
        {
            invoice.Items.Add(new InvoiceItem
            {
                Position = position++, BeforeCorrection = true, Name = b.Name, Unit = b.Unit, Quantity = b.Quantity,
                UnitNetPrice = b.UnitNetPrice, VatRate = b.VatRate, NetAmount = b.NetAmount, VatAmount = b.VatAmount, GrossAmount = b.GrossAmount
            });
        }
        for (var n = 0; n < request.Items.Count; n++)
        {
            var item = request.Items[n];
            var line = totals.Lines[n];
            invoice.Items.Add(new InvoiceItem
            {
                Position = position++,
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

        // Waluta obca: kurs średni NBP z ostatniego dnia roboczego przed dniem sprzedaży (art. 31a ustawy o VAT) - D43
        if (before is null)
        {
            if (Currencies.IsForeign(invoice.Currency))
            {
                try
                {
                    var rate = await rates.ForTaxPointAsync(invoice.Currency, invoice.SaleDate ?? invoice.IssueDate, ct);
                    (invoice.ExchangeRate, invoice.ExchangeRateDate, invoice.ExchangeRateTable) = (rate.Mid, rate.EffectiveDate, rate.TableNo);
                }
                catch (NbpException ex)
                {
                    return ApiErrors.Validation(nameof(request.Currency), $"Nie udało się pobrać kursu NBP: {ex.Message}");
                }
            }
            else
            {
                (invoice.ExchangeRate, invoice.ExchangeRateDate, invoice.ExchangeRateTable) = (null, null, null);
            }
        }

        if (before is not null)
        {
            var correction = InvoiceCalculator.CalculateCorrection(
                before.Select(b => new LineInput(b.Quantity, b.UnitNetPrice, b.VatRate)), afterInputs);
            invoice.TotalNet = correction.TotalNet;
            invoice.TotalVat = correction.TotalVat;
            invoice.TotalGross = correction.TotalGross;
        }
        else
        {
            invoice.TotalNet = totals.TotalNet;
            invoice.TotalVat = totals.TotalVat;
            invoice.TotalGross = totals.TotalGross;
        }

        return null;
    }

    /// <summary>Aktualny stan pozycji faktury: po ostatniej korekcie przekazanej do KSeF albo stan pierwotny.</summary>
    internal static async Task<(List<InvoiceItem> Items, bool PendingCorrection)> CurrentStateAsync(
        AppDbContext db, Invoice original, int exceptCorrectionId, CancellationToken ct)
    {
        var corrections = await db.Invoices.AsNoTracking().Include(c => c.Items)
            .Where(c => c.CorrectedInvoiceId == original.Id && c.Id != exceptCorrectionId
                        && (c.Status == InvoiceStatus.Pending || c.Status == InvoiceStatus.Accepted))
            .OrderByDescending(c => c.Id)
            .ToListAsync(ct);

        var items = corrections.FirstOrDefault(c => c.Status == InvoiceStatus.Accepted) is { } last
            ? last.Items.Where(x => !x.BeforeCorrection)
            : original.Items.Where(x => !x.BeforeCorrection);
        return (items.OrderBy(x => x.Position).ToList(), corrections.Any(c => c.Status == InvoiceStatus.Pending));
    }

    /// <summary>
    /// Przed wysyłką korekty: stan „przed korektą” musi odpowiadać aktualnemu stanowi faktury - w międzyczasie
    /// mogła zostać przyjęta inna korekta tej samej faktury.
    /// </summary>
    private static async Task<IResult?> CheckCorrectionStateAsync(AppDbContext db, Invoice correction, CancellationToken ct)
    {
        var original = await db.Invoices.AsNoTracking().Include(i => i.Items)
            .SingleAsync(i => i.Id == correction.CorrectedInvoiceId, ct);
        var state = await CurrentStateAsync(db, original, correction.Id, ct);
        if (state.PendingCorrection)
            return ApiErrors.Conflict("Inna korekta tej faktury jest przetwarzana przez KSeF. Wyślij tę korektę po jej przyjęciu.");

        var before = correction.Items.Where(x => x.BeforeCorrection).OrderBy(x => x.Position).ToList();
        var same = before.Count == state.Items.Count && before.Zip(state.Items).All(p =>
            p.First.Name == p.Second.Name && p.First.Unit == p.Second.Unit && p.First.Quantity == p.Second.Quantity
            && p.First.UnitNetPrice == p.Second.UnitNetPrice && p.First.VatRate == p.Second.VatRate);
        return same ? null : ApiErrors.Conflict(
            "Po zapisaniu tej korekty KSeF przyjął inną korektę tej faktury. Otwórz edycję korekty i zapisz ją ponownie, aby uwzględnić aktualny stan.");
    }

    private static bool SameItems(IReadOnlyList<InvoiceItem> before, IReadOnlyList<SaveInvoiceItemRequest> after) =>
        before.Count == after.Count && before.Zip(after).All(p =>
            p.First.Name == p.Second.Name.Trim() && p.First.Unit == Mapping.EmptyToNull(p.Second.Unit)
            && p.First.Quantity == p.Second.Quantity && p.First.UnitNetPrice == p.Second.UnitNetPrice && p.First.VatRate == p.Second.VatRate);

    internal static bool IsDuplicateNumber(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Invoices_CompanyId_Number" };
}
