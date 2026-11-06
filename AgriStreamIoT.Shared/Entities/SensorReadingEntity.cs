using AgriStreamIoT.Shared.Storage;
using Azure;
using Azure.Data.Tables;

namespace AgriStreamIoT.Shared.Entities;

/// <summary>
/// One soil sensor reading in the SensorTelemetry table.
/// PartitionKey = FieldId (all sensors of a field together), RowKey = newest-first ticks + DeviceId.
/// </summary>
public sealed class SensorReadingEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string DeviceId { get; set; } = string.Empty;
    public string FieldId { get; set; } = string.Empty;
    public DateTimeOffset ReadingUtc { get; set; }

    /// <summary>Volumetric water content at root depth (%).</summary>
    public double SoilMoisturePct { get; set; }
    public double SoilTemperatureC { get; set; }
    public double AirTemperatureC { get; set; }
    public double HumidityPct { get; set; }
    public double SoilPh { get; set; }

    /// <summary>Electrical conductivity (salinity), dS/m.</summary>
    public double SoilEcDsM { get; set; }
    public double NitrogenPpm { get; set; }
    public int BatteryPct { get; set; }
    public int SignalDbm { get; set; }

    /// <summary>Seed (sample history), Simulator (live demo stream) or Sensor (real device).</summary>
    public string Source { get; set; } = "Sensor";

    /// <summary>Sets the keys from FieldId, DeviceId and ReadingUtc.</summary>
    public SensorReadingEntity WithKeys()
    {
        PartitionKey = FieldId;
        RowKey = TableKeys.NewestFirst(ReadingUtc, DeviceId);
        return this;
    }
}
