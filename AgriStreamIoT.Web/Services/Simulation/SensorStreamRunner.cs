using System.Diagnostics;
using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Entities;
using AgriStreamIoT.Shared.Sql;
using AgriStreamIoT.Shared.Telemetry;
using AgriStreamIoT.Web.Interfaces;
using AgriStreamIoT.Web.Models;
using AgriStreamIoT.Web.Services.Seeding;
using AgriStreamIoT.Web.Services.Startup;

namespace AgriStreamIoT.Web.Services.Simulation;

/// <summary>
/// One "tick" of the live sensor stream: every reporting probe sends a reading, the weather station
/// logs every ten minutes, device heartbeats go to the register and the automation checks each field.
/// Also runs the on-demand demos from the Pipeline page (sensor burst, dry-soil alert).
/// </summary>
public sealed class SensorStreamRunner
{
    private static readonly TimeSpan WeatherEvery = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopes;
    private readonly FarmSimulation _simulation;
    private readonly SimulatorControl _control;
    private readonly AppReadiness _readiness;
    private readonly ILogger<SensorStreamRunner> _logger;
    private readonly HashSet<string> _primedFields = new(StringComparer.OrdinalIgnoreCase);

    public SensorStreamRunner(IServiceScopeFactory scopes, FarmSimulation simulation, SimulatorControl control,
        AppReadiness readiness, ILogger<SensorStreamRunner> logger)
    {
        _scopes = scopes;
        _simulation = simulation;
        _control = control;
        _readiness = readiness;
        _logger = logger;
    }

    public sealed record TickResult(int Readings, int WeatherLogs, IReadOnlyList<AutomationAction> Actions, double ElapsedMs, int Batches);

    /// <summary>Scheduled tick: only the instance holding the blob lease writes.</summary>
    public async Task<TickResult?> ScheduledTickAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var lease = scope.ServiceProvider.GetRequiredService<ISimulatorLease>();
        var leader = await lease.TryAcquireAsync(cancellationToken);
        _control.LastTickWasLeader = leader;
        if (!leader) return null;
        return await TickAsync(scope.ServiceProvider, readingsPerSensor: 1, forceFieldId: null, cancellationToken);
    }

    /// <summary>Pipeline page: write a burst of readings (high-frequency ingestion demo).</summary>
    public async Task<TickResult> BurstAsync(int readingsPerSensor, CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        return await TickAsync(scope.ServiceProvider, Math.Clamp(readingsPerSensor, 1, 60), null, cancellationToken);
    }

    /// <summary>Pipeline page: make one field's probes report dry soil so the automation reacts now.</summary>
    public async Task<TickResult> DrySoilAlertAsync(string fieldId, CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        return await TickAsync(scope.ServiceProvider, 1, fieldId, cancellationToken);
    }

    private async Task<TickResult> TickAsync(IServiceProvider sp, int readingsPerSensor, string? forceFieldId, CancellationToken ct)
    {
        await _control.TickLock.WaitAsync(ct);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var (fields, devices, sqlAvailable) = await LoadTopologyAsync(sp, ct);
            var queries = sp.GetRequiredService<ITelemetryQueryService>();
            var writer = sp.GetRequiredService<TelemetryWriter>();
            var now = DateTimeOffset.UtcNow;
            var readings = new List<SensorReadingEntity>();
            var fieldAverages = new Dictionary<string, (double Average, string Lowest)>(StringComparer.OrdinalIgnoreCase);

            foreach (var field in fields)
            {
                var sensors = devices.Where(d => d.FieldId == field.FieldId && d.DeviceType == DeviceTypes.SoilSensor
                                                 && d.OperationalStatus is not (DeviceStatuses.Offline or DeviceStatuses.Maintenance)).ToList();
                if (sensors.Count == 0) continue;

                if (_primedFields.Add(field.FieldId))
                {
                    foreach (var latest in await queries.GetLatestPerSensorAsync(field.FieldId, ct))
                        _simulation.Prime(latest, sensors.Any(s => s.DeviceId == latest.DeviceId && s.OperationalStatus == DeviceStatuses.Fault));
                }

                if (string.Equals(forceFieldId, field.FieldId, StringComparison.OrdinalIgnoreCase))
                    _simulation.ForceMoisture(field.FieldId, (double)field.MoistureThresholdPct - 4);

                var controller = devices.FirstOrDefault(d => d.FieldId == field.FieldId && DeviceTypes.IsIrrigationController(d.DeviceType));
                var irrigating = controller?.OperationalStatus == DeviceStatuses.Irrigating;

                var fieldReadings = new List<SensorReadingEntity>();
                foreach (var sensor in sensors)
                {
                    var state = _simulation.GetState(sensor.DeviceId, field.FieldId, sensor.BatteryPct);
                    state.Faulty = sensor.OperationalStatus == DeviceStatuses.Fault;
                    for (var i = readingsPerSensor - 1; i >= 0; i--)
                    {
                        // Burst readings are one second apart; a normal tick covers the scheduler interval.
                        var minutes = readingsPerSensor > 1 ? 1 / 60.0 : _control.IntervalSeconds / 60.0;
                        var reading = _simulation.NextReading(state, now.AddSeconds(-i), minutes, irrigating,
                            readingsPerSensor > 1 ? "Burst" : "Simulator");
                        fieldReadings.Add(reading);
                    }
                }
                readings.AddRange(fieldReadings);

                var current = fieldReadings.Where(r => sensors.Any(s => s.DeviceId == r.DeviceId && s.OperationalStatus != DeviceStatuses.Fault))
                    .GroupBy(r => r.DeviceId).Select(g => g.OrderByDescending(r => r.ReadingUtc).First()).ToList();
                if (current.Count > 0)
                    fieldAverages[field.FieldId] = (current.Average(r => r.SoilMoisturePct), current.OrderBy(r => r.SoilMoisturePct).First().DeviceId);
            }

            var written = await writer.WriteReadingsAsync(readings, ct);
            _control.AddReadings(written);

            var weatherLogs = 0;
            if (_control.LastWeatherUtc is null || now - _control.LastWeatherUtc >= WeatherEvery)
            {
                var log = _simulation.NextWeather(FarmDefaults.PrimaryWeatherStationId, now, WeatherEvery.TotalMinutes, "Simulator");
                weatherLogs = await writer.WriteEnvironmentalLogsAsync(new[] { log }, ct);
                _control.LastWeatherUtc = now;
            }

            if (sqlAvailable)
            {
                var heartbeats = readings.GroupBy(r => r.DeviceId)
                    .Select(g => g.OrderByDescending(r => r.ReadingUtc).First())
                    .Select(r => (r.DeviceId, r.ReadingUtc.UtcDateTime, (int?)r.BatteryPct)).ToList();
                if (_control.LastWeatherUtc == now) heartbeats.Add(("WS-01", now.UtcDateTime, null));
                await sp.GetRequiredService<IDeviceRegisterRepository>().TouchLastSeenAsync(heartbeats, ct);
            }

            // Sensor-driven irrigation (needs the register to know controller status).
            var actions = new List<AutomationAction>();
            if (sqlAvailable)
            {
                var automation = sp.GetRequiredService<IrrigationAutomation>();
                foreach (var field in fields)
                {
                    if (!fieldAverages.TryGetValue(field.FieldId, out var info)) continue;
                    var controller = devices.FirstOrDefault(d => d.FieldId == field.FieldId && DeviceTypes.IsIrrigationController(d.DeviceType));
                    var forced = string.Equals(forceFieldId, field.FieldId, StringComparison.OrdinalIgnoreCase);
                    var action = await automation.EvaluateAsync(field, controller, info.Average, info.Lowest, forced, ct);
                    if (action is not null) actions.Add(action);
                }
            }

            _control.LastTickUtc = now;
            _control.LastError = null;
            var batches = readings.GroupBy(r => r.PartitionKey).Sum(g => (g.Count() + 99) / 100);
            return new TickResult(written, weatherLogs, actions, Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1), batches);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _control.LastError = ex.Message;
            throw;
        }
        finally
        {
            _control.TickLock.Release();
        }
    }

    private async Task<(IReadOnlyList<FieldRecord> Fields, IReadOnlyList<DeviceRecord> Devices, bool SqlAvailable)> LoadTopologyAsync(
        IServiceProvider sp, CancellationToken ct)
    {
        if (_readiness.Sql.Ready)
        {
            try
            {
                var fields = await sp.GetRequiredService<IFieldRepository>().GetAllAsync(ct);
                var devices = await sp.GetRequiredService<IDeviceRegisterRepository>().GetAllAsync(ct);
                return (fields, devices, true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Device register unavailable for this tick; using the sample farm layout.");
            }
        }
        return (FarmSeedData.Fields, FarmSeedData.Devices, false);
    }
}
