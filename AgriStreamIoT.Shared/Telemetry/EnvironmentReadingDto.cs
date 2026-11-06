namespace AgriStreamIoT.Shared.Telemetry;

/// <summary>Latest weather station conditions.</summary>
public sealed class EnvironmentReadingDto
{
    public string StationId { get; set; } = string.Empty;
    public DateTimeOffset ReadingUtc { get; set; }
    public double AirTemperatureC { get; set; }
    public double HumidityPct { get; set; }
    public double RainfallMm { get; set; }
    public double WindSpeedKmh { get; set; }
    public int WindDirectionDeg { get; set; }
    public double SolarRadiationWm2 { get; set; }
    public double Et0Mm { get; set; }
    public double PressureHpa { get; set; }
    public double UvIndex { get; set; }
    public string AlertLevel { get; set; } = "None";
    public string? Notes { get; set; }
}
