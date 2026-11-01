namespace FakturowanieKSeF.Api.Services;

/// <summary>Sygnał „jest nowa praca” - pozwala obudzić usługę w tle od razu po kliknięciu „Wyślij”.</summary>
public sealed class KsefWorkSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Pulse()
    {
        if (_signal.CurrentCount == 0)
        {
            try { _signal.Release(); } catch (SemaphoreFullException) { /* już zasygnalizowano */ }
        }
    }

    public Task WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}

/// <summary>
/// Usługa w tle realizująca wysyłki do KSeF (<see cref="KsefSubmissionProcessor"/>). Budzi się po sygnale
/// z endpointu wysyłki albo co kilka sekund, żeby odpytać KSeF o statusy i UPO.
/// </summary>
public sealed class KsefSubmissionWorker(
    IServiceScopeFactory scopes,
    KsefWorkSignal signal,
    IConfiguration configuration,
    ILogger<KsefSubmissionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Ksef:Worker:Enabled", true))
        {
            logger.LogInformation("Usługa wysyłki do KSeF jest wyłączona w konfiguracji.");
            return;
        }

        var interval = TimeSpan.FromSeconds(configuration.GetValue("Ksef:Worker:IntervalSeconds", 3));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var processed = await scope.ServiceProvider.GetRequiredService<KsefSubmissionProcessor>().ProcessAsync(stoppingToken);

                // Gdy nie ma pracy - czekamy dłużej (lub do sygnału), żeby nie obciążać bazy
                await signal.WaitAsync(processed > 0 ? interval : interval * 5, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Błąd usługi wysyłki do KSeF");
                await Task.Delay(interval, stoppingToken);
            }
        }
    }
}
