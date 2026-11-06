namespace AgriStreamIoT.Shared.Telemetry;

/// <summary>One averaged point of a sensor's history (one time bucket).</summary>
public sealed class TelemetryPoint
{
    public string DeviceId { get; set; } = string.Empty;
    public DateTimeOffset TimeUtc { get; set; }
    public double SoilMoisturePct { get; set; }
    public double SoilTemperatureC { get; set; }
}
