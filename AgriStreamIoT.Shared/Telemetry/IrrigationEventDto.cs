namespace AgriStreamIoT.Shared.Telemetry;

/// <summary>An irrigation command as processed by the pipeline.</summary>
public sealed class IrrigationEventDto
{
    public string CommandId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string FieldId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public string Trigger { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? OutcomeDetail { get; set; }
    public string? NewStatus { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public DateTimeOffset ProcessedAtUtc { get; set; }
    public double LatencySeconds { get; set; }
    public double EstimatedWaterLitres { get; set; }
}
