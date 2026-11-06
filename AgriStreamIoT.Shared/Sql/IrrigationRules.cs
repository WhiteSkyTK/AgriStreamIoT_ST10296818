using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Messages;

namespace AgriStreamIoT.Shared.Sql;

/// <summary>
/// The business rules for an irrigation command, kept free of SQL so they are easy to read and test.
/// </summary>
public static class IrrigationRules
{
    /// <summary>The columns of the device row that the rules need (read under an update lock).</summary>
    public sealed class DeviceState
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
        public string? FieldId { get; set; }
        public string OperationalStatus { get; set; } = string.Empty;
        public string? LastCommandId { get; set; }

        /// <summary>From dbo.Fields, used to estimate water use.</summary>
        public decimal? FlowRateLitresPerMin { get; set; }
    }

    public static CommandApplyResult Decide(IrrigationCommand command, DeviceState? device, DateTime utcNow)
    {
        if (device is null)
            return CommandApplyResult.Rejected(null, $"Device {command.DeviceId} is not in the master device register.");

        if (!DeviceTypes.IsIrrigationController(device.DeviceType))
            return CommandApplyResult.Rejected(device.OperationalStatus,
                $"{device.DeviceId} is a {DeviceTypes.Label(device.DeviceType).ToLowerInvariant()}, not an irrigation controller.");

        if (!string.IsNullOrEmpty(device.FieldId) && !string.Equals(device.FieldId, command.FieldId, StringComparison.OrdinalIgnoreCase))
            return CommandApplyResult.Rejected(device.OperationalStatus,
                $"{device.DeviceId} belongs to field {device.FieldId}, not {command.FieldId}.");

        // Queue messages are delivered at least once: applying the same command twice must be harmless.
        if (string.Equals(device.LastCommandId, command.CommandId, StringComparison.OrdinalIgnoreCase))
            return CommandApplyResult.Ignored(device.OperationalStatus, "Duplicate delivery: this command was already applied.");

        if (command.Action == IrrigationActions.Stop)
        {
            if (command.Trigger == CommandTriggers.AutoStop &&
                !string.Equals(device.LastCommandId, command.ParentCommandId, StringComparison.OrdinalIgnoreCase))
                return CommandApplyResult.Ignored(device.OperationalStatus,
                    "Auto-stop skipped: a newer command has replaced this irrigation cycle.");

            if (device.OperationalStatus != DeviceStatuses.Irrigating)
                return CommandApplyResult.Ignored(device.OperationalStatus, "Controller was not irrigating.");

            return CommandApplyResult.AppliedChange(device.OperationalStatus, DeviceStatuses.Online,
                command.Trigger == CommandTriggers.AutoStop ? "Cycle finished: valve closed." : "Irrigation stopped: valve closed.");
        }

        if (DeviceStatuses.BlocksIrrigation(device.OperationalStatus))
            return CommandApplyResult.Rejected(device.OperationalStatus,
                $"Safety interlock: controller is {device.OperationalStatus}, irrigation not started.");

        var endsAt = utcNow.AddMinutes(command.DurationMinutes);
        var detail = device.OperationalStatus == DeviceStatuses.Irrigating
            ? $"Cycle extended: irrigating for {command.DurationMinutes} min from now."
            : $"Valve opened: irrigating for {command.DurationMinutes} min.";
        return CommandApplyResult.AppliedChange(device.OperationalStatus, DeviceStatuses.Irrigating, detail, endsAt) with
        {
            EstimatedWaterLitres = EstimateWaterLitres(command.Action, command.DurationMinutes, device.FlowRateLitresPerMin ?? 0m)
        };
    }

    /// <summary>Litres of water a START will use, from the field's flow rate.</summary>
    public static double EstimateWaterLitres(string action, int durationMinutes, decimal flowRateLitresPerMin) =>
        action == IrrigationActions.Start ? Math.Round((double)flowRateLitresPerMin * durationMinutes, 0) : 0;
}
