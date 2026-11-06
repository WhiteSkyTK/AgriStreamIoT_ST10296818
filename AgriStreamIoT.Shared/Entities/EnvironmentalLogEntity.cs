using AgriStreamIoT.Shared.Storage;
using Azure;
using Azure.Data.Tables;

namespace AgriStreamIoT.Shared.Entities;

/// <summary>
/// One weather station log in the EnvironmentalLogs table.
/// PartitionKey = StationId, RowKey = newest-first ticks + StationId.
/// </summary>
public sealed class EnvironmentalLogEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string StationId { get; set; } = string.Empty;
    public DateTimeOffset ReadingUtc { get; set; }
    public double AirTemperatureC { get; set; }
    public double HumidityPct { get; set; }
    public double RainfallMm { get; set; }
    public double WindSpeedKmh { get; set; }
    public int WindDirectionDeg { get; set; }
    public double SolarRadiationWm2 { get; set; }

    /// <summary>Reference evapotranspiration for the interval (mm).</summary>
    public double Et0Mm { get; set; }
    public double PressureHpa { get; set; }
    public double UvIndex { get; set; }

    /// <summary>None, Advisory or Warning (e.g. heat stress, high wind, frost risk).</summary>
    public string AlertLevel { get; set; } = "None";
    public string? Notes { get; set; }
    public string Source { get; set; } = "Sensor";

    public EnvironmentalLogEntity WithKeys()
    {
        PartitionKey = StationId;
        RowKey = TableKeys.NewestFirst(ReadingUtc, StationId);
        return this;
    }
}
