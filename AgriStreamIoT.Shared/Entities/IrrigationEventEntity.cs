using AgriStreamIoT.Shared.Storage;
using Azure;
using Azure.Data.Tables;

namespace AgriStreamIoT.Shared.Entities;

/// <summary>
/// One processed irrigation command in the IrrigationEvents table.
/// PartitionKey = FieldId, RowKey = newest-first ticks + CommandId.
/// </summary>
public sealed class IrrigationEventEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string CommandId { get; set; } = string.Empty;
    public string? ParentCommandId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string FieldId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public string Trigger { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public DateTimeOffset ProcessedAtUtc { get; set; }

    /// <summary>Applied, Ignored or Rejected.</summary>
    public string Outcome { get; set; } = string.Empty;
    public string? OutcomeDetail { get; set; }
    public string? PreviousStatus { get; set; }
    public string? NewStatus { get; set; }
    public double EstimatedWaterLitres { get; set; }

    /// <summary>Seconds between the request and processing (queue wait + processing).</summary>
    public double LatencySeconds { get; set; }
    public int DequeueCount { get; set; }
    public string ProcessedBy { get; set; } = string.Empty;

    public IrrigationEventEntity WithKeys()
    {
        PartitionKey = FieldId;
        RowKey = TableKeys.NewestFirst(ProcessedAtUtc, CommandId);
        return this;
    }
}
