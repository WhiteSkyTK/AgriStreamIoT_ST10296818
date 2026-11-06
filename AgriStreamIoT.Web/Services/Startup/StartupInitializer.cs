using AgriStreamIoT.Shared;
using AgriStreamIoT.Shared.Storage;
using AgriStreamIoT.Web.Configuration;
using AgriStreamIoT.Web.Interfaces;
using AgriStreamIoT.Web.Services.Seeding;
using Microsoft.Extensions.Options;

namespace AgriStreamIoT.Web.Services.Startup;

/// <summary>
/// Runs in the background after the app starts (so App Service health checks pass immediately):
/// creates the Azure SQL schema and demo data, creates the storage tables/containers/queue and
/// writes or back-fills sample telemetry. Retries with back-off, which covers a serverless SQL
/// database resuming from pause and connection strings pasted in while the app is running.
/// </summary>
public sealed class StartupInitializer : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly AppReadiness _readiness;
    private readonly IOptionsMonitor<AgriStreamOptions> _options;
    private readonly ILogger<StartupInitializer> _logger;

    public StartupInitializer(IServiceProvider services, AppReadiness readiness,
        IOptionsMonitor<AgriStreamOptions> options, ILogger<StartupInitializer> logger)
    {
        _services = services;
        _readiness = readiness;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        var attempt = 0;

        while (!stoppingToken.IsCancellationRequested && !_readiness.AllReady)
        {
            attempt++;
            var seed = _options.CurrentValue.SeedDemoData;

            if (!_readiness.Sql.Ready)
            {
                await RunStepAsync(_readiness.Sql, async ct =>
                {
                    var seeder = _services.GetRequiredService<DatabaseSeeder>();
                    await seeder.EnsureSchemaAsync(ct);
                    if (seed) await seeder.SeedAsync(ct);
                }, stoppingToken);
            }

            if (!_readiness.Storage.Ready)
            {
                await RunStepAsync(_readiness.Storage, async ct =>
                {
                    await _services.GetRequiredService<FarmTableStore>().EnsureTablesAsync(ct);
                    await _services.GetRequiredService<IFarmMediaService>().EnsureContainersAsync(ct);
                    await _services.GetRequiredService<IQueueMonitor>().GetStatusAsync(ct);   // creates the queue
                    if (seed) await _services.GetRequiredService<TelemetrySeeder>().SeedOrBackfillAsync(ct);
                }, stoppingToken);
            }

            if (_readiness.AllReady) break;

            // 5 s, 10 s, 20 s ... capped at 60 s.
            var delay = TimeSpan.FromSeconds(Math.Min(60, 5 * Math.Pow(2, Math.Min(attempt - 1, 4))));
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }

        if (_readiness.AllReady)
            _logger.LogInformation("AgriStream start-up complete: Azure SQL and Azure Storage are ready.");
    }

    private async Task RunStepAsync(ComponentState state, Func<CancellationToken, Task> step, CancellationToken stoppingToken)
    {
        state.MarkAttempt();
        try
        {
            await step(stoppingToken);
            state.MarkReady();
            _logger.LogInformation("{Component} ready.", state.Name);
        }
        catch (FarmConfigurationException ex)
        {
            state.MarkFailed(ex.Message, notConfigured: true);
            if (state.Attempts == 1) _logger.LogWarning("{Component}: {Message}", state.Name, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            state.MarkFailed(ex.Message, notConfigured: false);
            _logger.LogWarning(ex, "{Component} not ready yet (attempt {Attempt}); retrying.", state.Name, state.Attempts);
        }
    }
}
