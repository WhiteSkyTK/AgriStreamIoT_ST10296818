namespace AgriStreamIoT.Shared.Telemetry;

/// <summary>Latest conditions of one field, rolled up from its sensors.</summary>
public sealed class FieldTelemetrySummary
{
    public string FieldId { get; set; } = string.Empty;
    public int SensorsReporting { get; set; }
    public double? AvgSoilMoisturePct { get; set; }
    public double? MinSoilMoisturePct { get; set; }
    public double? AvgSoilTemperatureC { get; set; }
    public double? AvgSoilPh { get; set; }
    public double? AvgSoilEcDsM { get; set; }
    public DateTimeOffset? LatestReadingUtc { get; set; }
    public List<DeviceReadingDto> Sensors { get; set; } = new();
}
