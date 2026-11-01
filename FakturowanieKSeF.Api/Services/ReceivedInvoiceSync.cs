using FakturowanieKSeF.Api.Data;
using FakturowanieKSeF.Contracts.Dtos;
using FakturowanieKSeF.Ksef;
using FakturowanieKSeF.Models;
using Microsoft.EntityFrameworkCore;

namespace FakturowanieKSeF.Api.Services;

/// <summary>
/// Przyrostowa synchronizacja faktur otrzymanych (zakupowych) z KSeF według zaleceń MF (scenariusz „do HWM”):
/// <list type="number">
/// <item>zapytanie o metadane faktur, w których firma jest nabywcą, od punktu kontrolnego
///   (<see cref="Company.ReceivedInvoicesSyncedUntil"/>) do HWM - momentu, do którego KSeF gwarantuje kompletność,</item>
/// <item>pobranie XML każdej nowej faktury i zapis w lokalnej bazie (deduplikacja po numerze KSeF),</item>
/// <item>przesunięcie punktu kontrolnego - do HWM po pełnym przejściu albo do ostatniej przetworzonej faktury,
///   gdy synchronizacja została przerwana (limit zapytań, błąd).</item>
/// </list>
/// Budżet zapytań na jedną synchronizację mieści się w limitach API KSeF (20 zapytań o metadane i 64 pobrania
/// faktur na godzinę); pozostałe faktury zostaną pobrane w kolejnym przebiegu.
/// </summary>
public sealed class ReceivedInvoiceSync(
    AppDbContext db,
    IKsefGateway gateway,
    KsefTokenProtector tokenProtector,
    TimeProvider time,
    IConfiguration configuration,
    ILogger<ReceivedInvoiceSync> logger)
{
    public const int PageSize = 100;
    public const int MaxPagesPerSync = 3;
    public const int MaxDownloadsPerSync = 40;

    /// <summary>Przy pierwszej synchronizacji pobierane są faktury z tylu ostatnich dni.</summary>
    private int InitialDays => configuration.GetValue("Ksef:Receive:InitialDays", 30);

    public async Task<SyncResultDto> SyncAsync(Company company, CancellationToken ct = default)
    {
        if (company.KsefTokenEncrypted is null)
            throw new KsefAuthenticationException("Firma nie ma skonfigurowanego tokenu KSeF.");

        var token = tokenProtector.Unprotect(company.KsefTokenEncrypted);
        var now = time.GetUtcNow();
        var from = company.ReceivedInvoicesSyncedUntil is { } synced
            ? new DateTimeOffset(DateTime.SpecifyKind(synced, DateTimeKind.Utc))
            : now.AddDays(-InitialDays);

        company.ReceivedInvoicesLastSyncAt = now.UtcDateTime;
        DateTimeOffset? checkpoint = null; // data trwałego zapisu, do której wszystko jest już w bazie
        var downloaded = 0;
        var complete = false;

        try
        {
            for (var pageNo = 0; pageNo < MaxPagesPerSync; pageNo++)
            {
                var page = await gateway.QueryReceivedInvoicesAsync(company.Nip, token, from, pageNo, PageSize, ct);
                var budgetExhausted = false;

                foreach (var meta in page.Invoices)
                {
                    var known = await db.ReceivedInvoices.AnyAsync(r => r.CompanyId == company.Id && r.KsefNumber == meta.KsefNumber, ct);
                    if (!known)
                    {
                        if (downloaded >= MaxDownloadsPerSync)
                        {
                            budgetExhausted = true;
                            break;
                        }

                        var xml = await gateway.DownloadInvoiceAsync(company.Nip, token, meta.KsefNumber, ct);
                        db.ReceivedInvoices.Add(new ReceivedInvoice
                        {
                            CompanyId = company.Id,
                            KsefNumber = meta.KsefNumber,
                            InvoiceNumber = Truncate(meta.InvoiceNumber, 256),
                            SellerNip = Truncate(meta.SellerNip, 10),
                            SellerName = Truncate(meta.SellerName, 250),
                            IssueDate = meta.IssueDate,
                            Currency = Truncate(meta.Currency, 3),
                            TotalNet = meta.NetAmount,
                            TotalVat = meta.VatAmount,
                            TotalGross = meta.GrossAmount,
                            KsefAcceptedAt = meta.AcquisitionDate.UtcDateTime, // data nadania numeru KSeF = data otrzymania
                            Xml = xml,
                            InvoiceHash = meta.InvoiceHash,
                            DownloadedAt = time.GetUtcNow().UtcDateTime
                        });
                        await db.SaveChangesAsync(ct); // każda faktura zapisana trwale - przerwanie niczego nie gubi
                        downloaded++;
                    }

                    checkpoint = meta.PermanentStorageDate;
                }

                if (budgetExhausted) break;

                if (!page.HasMore)
                {
                    // Pełne przejście do HWM: wszystko ≤ HWM jest pobrane i nic nowego już się tam nie pojawi
                    if (!page.IsTruncated && page.PermanentStorageHwmDate is { } hwm && (checkpoint is null || hwm > checkpoint))
                        checkpoint = hwm;
                    complete = true;
                    break;
                }
            }

            company.ReceivedInvoicesLastSyncError = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            company.ReceivedInvoicesLastSyncError = Truncate(ex.Message, 1000);
            logger.LogWarning(ex, "Synchronizacja faktur otrzymanych firmy {CompanyId} nie powiodła się", company.Id);
        }

        if (checkpoint is { } c && (company.ReceivedInvoicesSyncedUntil is null || c.UtcDateTime > company.ReceivedInvoicesSyncedUntil))
            company.ReceivedInvoicesSyncedUntil = c.UtcDateTime;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Synchronizacja faktur otrzymanych firmy {CompanyId}: pobrano {Count}, kompletna: {Complete}", company.Id, downloaded, complete);
        return new SyncResultDto(downloaded, complete);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>
/// Cykliczna synchronizacja faktur otrzymanych wszystkich firm z tokenem KSeF. Interwał zgodnie z zaleceniem MF
/// (nie częściej niż co 15 minut); użytkownik może dodatkowo uruchomić synchronizację ręcznie (tryb mieszany).
/// </summary>
public sealed class ReceivedInvoicesWorker(IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider time,
    ILogger<ReceivedInvoicesWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Ksef:Receive:Enabled", true))
        {
            logger.LogInformation("Cykliczne pobieranie faktur otrzymanych jest wyłączone w konfiguracji.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(15, configuration.GetValue("Ksef:Receive:IntervalMinutes", 15)));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var sync = scope.ServiceProvider.GetRequiredService<ReceivedInvoiceSync>();
                var due = time.GetUtcNow().UtcDateTime - interval;

                var companies = await db.Companies
                    .Where(c => c.KsefTokenEncrypted != null && (c.ReceivedInvoicesLastSyncAt == null || c.ReceivedInvoicesLastSyncAt < due))
                    .ToListAsync(stoppingToken);

                foreach (var company in companies)
                    await sync.SyncAsync(company, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Błąd cyklicznej synchronizacji faktur otrzymanych");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ContinueWith(_ => { });
        }
    }
}
