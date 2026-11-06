using AgriStreamIoT.Web.Services.Startup;

namespace AgriStreamIoT.Web.Services.Simulation;

/// <summary>
/// Background service that keeps the farm "alive" for demos: a reading from every probe each
/// interval (default 2 minutes). Turn it off with AgriStream:Simulator:Enabled = false.
/// </summary>
public sealed class SensorSimulatorService : BackgroundService
{
    private readonly SensorStreamRunner _runner;
    private readonly SimulatorControl _control;
    private readonly AppReadiness _readiness;
    private readonly ILogger<SensorSimulatorService> _logger;

    public SensorSimulatorService(SensorStreamRunner runner, SimulatorControl control, AppReadiness readiness,
        ILogger<SensorSimulatorService> logger)
    {
        _runner = runner;
        _control = control;
        _readiness = readiness;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give start-up seeding a head start.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_control.Enabled && _readiness.Storage.Ready)
            {
                try
                {
                    var result = await _runner.ScheduledTickAsync(stoppingToken);
                    if (result is not null)
                        _logger.LogInformation("Sensor stream: {Readings} readings in {Batches} batch(es), {Actions} automation action(s), {Ms} ms.",
                            result.Readings, result.Batches, result.Actions.Count, result.ElapsedMs);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Never let a transient storage/SQL error stop the host.
                    _logger.LogWarning(ex, "Sensor stream tick failed; will retry next interval.");
                }
            }

            try { await Task.Delay(TimeSpan.FromSeconds(_control.IntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
