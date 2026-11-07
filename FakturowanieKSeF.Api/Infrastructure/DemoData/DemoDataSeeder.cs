using System.Security.Cryptography;
using System.Text;
using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Api.Endpoints;
using FakturowanieKSeF.Api.Integrations.Nbp;
using FakturowanieKSeF.Api.Services;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Ksef;
using FakturowanieKSeF.Ksef.Fa3;
using FakturowanieKSeF.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FakturowanieKSeF.Api.Infrastructure.DemoData;

/// <summary>
/// Dane demonstracyjne dla środowiska deweloperskiego (D49): konta testowe z kompletnymi danymi firm, kontrahentami,
/// katalogiem i fakturami wystawionymi między sobą. Dwa rodzaje faktur:
/// <list type="bullet">
/// <item>historia - faktury „przyjęte w KSeF” z przykładowym numerem KSeF (NIE są wysyłane do KSeF), z płatnościami,
///   korektą, proformą i lustrzanymi fakturami otrzymanymi u nabywców; wypełniają listy, rozliczenia i wykresy,</item>
/// <item>szkice gotowe do wysłania - użytkownik wysyła je sam do testowego KSeF (prawdziwy numer, UPO, QR).</item>
/// </list>
/// Faktury powstają tą samą ścieżką co z formularza (<c>ApplyAsync</c>, numeracja, XML FA(3) z walidacją XSD).
/// NIP-y firm są losowe (z poprawną sumą kontrolną) - po wyczyszczeniu bazy ponowne wysłanie do testowego KSeF
/// faktur o tych samych numerach nie koliduje z poprzednimi. Uruchamiane wyłącznie w środowisku Development.
/// </summary>
public sealed class DemoDataSeeder(
    AppDbContext db,
    UserManager<ApplicationUser> users,
    InvoiceNumbering numbering,
    ExchangeRateLookup rates,
    Fa3XmlGenerator generator,
    Fa3SchemaValidator validator,
    IKsefGateway gateway,
    KsefTokenProtector protector,
    TimeProvider time,
    IConfiguration configuration,
    ILogger<DemoDataSeeder> logger)
{
    public const string DemoNote = "Dane demonstracyjne: faktura nie została wysłana do KSeF, numer KSeF jest przykładowy.";

    /// <summary>Konta testowe (dane fikcyjne; domena example.com jest zarezerwowana i nie przyjmuje poczty).</summary>
    public static readonly IReadOnlyList<DemoAccount> TestAccounts =
    [
        new("TestUser1@example.com", "TestUser1@", "Studio Graficzne Piksel Anna Nowak",
            new Address { Street = "ul. Marszałkowska 10/5", PostalCode = "00-590", City = "Warszawa" }, "600100201",
            [new("Projekt logo", "szt.", 2500m, VatRate.Rate23), new("Projekt strony internetowej", "usł.", 6800m, VatRate.Rate23),
             new("Sesja zdjęciowa produktów", "godz.", 250m, VatRate.Rate23), new("Druk ulotek A5", "szt.", 0.45m, VatRate.Rate23),
             new("Projekt wizytówek", "usł.", 400m, VatRate.Rate23)]),
        new("TestUser2@example.com", "TestUser2@", "Hurtownia Biuro-Max sp. z o.o.",
            new Address { Street = "ul. Wielicka 25", PostalCode = "30-552", City = "Kraków" }, "600100202",
            [new("Papier ksero A4 (ryza)", "ryza", 18.90m, VatRate.Rate23), new("Toner do drukarki laserowej", "szt.", 289m, VatRate.Rate23),
             new("Krzesło biurowe ergonomiczne", "szt.", 649m, VatRate.Rate23), new("Segregator A4", "szt.", 7.50m, VatRate.Rate23),
             new("Poradnik „Excel w biurze”", "szt.", 59m, VatRate.Rate5)]),
        new("TestUser3@example.com", "TestUser3@", "Kod i Kawa sp. z o.o.",
            new Address { Street = "ul. Legnicka 48", PostalCode = "54-202", City = "Wrocław" }, "600100203",
            [new("Programowanie - prace rozwojowe", "godz.", 180m, VatRate.Rate23), new("Utrzymanie aplikacji", "mies.", 2500m, VatRate.Rate23),
             new("Wdrożenie systemu", "usł.", 15000m, VatRate.Rate23), new("Szkolenie z obsługi systemu", "godz.", 300m, VatRate.Rate23)]),
        new("TestUser4@example.com", "TestUser4@", "Biuro Rachunkowe Saldo Ewa Wiśniewska",
            new Address { Street = "ul. Głogowska 12", PostalCode = "60-734", City = "Poznań" }, "600100204",
            [new("Prowadzenie KPiR", "mies.", 450m, VatRate.Rate23), new("Pełna księgowość", "mies.", 1500m, VatRate.Rate23),
             new("Obsługa kadrowo-płacowa", "os.", 80m, VatRate.Rate23), new("Doradztwo podatkowe", "godz.", 200m, VatRate.Rate23)])
    ];

    private static readonly IReadOnlyList<DemoProduct> OwnerProducts =
    [
        new("Usługi programistyczne", "godz.", 150m, VatRate.Rate23), new("Konsultacje IT", "godz.", 200m, VatRate.Rate23),
        new("Audyt aplikacji internetowej", "usł.", 3500m, VatRate.Rate23)
    ];

    private readonly Random _random = new();
    private DateOnly _today;

    /// <summary>Wgrywa dane, jeśli jeszcze ich nie ma. Zwraca false, gdy dane już istniały.</summary>
    public async Task<bool> SeedAsync(CancellationToken ct = default)
    {
        if (await users.FindByEmailAsync(TestAccounts[0].Email) is not null)
        {
            logger.LogInformation("Dane demonstracyjne już istnieją - pomijam (pełny reset: docker compose down -v).");
            return false;
        }

        _today = PolishDate.Today(time);
        var accounts = TestAccounts.ToList();
        if (OwnerAccount() is { } owner)
        {
            if (await users.FindByEmailAsync(owner.Email) is null) accounts.Add(owner);
            else logger.LogWarning("Konto {Email} już istnieje - nie tworzę go ponownie w danych demonstracyjnych.", owner.Email);
        }

        // 1. Konta i firmy
        var companies = new List<SeedCompany>();
        foreach (var account in accounts)
            companies.Add(await CreateAccountAsync(account, account.IsOwner, ct));

        // 2. Kontrahenci (pozostałe firmy + osoba prywatna) i katalog
        foreach (var c in companies)
        {
            foreach (var other in companies.Where(o => o != c))
            {
                var customer = new Customer
                {
                    CompanyId = c.Company.Id, Name = other.Company.Name, Nip = other.Company.Nip, IsCompany = true,
                    Address = other.Company.Address.Copy(), Email = other.Company.Email
                };
                db.Customers.Add(customer);
                c.Customers[other.Company.Id] = customer;
            }
            c.PrivatePerson = new Customer
            {
                CompanyId = c.Company.Id, Name = "Jan Kowalski", IsCompany = false,
                Address = new Address { Street = "ul. Długa 7/3", PostalCode = "80-831", City = "Gdańsk" }, Email = "jan.kowalski@example.com"
            };
            db.Customers.Add(c.PrivatePerson);
            foreach (var p in c.Account.Products)
                db.Products.Add(new Product { CompanyId = c.Company.Id, Name = p.Name, Unit = p.Unit, UnitNetPrice = p.Price, VatRate = p.VatRate });
        }
        await db.SaveChangesAsync(ct);

        // 3. Historia: faktury „przyjęte w KSeF” z ostatnich miesięcy
        for (var i = 0; i < companies.Count; i++)
            await SeedHistoryAsync(companies, i, ct);

        // 4. Funkcje szczególne
        await SeedSpecialsAsync(companies, ct);

        // 5. Szkice gotowe do wysłania do testowego KSeF
        foreach (var c in companies)
            await SeedDraftsAsync(companies, c, ct);

        // 6. Role: biuro rachunkowe (TestUser4) jako księgowa w firmie TestUser1 (D46)
        db.CompanyMembers.Add(new CompanyMember
        {
            CompanyId = companies[0].Company.Id, UserId = companies[3].User.Id, Role = CompanyRole.Accountant,
            CreatedAt = time.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Dane demonstracyjne wgrane: {Count} kont ({Emails}).", companies.Count,
            string.Join(", ", companies.Select(c => c.User.Email)));

        // 7. Tokeny testowego KSeF - na końcu, bo wymagają sieci; dane powstają także bez nich
        if (configuration.GetValue("DemoData:GenerateKsefTokens", true))
            await GenerateTokensAsync(companies, ct);

        return true;
    }

    private DemoAccount? OwnerAccount()
    {
        var email = configuration["DemoData:Owner:Email"];
        var password = configuration["DemoData:Owner:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return null;

        return new DemoAccount(email.Trim(), password, configuration["DemoData:Owner:CompanyName"] ?? "Moja Firma (konto demonstracyjne)",
            new Address { Street = "ul. Piotrkowska 100", PostalCode = "90-004", City = "Łódź" }, "600100200", OwnerProducts, IsOwner: true);
    }

    private async Task<SeedCompany> CreateAccountAsync(DemoAccount account, bool isOwner, CancellationToken ct)
    {
        var user = new ApplicationUser { UserName = account.Email, Email = account.Email, EmailConfirmed = true };
        var result = await users.CreateAsync(user, account.Password);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Nie udało się utworzyć konta {account.Email}: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        var company = new Company
        {
            OwnerId = user.Id, Name = account.CompanyName, Nip = RandomNip(), Address = account.Address.Copy(),
            Email = account.Email, Phone = account.Phone, BankAccountNumber = RandomBankAccount(),
            // Konto autora: powiadomienia o nowych fakturach kosztowych i automatyczne przypomnienia włączone
            NotifyReceivedInvoices = isOwner, AutoRemindersEnabled = isOwner, NotificationEmail = isOwner ? account.Email : null
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync(ct);
        return new SeedCompany(account, user, company);
    }

    // ---------------- historia ----------------

    private async Task SeedHistoryAsync(List<SeedCompany> companies, int index, CancellationToken ct)
    {
        var seller = companies[index];
        var partners = companies.Where(c => c != seller).ToList();

        for (var monthsAgo = 6; monthsAgo >= 1; monthsAgo--)
        {
            var month = _today.AddMonths(-monthsAgo);
            for (var j = 0; j < 2; j++)
            {
                var day = Math.Min(3 + 11 * j + index, 28);
                var issue = new DateOnly(month.Year, month.Month, day);
                var toPrivate = monthsAgo == 3 && j == 1;
                var buyer = toPrivate ? null : partners[(monthsAgo + j + index) % partners.Count];
                var cash = monthsAgo == 4 && j == 1; // zapłacono gotówką przy wystawieniu (D33)

                var request = Request(seller, buyer is null ? seller.PrivatePerson!.Id : seller.Customers[buyer.Company.Id].Id,
                    issue, RandomItems(seller.Account.Products), cash);
                var invoice = await IssueAcceptedAsync(seller, request, ct);
                if (invoice is null) continue;

                // Rozliczenie: starsze opłacone, z ostatniego miesiąca - częściowo albo wcale (należności, po terminie)
                if (!cash && monthsAgo >= 2) await PayAsync(invoice, buyer, invoice.DueDate!.Value.AddDays(-2), invoice.TotalGross, ct);
                else if (!cash && monthsAgo == 1 && j == 0)
                    await PayAsync(invoice, buyer, issue.AddDays(5), Math.Round(invoice.TotalGross / 2, 2), ct);
            }
        }
    }

    private async Task SeedSpecialsAsync(List<SeedCompany> companies, CancellationToken ct)
    {
        var studio = companies[0];
        var wholesale = companies[1];
        var software = companies[2];
        var accounting = companies[3];

        // Korekta (D32): rabat do przedostatniej faktury studia
        var original = await db.Invoices.Include(i => i.Items)
            .Where(i => i.CompanyId == studio.Company.Id && i.Kind == InvoiceKind.Vat && i.Status == InvoiceStatus.Accepted && i.CustomerId != null)
            .OrderByDescending(i => i.IssueDate).Skip(2).FirstAsync(ct);
        var after = original.Items.OrderBy(i => i.Position).Select(i => new SaveInvoiceItemRequest
        {
            Name = i.Name, Unit = i.Unit, Quantity = i.Quantity, UnitNetPrice = Math.Round(i.UnitNetPrice * 0.9m, 2), VatRate = i.VatRate
        }).ToList();
        await IssueAcceptedAsync(studio, new SaveInvoiceRequest
        {
            CorrectedInvoiceId = original.Id, IssueDate = Min(original.IssueDate.AddDays(10), _today), Items = after,
            CorrectionReason = "Rabat 10% przyznany po sprzedaży", CorrectionType = CorrectionType.CorrectionDate,
            PaymentMethod = PaymentMethod.Transfer, DueDate = Min(original.IssueDate.AddDays(10), _today).AddDays(14)
        }, ct);

        // Faktura cykliczna (D39): miesięczny abonament dla biura rachunkowego
        var subscription = new List<SaveInvoiceItemRequest> { Item("Abonament - obsługa graficzna", "mies.", 1, 1200m) };
        Invoice? template = null;
        for (var monthsAgo = 3; monthsAgo >= 1; monthsAgo--)
        {
            var m = _today.AddMonths(-monthsAgo);
            var issue = new DateOnly(m.Year, m.Month, 1);
            template = await IssueAcceptedAsync(studio, Request(studio, studio.Customers[accounting.Company.Id].Id, issue, subscription), ct);
            if (template is not null && monthsAgo > 1) await PayAsync(template, accounting, issue.AddDays(7), template.TotalGross, ct);
        }
        if (template is not null)
        {
            var next = new DateOnly(_today.Year, _today.Month, 1).AddMonths(1);
            db.RecurringInvoices.Add(new RecurringInvoice
            {
                CompanyId = studio.Company.Id, Name = "Abonament - Biuro Rachunkowe Saldo", TemplateInvoiceId = template.Id,
                IntervalMonths = 1, DayOfMonth = 1, NextIssueDate = next, NotifyByEmail = false
            });
        }

        // Faktura odrzucona przez KSeF - do pokazania błędu i ponownej wysyłki po poprawce
        var rejected = await CreateDraftAsync(studio, Request(studio, studio.Customers[wholesale.Company.Id].Id, _today.AddDays(-2),
            [Item("Projekt katalogu produktów", "usł.", 1, 3200m)]), ct);
        rejected.Status = InvoiceStatus.Rejected;
        rejected.KsefSubmissions.Add(new KsefSubmission
        {
            SentAt = time.GetUtcNow().UtcDateTime.AddDays(-2), SentXml = Encoding.UTF8.GetString(generator.GenerateBytes(rejected)),
            InvoiceHash = "demo", StatusCode = 450, CompletedAt = time.GetUtcNow().UtcDateTime.AddDays(-2),
            StatusDescription = "Błąd weryfikacji semantyki dokumentu faktury (przykład - dane demonstracyjne).",
            StatusDetails = "Przykładowy błąd: popraw fakturę i wyślij ją ponownie - pójdzie do testowego KSeF."
        });
        await db.SaveChangesAsync(ct);

        // Proforma (D42): jedna zamieniona na szkic faktury VAT, druga czeka na zapłatę
        var proforma = await CreateDraftAsync(wholesale, Request(wholesale, wholesale.Customers[software.Company.Id].Id, _today.AddDays(-6),
            [Item("Krzesło biurowe ergonomiczne", "szt.", 6, 649m), Item("Segregator A4", "szt.", 20, 7.50m)], proforma: true), ct);
        var vat = new Invoice { CompanyId = wholesale.Company.Id, ProformaId = proforma.Id };
        await ApplyOrThrowAsync(vat, wholesale, InvoiceTemplates.RequestFrom(proforma, true, _today, proforma.DueDate), ct);
        vat.Number = await numbering.NextAsync(wholesale.Company.Id, _today, InvoiceKind.Vat, ct);
        db.Invoices.Add(vat);
        await db.SaveChangesAsync(ct);
        await CreateDraftAsync(wholesale, Request(wholesale, wholesale.Customers[studio.Company.Id].Id, _today.AddDays(-1),
            [Item("Toner do drukarki laserowej", "szt.", 4, 289m)], proforma: true), ct);

        // Waluta obca (D43): faktura w EUR z kursem NBP; bez dostępu do NBP pomijana
        var eurIssue = _today.AddMonths(-1);
        var eur = await IssueAcceptedAsync(software, Request(software, software.Customers[wholesale.Company.Id].Id, eurIssue,
            [Item("Programowanie - prace rozwojowe", "godz.", 40, 45m), Item("Utrzymanie aplikacji", "mies.", 1, 600m)], currency: "EUR"), ct);
        if (eur is not null) await PayAsync(eur, wholesale, eurIssue.AddDays(10), Math.Round(eur.TotalGross / 2, 2), ct);
    }

    // ---------------- szkice ----------------

    private async Task SeedDraftsAsync(List<SeedCompany> companies, SeedCompany seller, CancellationToken ct)
    {
        // Najpierw konto autora (jeśli jest) - wysłana faktura trafi do niego jako koszt z prawdziwego KSeF
        var partners = companies.Where(c => c != seller).OrderByDescending(c => c.Account.IsOwner).Take(2).ToList();
        foreach (var buyer in partners)
        {
            var draft = await CreateDraftAsync(seller, Request(seller, seller.Customers[buyer.Company.Id].Id, _today, RandomItems(seller.Account.Products)), ct);
            // Szkic musi dać się wysłać - sprawdzamy XML już teraz (nie jest zapisywany)
            Validate(generator.GenerateBytes(draft), draft.Number);
        }
    }

    // ---------------- faktury ----------------

    private SaveInvoiceRequest Request(SeedCompany seller, int customerId, DateOnly issue, List<SaveInvoiceItemRequest> items,
        bool paidInCash = false, bool proforma = false, string currency = "PLN") => new()
    {
        IssueDate = issue, SaleDate = issue, IssuePlace = seller.Company.Address.City, CustomerId = customerId, Items = items,
        Currency = currency, IsProforma = proforma,
        PaymentMethod = paidInCash ? PaymentMethod.Cash : PaymentMethod.Transfer,
        DueDate = paidInCash ? issue : issue.AddDays(14),
        PaidOn = paidInCash ? issue : null
    };

    private async Task<Invoice> CreateDraftAsync(SeedCompany seller, SaveInvoiceRequest request, CancellationToken ct)
    {
        var invoice = new Invoice { CompanyId = seller.Company.Id };
        await ApplyOrThrowAsync(invoice, seller, request, ct);
        invoice.Number = await numbering.NextAsync(seller.Company.Id, invoice.IssueDate, invoice.Kind, ct);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        return invoice;
    }

    /// <summary>
    /// Faktura „przyjęta w KSeF” bez wysyłki: XML jak przy wysyłce (z walidacją XSD), przykładowy numer KSeF,
    /// a u nabywcy z danych demonstracyjnych - ta sama faktura jako otrzymana. Null, gdy nie da się jej wystawić
    /// (np. brak kursu NBP).
    /// </summary>
    private async Task<Invoice?> IssueAcceptedAsync(SeedCompany seller, SaveInvoiceRequest request, CancellationToken ct)
    {
        var invoice = new Invoice { CompanyId = seller.Company.Id };
        try
        {
            await ApplyOrThrowAsync(invoice, seller, request, ct);
        }
        catch (InvalidOperationException ex) when (request.Currency != "PLN")
        {
            logger.LogWarning("Pomijam fakturę w {Currency} w danych demonstracyjnych: {Message}", request.Currency, ex.Message);
            return null;
        }
        invoice.Number = await numbering.NextAsync(seller.Company.Id, invoice.IssueDate, invoice.Kind, ct);

        var xml = generator.GenerateBytes(invoice);
        Validate(xml, invoice.Number);

        var acceptedAt = invoice.IssueDate.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc);
        var ksefNumber = $"{invoice.Seller.Nip}-{invoice.IssueDate:yyyyMMdd}-{RandomHex(12)}-{RandomHex(2)}";
        invoice.Status = InvoiceStatus.Accepted;
        invoice.KsefNumber = ksefNumber;
        invoice.KsefAcceptedAt = acceptedAt;
        invoice.KsefSubmissions.Add(new KsefSubmission
        {
            SentAt = acceptedAt, SentXml = Encoding.UTF8.GetString(xml), InvoiceHash = Convert.ToBase64String(SHA256.HashData(xml)),
            StatusCode = 200, StatusDescription = DemoNote, KsefNumber = ksefNumber, KsefAssignedAt = acceptedAt, CompletedAt = acceptedAt
        });
        db.Invoices.Add(invoice);
        InvoiceEndpoints.RecordPaymentFromInvoice(db, invoice, time);
        await db.SaveChangesAsync(ct);

        // Nabywca z danych demonstracyjnych otrzymuje tę fakturę „z KSeF”
        var buyer = await db.Companies.SingleOrDefaultAsync(c => c.Nip == invoice.Buyer.Nip && c.Id != seller.Company.Id, ct);
        if (buyer is not null)
        {
            db.ReceivedInvoices.Add(new ReceivedInvoice
            {
                CompanyId = buyer.Id, KsefNumber = ksefNumber, InvoiceNumber = invoice.Number, SellerNip = invoice.Seller.Nip!,
                SellerName = invoice.Seller.Name, IssueDate = invoice.IssueDate, DueDate = invoice.DueDate, Currency = invoice.Currency,
                TotalNet = invoice.TotalNet, TotalVat = invoice.TotalVat, TotalGross = invoice.TotalGross, KsefAcceptedAt = acceptedAt,
                InvoiceHash = invoice.KsefSubmissions[0].InvoiceHash, Xml = invoice.KsefSubmissions[0].SentXml,
                DownloadedAt = time.GetUtcNow().UtcDateTime
            });
            await db.SaveChangesAsync(ct);
        }
        return invoice;
    }

    private async Task ApplyOrThrowAsync(Invoice invoice, SeedCompany seller, SaveInvoiceRequest request, CancellationToken ct)
    {
        if (await InvoiceEndpoints.ApplyAsync(invoice, request, seller.Company, db, rates, ct) is { } error)
            throw new InvalidOperationException($"Nie udało się przygotować faktury demonstracyjnej: {Describe(error)}");
    }

    private static string Describe(IResult error) => (error as IValueHttpResult)?.Value switch
    {
        HttpValidationProblemDetails v => string.Join("; ", v.Errors.SelectMany(e => e.Value)),
        Microsoft.AspNetCore.Mvc.ProblemDetails p => $"{p.Title} {p.Detail}".Trim(),
        _ => error.GetType().Name
    };

    private void Validate(byte[] xml, string number)
    {
        var result = validator.Validate(xml);
        if (!result.IsValid)
            throw new InvalidOperationException($"XML faktury demonstracyjnej {number} niezgodny z FA(3): {string.Join("; ", result.Errors)}");
    }

    /// <summary>Wpłata po stronie sprzedawcy i - gdy nabywca jest w danych demonstracyjnych - po stronie nabywcy.</summary>
    private async Task PayAsync(Invoice invoice, SeedCompany? buyer, DateOnly paidOn, decimal amount, CancellationToken ct)
    {
        paidOn = Min(paidOn, _today);
        var now = time.GetUtcNow().UtcDateTime;
        db.Payments.Add(new Payment { CompanyId = invoice.CompanyId, InvoiceId = invoice.Id, PaidOn = paidOn, Amount = amount, Note = "Przelew", CreatedAt = now });
        if (buyer is not null &&
            await db.ReceivedInvoices.SingleOrDefaultAsync(r => r.CompanyId == buyer.Company.Id && r.KsefNumber == invoice.KsefNumber, ct) is { } received)
            db.Payments.Add(new Payment { CompanyId = buyer.Company.Id, ReceivedInvoiceId = received.Id, PaidOn = paidOn, Amount = amount, Note = "Przelew", CreatedAt = now });
        await db.SaveChangesAsync(ct);
    }

    private List<SaveInvoiceItemRequest> RandomItems(IReadOnlyList<DemoProduct> products) =>
        products.OrderBy(_ => _random.Next()).Take(_random.Next(1, 4))
            .Select(p => Item(p.Name, p.Unit, RandomQuantity(p.Price), p.Price, p.VatRate))
            .ToList();

    /// <summary>Realistyczna ilość: drogie usługi pojedynczo, tanie towary w większych ilościach.</summary>
    private decimal RandomQuantity(decimal price) => price switch
    {
        >= 1000m => 1,
        >= 100m => _random.Next(1, 6),
        >= 10m => _random.Next(2, 21),
        _ => _random.Next(5, 31) * 100
    };

    private static SaveInvoiceItemRequest Item(string name, string unit, decimal quantity, decimal price, VatRate vat = VatRate.Rate23) =>
        new() { Name = name, Unit = unit, Quantity = quantity, UnitNetPrice = price, VatRate = vat };

    // ---------------- KSeF ----------------

    private async Task GenerateTokensAsync(List<SeedCompany> companies, CancellationToken ct)
    {
        logger.LogInformation("Generowanie tokenów w TESTOWYM KSeF dla firm z danych demonstracyjnych...");
        foreach (var c in companies)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                var token = await gateway.GenerateTestTokenAsync(c.Company.Nip, timeout.Token);
                c.Company.KsefTokenEncrypted = protector.Protect(token);
                c.Company.KsefTokenUpdatedAt = time.GetUtcNow().UtcDateTime;
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Bez tokenu dane działają; token można wygenerować później przyciskiem w „Moja firma”
                logger.LogWarning("Nie udało się wygenerować tokenu testowego KSeF ({Message}) - pomijam pozostałe firmy.", ex.Message);
                return;
            }
        }
        logger.LogInformation("Tokeny testowego KSeF wygenerowane - szkice faktur można wysyłać.");
    }

    // ---------------- dane losowe z poprawnymi sumami kontrolnymi ----------------

    private string RandomNip()
    {
        int[] w = [6, 5, 7, 2, 3, 4, 5, 6, 7];
        while (true)
        {
            var d = Enumerable.Range(0, 9).Select(i => i < 3 ? _random.Next(1, 10) : _random.Next(0, 10)).ToArray();
            var c = d.Select((x, i) => x * w[i]).Sum() % 11;
            if (c < 10) return string.Concat(d) + c;
        }
    }

    /// <summary>Numer rachunku NRB (26 cyfr) z poprawną cyfrą kontrolną IBAN (mod 97).</summary>
    private string RandomBankAccount()
    {
        var bban = string.Concat(Enumerable.Range(0, 24).Select(_ => _random.Next(0, 10)));
        var check = 98 - (int)(System.Numerics.BigInteger.Parse(bban + "252100") % 97);
        return $"{check:00}{bban}";
    }

    private string RandomHex(int length) => Convert.ToHexString(RandomNumberGenerator.GetBytes((length + 1) / 2))[..length];

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private sealed record SeedCompany(DemoAccount Account, ApplicationUser User, Company Company)
    {
        public Dictionary<int, Customer> Customers { get; } = [];
        public Customer? PrivatePerson { get; set; }
    }
}

public sealed record DemoProduct(string Name, string Unit, decimal Price, VatRate VatRate);

public sealed record DemoAccount(string Email, string Password, string CompanyName, Address Address, string Phone,
    IReadOnlyList<DemoProduct> Products, bool IsOwner = false);
