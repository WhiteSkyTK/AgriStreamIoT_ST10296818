namespace AgriStreamIoT.Shared.Telemetry;

/// <summary>Latest reading of one soil sensor.</summary>
public sealed class DeviceReadingDto
{
    public string DeviceId { get; set; } = string.Empty;
    public string FieldId { get; set; } = string.Empty;
    public DateTimeOffset ReadingUtc { get; set; }
    public double SoilMoisturePct { get; set; }
    public double SoilTemperatureC { get; set; }
    public double AirTemperatureC { get; set; }
    public double HumidityPct { get; set; }
    public double SoilPh { get; set; }
    public double SoilEcDsM { get; set; }
    public double NitrogenPpm { get; set; }
    public int BatteryPct { get; set; }
    public int SignalDbm { get; set; }

    /// <summary>Seconds since the reading was taken (at the time the snapshot was built).</summary>
    public double AgeSeconds { get; set; }
}
