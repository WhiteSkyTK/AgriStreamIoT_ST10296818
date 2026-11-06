using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Messages;
using AgriStreamIoT.Shared.Sql;
using AgriStreamIoT.Web.Configuration;
using AgriStreamIoT.Web.Interfaces;
using AgriStreamIoT.Web.Models;
using Microsoft.Extensions.Options;

namespace AgriStreamIoT.Web.Services.Simulation;

/// <summary>
/// "An automated sensor detects a problem": when a field's average soil moisture drops below its
/// threshold, ask the TriggerIrrigation function (HTTP POST) to queue an irrigation command.
/// </summary>
public sealed class IrrigationAutomation
{
    private readonly IFarmFunctionsClient _functions;
    private readonly SimulatorControl _control;
    private readonly IOptionsMonitor<AgriStreamOptions> _options;
    private readonly ILogger<IrrigationAutomation> _logger;

    public IrrigationAutomation(IFarmFunctionsClient functions, SimulatorControl control,
        IOptionsMonitor<AgriStreamOptions> options, ILogger<IrrigationAutomation> logger)
    {
        _functions = functions;
        _control = control;
        _options = options;
        _logger = logger;
    }

    public async Task<AutomationAction?> EvaluateAsync(FieldRecord field, DeviceRecord? controller, double averageMoisturePct,
        string lowestSensorId, bool ignoreCooldown, CancellationToken cancellationToken)
    {
        var settings = _options.CurrentValue.Automation;
        var threshold = (double)field.MoistureThresholdPct;
        if (!settings.Enabled || averageMoisturePct >= threshold) return null;

        var now = DateTimeOffset.UtcNow;
        var cooldown = TimeSpan.FromMinutes(Math.Clamp(settings.CooldownMinutes, 1, 1440));
        var coolingDown = _control.LastAutoTrigger.TryGetValue(field.FieldId, out var last) && now - last < cooldown;
        if (coolingDown && !ignoreCooldown) return null;

        if (controller is null)
            return Record(field.FieldId, "-", "Skipped", "No irrigation controller registered for this field.");

        if (controller.OperationalStatus == DeviceStatuses.Irrigating) return null;   // already watering

        _control.LastAutoTrigger[field.FieldId] = now;

        if (DeviceStatuses.BlocksIrrigation(controller.OperationalStatus))
            return Record(field.FieldId, controller.DeviceId, "Skipped",
                $"Soil at {averageMoisturePct:F1}% but {controller.DeviceId} is {controller.OperationalStatus}; irrigation locked out.");

        var minutes = Math.Clamp(settings.IrrigationMinutes, IrrigationActions.MinDurationMinutes, IrrigationActions.MaxDurationMinutes);
        var call = await _functions.TriggerIrrigationAsync(new TriggerIrrigationRequest
        {
            DeviceId = controller.DeviceId,
            FieldId = field.FieldId,
            Action = IrrigationActions.Start,
            DurationMinutes = minutes,
            Trigger = CommandTriggers.Sensor,
            RequestedBy = $"Sensor {lowestSensorId}",
            Reason = $"Soil moisture {averageMoisturePct:F1}% fell below the {threshold:F0}% threshold"
        }, cancellationToken);

        if (call.Success && call.Value is not null)
        {
            _logger.LogInformation("Automation queued {CommandId} for {Field} ({Moisture:F1}% < {Threshold}%).",
                call.Value.CommandId, field.FieldId, averageMoisturePct, threshold);
            return Record(field.FieldId, controller.DeviceId, "Queued",
                $"{call.Value.CommandId}: soil at {averageMoisturePct:F1}% (threshold {threshold:F0}%), {minutes} min cycle requested.");
        }

        return Record(field.FieldId, controller.DeviceId, "Failed", call.Error ?? "TriggerIrrigation call failed.");
    }

    private AutomationAction Record(string fieldId, string deviceId, string outcome, string detail)
    {
        var action = new AutomationAction(DateTimeOffset.UtcNow, fieldId, deviceId, outcome, detail);
        _control.Record(action);
        return action;
    }
}
