using AgriStreamIoT.Shared.Constants;

namespace AgriStreamIoT.Shared.Messages;

/// <summary>
/// The "Irrigation Command" message placed on the irrigation-commands Storage Queue by the
/// TriggerIrrigation function and processed by the ProcessIrrigationCommand queue trigger.
/// </summary>
public sealed class IrrigationCommand
{
    /// <summary>Unique id; also the idempotency key (a re-delivered message is applied once).</summary>
    public string CommandId { get; set; } = NewCommandId();

    /// <summary>Irrigation controller in the master device register, e.g. IC-F01.</summary>
    public string DeviceId { get; set; } = string.Empty;
    public string FieldId { get; set; } = string.Empty;

    /// <summary>START or STOP.</summary>
    public string Action { get; set; } = IrrigationActions.Start;

    /// <summary>How long a START runs before the automatic STOP (1–240 minutes).</summary>
    public int DurationMinutes { get; set; } = IrrigationActions.DefaultDurationMinutes;

    /// <summary>Manual, Sensor or AutoStop.</summary>
    public string Trigger { get; set; } = CommandTriggers.Manual;
    public string RequestedBy { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>For an automatic STOP: the START command whose cycle it ends.</summary>
    public string? ParentCommandId { get; set; }

    public static string NewCommandId() =>
        $"CMD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

    /// <summary>Builds a command from what the web app (or a sensor) posted.</summary>
    public static IrrigationCommand FromRequest(TriggerIrrigationRequest request, DateTimeOffset nowUtc) => new()
    {
        CommandId = NewCommandId(),
        DeviceId = request.DeviceId?.Trim() ?? string.Empty,
        FieldId = request.FieldId?.Trim() ?? string.Empty,
        Action = IrrigationActions.Normalise(request.Action) ?? request.Action?.Trim() ?? string.Empty,
        DurationMinutes = request.DurationMinutes ?? IrrigationActions.DefaultDurationMinutes,
        Trigger = CommandTriggers.Normalise(request.Trigger),
        RequestedBy = string.IsNullOrWhiteSpace(request.RequestedBy) ? "unknown" : request.RequestedBy.Trim(),
        Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
        RequestedAtUtc = nowUtc
    };

    /// <summary>The delayed STOP that ends a timed START cycle.</summary>
    public static IrrigationCommand AutoStopFor(IrrigationCommand start, DateTimeOffset nowUtc) => new()
    {
        CommandId = NewCommandId(),
        ParentCommandId = start.CommandId,
        DeviceId = start.DeviceId,
        FieldId = start.FieldId,
        Action = IrrigationActions.Stop,
        DurationMinutes = 0,
        Trigger = CommandTriggers.AutoStop,
        RequestedBy = "ProcessIrrigationCommand",
        Reason = $"Scheduled end of the {start.DurationMinutes}-minute cycle started by {start.CommandId}",
        RequestedAtUtc = nowUtc.AddMinutes(start.DurationMinutes)
    };
}
