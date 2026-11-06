namespace AgriStreamIoT.Shared.Messages;

/// <summary>Body the web app POSTs to the TriggerIrrigation HTTP function.</summary>
public sealed class TriggerIrrigationRequest
{
    public string? DeviceId { get; set; }
    public string? FieldId { get; set; }

    /// <summary>START or STOP (case-insensitive).</summary>
    public string? Action { get; set; }
    public int? DurationMinutes { get; set; }

    /// <summary>Manual (button in the app) or Sensor (automation).</summary>
    public string? Trigger { get; set; }
    public string? RequestedBy { get; set; }
    public string? Reason { get; set; }
}
