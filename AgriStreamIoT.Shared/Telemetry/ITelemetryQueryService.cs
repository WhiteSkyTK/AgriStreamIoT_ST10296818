using AgriStreamIoT.Shared.Entities;

namespace AgriStreamIoT.Shared.Telemetry;

/// <summary>Read side of the Table Storage telemetry (used by the GET function and the web app).</summary>
public interface ITelemetryQueryService
{
    /// <summary>Latest reading per sensor, chart history, weather and recent irrigation for one field.</summary>
    Task<TelemetrySnapshot> GetFieldSnapshotAsync(string fieldId, int hours, CancellationToken cancellationToken = default);

    /// <summary>Latest conditions for several fields (farm overview).</summary>
    Task<TelemetrySnapshot> GetFarmSnapshotAsync(IReadOnlyCollection<string> fieldIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SensorReadingEntity>> GetRecentReadingsAsync(
        IReadOnlyCollection<string> fieldIds, string? deviceId, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EnvironmentalLogEntity>> GetEnvironmentalLogsAsync(
        string stationId, int hours, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IrrigationEventEntity>> GetIrrigationEventsAsync(
        IReadOnlyCollection<string> fieldIds, int takePerField, CancellationToken cancellationToken = default);

    /// <summary>Newest reading per sensor in a field (used to continue the simulator smoothly).</summary>
    Task<IReadOnlyList<SensorReadingEntity>> GetLatestPerSensorAsync(string fieldId, CancellationToken cancellationToken = default);

    Task<EnvironmentalLogEntity?> GetNewestEnvironmentalLogAsync(string stationId, CancellationToken cancellationToken = default);
}
