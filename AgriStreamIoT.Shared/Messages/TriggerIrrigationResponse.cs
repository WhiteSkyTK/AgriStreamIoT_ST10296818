namespace AgriStreamIoT.Shared.Messages;

/// <summary>202 Accepted body returned by the TriggerIrrigation HTTP function.</summary>
public sealed class TriggerIrrigationResponse
{
    public string CommandId { get; set; } = string.Empty;
    public string Status { get; set; } = "Queued";
    public string Queue { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string FieldId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public DateTimeOffset QueuedAtUtc { get; set; }
    public string? ServedBy { get; set; }
    public string Message { get; set; } = string.Empty;
}
