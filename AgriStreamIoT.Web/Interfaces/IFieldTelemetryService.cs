using AgriStreamIoT.Web.Services.Telemetry;

namespace AgriStreamIoT.Web.Interfaces;

/// <summary>
/// Gets dashboard telemetry through the GetLatestTelemetry function, falling back to a direct
/// Table Storage query (clearly labelled) if the Function App cannot be reached.
/// </summary>
public interface IFieldTelemetryService
{
    Task<TelemetryResult> GetFieldAsync(string fieldId, int hours, CancellationToken cancellationToken = default);

    Task<TelemetryResult> GetFarmAsync(IReadOnlyCollection<string> fieldIds, CancellationToken cancellationToken = default);
}
