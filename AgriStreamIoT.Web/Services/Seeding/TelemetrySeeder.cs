using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Entities;
using AgriStreamIoT.Shared.Sql;
using AgriStreamIoT.Shared.Telemetry;
using AgriStreamIoT.Web.Interfaces;
using AgriStreamIoT.Web.Models;
using AgriStreamIoT.Web.Services.Simulation;

namespace AgriStreamIoT.Web.Services.Seeding;

/// <summary>
/// Fills Table Storage with realistic history: soil readings every 15 minutes, weather every
/// 30 minutes and the irrigation cycles the automation would have run. On later start-ups it only
/// back-fills the gap since the newest reading (up to 24 h), so dashboards never look stale.
/// Timestamps sit on fixed 15-minute boundaries, which makes the seeding idempotent across instances.
/// </summary>
public sealed class TelemetrySeeder
{
    private static readonly TimeSpan SensorStep = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan WeatherStep = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MaxHistory = TimeSpan.FromHours(24);

    private readonly IServiceProvider _services;
    private readonly FarmSimulation _simulation;
    private readonly ILogger<TelemetrySeeder> _logger;

    public TelemetrySeeder(IServiceProvider services, FarmSimulation simulation, ILogger<TelemetrySeeder> logger)
    {
        _services = services;
        _simulation = simulation;
        _logger = logger;
    }

    public sealed record SeedSummary(int Readings, int WeatherLogs, int IrrigationEvents);

    /// <summary>Seeds 24 h of history, or back-fills since the newest stored reading.</summary>
    public async Task<SeedSummary> SeedOrBackfillAsync(CancellationToken cancellationToken)
    {
        var queries = _services.GetRequiredService<ITelemetryQueryService>();
        var writer = _services.GetRequiredService<TelemetryWriter>();
        var (fields, devices) = await LoadTopologyAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var lastSlot = Floor(now - TimeSpan.FromMinutes(1), SensorStep);
        var random = new Random();
        int readings = 0, weather = 0, events = 0;

        foreach (var field in fields)
        {
            var sensors = devices.Where(d => d.FieldId == field.FieldId && d.DeviceType == DeviceTypes.SoilSensor).ToList();
            if (sensors.Count == 0) continue;
            var controller = devices.FirstOrDefault(d => d.FieldId == field.FieldId && DeviceTypes.IsIrrigationController(d.DeviceType));

            var latest = await queries.GetLatestPerSensorAsync(field.FieldId, cancellationToken);
            var newest = latest.Count == 0 ? (DateTimeOffset?)null : latest.Max(r => r.ReadingUtc);
            if (newest is not null && lastSlot - newest.Value < TimeSpan.FromMinutes(20)) continue;   // fresh enough

            foreach (var reading in latest)
                _simulation.Prime(reading, sensors.Any(s => s.DeviceId == reading.DeviceId && s.OperationalStatus == DeviceStatuses.Fault));

            var start = newest is null || now - newest.Value > MaxHistory
                ? Floor(now - MaxHistory, SensorStep)
                : Floor(newest.Value, SensorStep) + SensorStep;

            var (fieldReadings, fieldEvents) = GenerateField(field, sensors, controller, start, lastSlot, now, random);
            readings += await writer.WriteReadingsAsync(fieldReadings, cancellationToken);
            events += await writer.WriteIrrigationEventsAsync(fieldEvents, cancellationToken);
        }

        // Weather station history.
        var newestWeather = await queries.GetNewestEnvironmentalLogAsync(FarmDefaults.PrimaryWeatherStationId, cancellationToken);
        if (newestWeather is null || lastSlot - newestWeather.ReadingUtc >= WeatherStep)
        {
            var from = newestWeather is null || now - newestWeather.ReadingUtc > MaxHistory
                ? Floor(now - MaxHistory, WeatherStep)
                : Floor(newestWeather.ReadingUtc, WeatherStep) + WeatherStep;
            var logs = new List<EnvironmentalLogEntity>();
            for (var t = from; t <= lastSlot; t += WeatherStep)
                logs.Add(FarmSimulation.Weather(FarmDefaults.PrimaryWeatherStationId, t, WeatherStep.TotalMinutes, "Seed", random));
            weather = await writer.WriteEnvironmentalLogsAsync(logs, cancellationToken);
        }

        if (readings + weather + events > 0)
            _logger.LogInformation("Telemetry history written: {Readings} readings, {Weather} weather logs, {Events} irrigation events.",
                readings, weather, events);
        return new SeedSummary(readings, weather, events);
    }

    private (List<SensorReadingEntity> Readings, List<IrrigationEventEntity> Events) GenerateField(
        FieldRecord field, List<DeviceRecord> sensors, DeviceRecord? controller,
        DateTimeOffset start, DateTimeOffset end, DateTimeOffset now, Random random)
    {
        var readings = new List<SensorReadingEntity>();
        var events = new List<IrrigationEventEntity>();
        var threshold = (double)field.MoistureThresholdPct;
        var canIrrigate = controller is not null && !DeviceStatuses.BlocksIrrigation(controller.OperationalStatus);

        // The offline probe stopped reporting six hours ago.
        var offlineSince = now - TimeSpan.FromHours(6);
        DateTimeOffset? irrigatingUntil = null;
        string? cycleId = null;

        for (var t = start; t <= end; t += SensorStep)
        {
            // Historical automation: start a cycle when the field average dips below the threshold,
            // but never in the last 90 minutes, so the live register (Azure SQL) stays the source of truth.
            var states = sensors.Where(s => s.OperationalStatus != DeviceStatuses.Fault)
                .Select(s => _simulation.GetState(s.DeviceId, s.FieldId!, s.BatteryPct)).ToList();
            var average = states.Count == 0 ? threshold + 5 : states.Average(s => s.MoisturePct);

            if (canIrrigate && irrigatingUntil is null && average < threshold + 0.5 && t < now - TimeSpan.FromMinutes(90))
            {
                var minutes = random.Next(3, 7) * 15;   // 45–90 minutes
                irrigatingUntil = t + TimeSpan.FromMinutes(minutes);
                cycleId = $"CMD-{t.UtcDateTime:yyyyMMddHHmmss}-H{field.FieldId}";
                events.Add(HistoryEvent(field, controller!, cycleId, null, IrrigationActions.Start, minutes, CommandTriggers.Sensor,
                    $"Soil moisture {average:F1}% fell below the {threshold:F0}% threshold", t,
                    DeviceStatuses.Online, DeviceStatuses.Irrigating));
            }
            else if (irrigatingUntil is not null && t >= irrigatingUntil)
            {
                events.Add(HistoryEvent(field, controller!, $"{cycleId}-STOP", cycleId, IrrigationActions.Stop, 0, CommandTriggers.AutoStop,
                    "Scheduled end of irrigation cycle", t, DeviceStatuses.Irrigating, DeviceStatuses.Online));
                irrigatingUntil = null;
            }

            foreach (var sensor in sensors)
            {
                if (sensor.OperationalStatus == DeviceStatuses.Offline && t > offlineSince) continue;
                var state = _simulation.GetState(sensor.DeviceId, sensor.FieldId!, sensor.BatteryPct);
                state.Faulty = sensor.OperationalStatus == DeviceStatuses.Fault;
                readings.Add(FarmSimulation.Step(state, t, SensorStep.TotalMinutes, irrigatingUntil is not null, "Seed", random));
            }
        }

        return (readings, events);
    }

    private static IrrigationEventEntity HistoryEvent(FieldRecord field, DeviceRecord controller, string commandId, string? parentId,
        string action, int minutes, string trigger, string reason, DateTimeOffset at, string previous, string next) =>
        new IrrigationEventEntity
        {
            CommandId = commandId,
            ParentCommandId = parentId,
            DeviceId = controller.DeviceId,
            FieldId = field.FieldId,
            Action = action,
            DurationMinutes = minutes,
            Trigger = trigger,
            RequestedBy = trigger == CommandTriggers.AutoStop ? "ProcessIrrigationCommand" : "Soil moisture automation",
            Reason = reason,
            RequestedAtUtc = at,
            ProcessedAtUtc = at.AddSeconds(3),
            Outcome = CommandOutcomes.Applied,
            OutcomeDetail = action == IrrigationActions.Start
                ? $"Valve opened: irrigating for {minutes} min."
                : "Cycle finished: valve closed.",
            PreviousStatus = previous,
            NewStatus = next,
            EstimatedWaterLitres = action == IrrigationActions.Start ? (double)field.FlowRateLitresPerMin * minutes : 0,
            LatencySeconds = 3,
            DequeueCount = 1,
            ProcessedBy = "Sample history"
        }.WithKeys();

    private async Task<(IReadOnlyList<FieldRecord> Fields, IReadOnlyList<DeviceRecord> Devices)> LoadTopologyAsync(CancellationToken ct)
    {
        try
        {
            var fields = await _services.GetRequiredService<IFieldRepository>().GetAllAsync(ct);
            var devices = await _services.GetRequiredService<IDeviceRegisterRepository>().GetAllAsync(ct);
            if (fields.Count > 0 && devices.Count > 0) return (fields, devices);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Azure SQL not available for the farm layout; using the built-in sample farm.");
        }
        return (FarmSeedData.Fields, FarmSeedData.Devices);
    }

    private static DateTimeOffset Floor(DateTimeOffset value, TimeSpan step) =>
        new(value.UtcTicks - value.UtcTicks % step.Ticks, TimeSpan.Zero);
}
