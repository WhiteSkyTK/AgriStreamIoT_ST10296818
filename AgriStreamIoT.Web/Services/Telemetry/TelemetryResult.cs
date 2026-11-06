using AgriStreamIoT.Shared.Telemetry;

namespace AgriStreamIoT.Web.Services.Telemetry;

/// <summary>A telemetry snapshot plus how it was obtained (shown on the dashboard).</summary>
public sealed record TelemetryResult(
    TelemetrySnapshot Snapshot,
    bool ViaFunction,
    double ElapsedMs,
    string? FunctionError,
    string? FunctionServedBy)
{
    public string SourceLabel => ViaFunction
        ? "Azure Function GetLatestTelemetry"
        : "Direct Table Storage query (Function App unavailable)";
}
