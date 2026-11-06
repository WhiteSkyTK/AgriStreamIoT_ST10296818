using AgriStreamIoT.Shared.Messages;
using AgriStreamIoT.Shared.Telemetry;
using AgriStreamIoT.Web.Services.Functions;

namespace AgriStreamIoT.Web.Interfaces;

/// <summary>Calls the two HTTP-triggered Azure Functions.</summary>
public interface IFarmFunctionsClient
{
    /// <summary>GET /api/telemetry/latest?fieldId=... (GetLatestTelemetry).</summary>
    Task<FunctionCallResult<TelemetrySnapshot>> GetFieldTelemetryAsync(string fieldId, int hours, CancellationToken cancellationToken = default);

    /// <summary>GET /api/telemetry/latest?fieldIds=... (GetLatestTelemetry, farm summary).</summary>
    Task<FunctionCallResult<TelemetrySnapshot>> GetFarmTelemetryAsync(IEnumerable<string> fieldIds, CancellationToken cancellationToken = default);

    /// <summary>POST /api/irrigation/trigger (TriggerIrrigation).</summary>
    Task<FunctionCallResult<TriggerIrrigationResponse>> TriggerIrrigationAsync(TriggerIrrigationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Base URL in use, for the pipeline page.</summary>
    string? BaseUrl { get; }
}
