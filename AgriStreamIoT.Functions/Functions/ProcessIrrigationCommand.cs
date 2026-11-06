using AgriStreamIoT.Functions.Services;
using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Entities;
using AgriStreamIoT.Shared.Messages;
using AgriStreamIoT.Shared.Sql;
using AgriStreamIoT.Shared.Telemetry;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AgriStreamIoT.Functions.Functions;

/// <summary>
/// FUNCTION 4 of 4 – QUEUE TRIGGER.
/// Listens to the irrigation-commands Storage Queue, applies each command to the master device
/// register in Azure SQL (the controller's operational status becomes Irrigating / Online) and
/// records the irrigation event in Table Storage. For a timed START it schedules the automatic STOP.
/// Reliability: a failure (e.g. SQL resuming from pause) throws, so the message becomes visible
/// again and is retried up to 5 times before moving to irrigation-commands-poison; commands are
/// idempotent so a retry never applies twice.
/// </summary>
public sealed class ProcessIrrigationCommand
{
    private readonly IDeviceRegisterRepository _register;
    private readonly TelemetryWriter _telemetry;
    private readonly IrrigationScheduler _scheduler;
    private readonly ILogger<ProcessIrrigationCommand> _logger;

    public ProcessIrrigationCommand(
        IDeviceRegisterRepository register,
        TelemetryWriter telemetry,
        IrrigationScheduler scheduler,
        ILogger<ProcessIrrigationCommand> logger)
    {
        _register = register;
        _telemetry = telemetry;
        _scheduler = scheduler;
        _logger = logger;
    }

    [Function(nameof(ProcessIrrigationCommand))]
    public async Task Run(
        [QueueTrigger(StorageNames.IrrigationCommandsQueue, Connection = StorageNames.FunctionsStorageSetting)]
        string message,
        FunctionContext context)
    {
        var cancellationToken = context.CancellationToken;
        var dequeueCount = ReadDequeueCount(context);

        var command = FarmJson.TryParseCommand(message);
        if (command is null || IrrigationCommandValidator.Validate(command).Count > 0)
        {
            // A malformed message will never succeed; log it and let it be removed instead of retrying.
            _logger.LogError("Irrigation queue message rejected as malformed: {Message}", Truncate(message, 500));
            return;
        }

        _logger.LogInformation("Processing irrigation command {CommandId} ({Action} {DeviceId}), attempt {Attempt}.",
            command.CommandId, command.Action, command.DeviceId, dequeueCount);

        // Azure SQL: update the device's operational status in the master register.
        var result = await _register.ApplyIrrigationCommandAsync(command, nameof(ProcessIrrigationCommand), cancellationToken);

        var processedAt = DateTimeOffset.UtcNow;
        var irrigationEvent = new IrrigationEventEntity
        {
            CommandId = command.CommandId,
            ParentCommandId = command.ParentCommandId,
            DeviceId = command.DeviceId,
            FieldId = command.FieldId,
            Action = command.Action,
            DurationMinutes = command.Action == IrrigationActions.Start ? command.DurationMinutes : 0,
            Trigger = command.Trigger,
            RequestedBy = command.RequestedBy,
            Reason = command.Reason,
            RequestedAtUtc = command.RequestedAtUtc,
            ProcessedAtUtc = processedAt,
            Outcome = result.Outcome,
            OutcomeDetail = result.Detail,
            PreviousStatus = result.PreviousStatus,
            NewStatus = result.NewStatus,
            EstimatedWaterLitres = result.EstimatedWaterLitres,
            // The scheduled STOP is requested "for" the end of the cycle; latency counts from then.
            LatencySeconds = Math.Max(0, Math.Round((processedAt - command.RequestedAtUtc).TotalSeconds, 1)),
            DequeueCount = dequeueCount,
            ProcessedBy = $"{nameof(ProcessIrrigationCommand)}@{FunctionSettings.InstanceId}"
        }.WithKeys();

        // Table Storage: irrigation events are high-frequency, non-relational history.
        await _telemetry.RecordIrrigationEventAsync(irrigationEvent, cancellationToken);

        if (result.Applied && command.Action == IrrigationActions.Start)
        {
            var stop = await _scheduler.ScheduleAutoStopAsync(command, cancellationToken);
            _logger.LogInformation("Auto-stop {StopId} scheduled for {DeviceId} in {Minutes} min.",
                stop.CommandId, command.DeviceId, command.DurationMinutes);
        }

        _logger.LogInformation("Irrigation command {CommandId}: {Outcome} - {Detail} ({Previous} -> {New}).",
            command.CommandId, result.Outcome, result.Detail, result.PreviousStatus ?? "n/a", result.NewStatus ?? "n/a");
    }

    private static int ReadDequeueCount(FunctionContext context)
    {
        if (context.BindingContext.BindingData.TryGetValue("DequeueCount", out var value) && value is not null)
        {
            if (int.TryParse(value.ToString(), out var count)) return count;
        }
        return 1;
    }

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..max];
}
