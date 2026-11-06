using System.Text.Json;
using AgriStreamIoT.Functions.Services;
using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Messages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AgriStreamIoT.Functions.Functions;

/// <summary>
/// FUNCTION 3 of 4 – HTTP TRIGGER (POST).
/// Called when a user clicks "Trigger irrigation" or when the sensor automation detects dry soil.
/// It validates the request and places an "Irrigation Command" message on the irrigation-commands
/// Storage Queue, then answers 202 Accepted straight away (queue-based load levelling: bursts of
/// commands never block the caller and are never lost).
///   POST /api/irrigation/trigger
///   { "deviceId": "IC-F01", "fieldId": "F01", "action": "START", "durationMinutes": 30,
///     "trigger": "Manual", "requestedBy": "farmer@agristream.co.za", "reason": "Soil dry" }
/// </summary>
public sealed class TriggerIrrigation
{
    private readonly ILogger<TriggerIrrigation> _logger;

    public TriggerIrrigation(ILogger<TriggerIrrigation> logger) => _logger = logger;

    [Function(nameof(TriggerIrrigation))]
    public async Task<TriggerIrrigationOutput> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "irrigation/trigger")] HttpRequest request,
        FunctionContext context)
    {
        request.HttpContext.Response.Headers["X-Served-By"] = FunctionSettings.InstanceId;

        TriggerIrrigationRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<TriggerIrrigationRequest>(
                request.Body, FarmJson.Options, context.CancellationToken);
        }
        catch (JsonException)
        {
            return TriggerIrrigationOutput.Error(new BadRequestObjectResult(new { errors = new[] { "Body must be valid JSON." } }));
        }

        if (body is null)
            return TriggerIrrigationOutput.Error(new BadRequestObjectResult(new { errors = new[] { "Request body is empty." } }));

        var command = IrrigationCommand.FromRequest(body, DateTimeOffset.UtcNow);
        var errors = IrrigationCommandValidator.Validate(command);
        if (errors.Count > 0)
            return TriggerIrrigationOutput.Error(new BadRequestObjectResult(new { errors }));

        _logger.LogInformation(
            "Irrigation command {CommandId} queued: {Action} {DeviceId} on {FieldId} for {Minutes} min ({Trigger} by {RequestedBy}).",
            command.CommandId, command.Action, command.DeviceId, command.FieldId, command.DurationMinutes,
            command.Trigger, command.RequestedBy);

        var accepted = new TriggerIrrigationResponse
        {
            CommandId = command.CommandId,
            Status = "Queued",
            Queue = StorageNames.IrrigationCommandsQueue,
            DeviceId = command.DeviceId,
            FieldId = command.FieldId,
            Action = command.Action,
            DurationMinutes = command.Action == IrrigationActions.Start ? command.DurationMinutes : 0,
            QueuedAtUtc = command.RequestedAtUtc,
            ServedBy = FunctionSettings.InstanceId,
            Message = $"{command.Action} for {command.DeviceId} is on the {StorageNames.IrrigationCommandsQueue} queue."
        };

        return new TriggerIrrigationOutput
        {
            QueueMessage = FarmJson.Serialize(command),
            HttpResponse = new ObjectResult(accepted) { StatusCode = StatusCodes.Status202Accepted }
        };
    }
}

/// <summary>Two outputs from one invocation: the queue message and the HTTP response.</summary>
public sealed class TriggerIrrigationOutput
{
    /// <summary>JSON irrigation command. Null = nothing is written to the queue.</summary>
    [QueueOutput(StorageNames.IrrigationCommandsQueue, Connection = StorageNames.FunctionsStorageSetting)]
    public string? QueueMessage { get; set; }

    [HttpResult]
    public IActionResult HttpResponse { get; set; } = new OkResult();

    public static TriggerIrrigationOutput Error(IActionResult result) => new() { QueueMessage = null, HttpResponse = result };
}
