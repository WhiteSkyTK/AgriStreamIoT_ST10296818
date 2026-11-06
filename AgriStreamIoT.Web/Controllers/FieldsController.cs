using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Messages;
using AgriStreamIoT.Shared.Sql;
using AgriStreamIoT.Shared.Telemetry;
using AgriStreamIoT.Web.Helpers;
using AgriStreamIoT.Web.Interfaces;
using AgriStreamIoT.Web.Models;
using AgriStreamIoT.Web.Services.Analytics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace AgriStreamIoT.Web.Controllers;

/// <summary>
/// Field dashboards. The page and its live refresh call the GetLatestTelemetry function (HTTP GET);
/// "Trigger irrigation" calls the TriggerIrrigation function (HTTP POST).
/// </summary>
[Authorize]
public sealed class FieldsController : Controller
{
    private readonly IFieldRepository _fields;
    private readonly IDeviceRegisterRepository _devices;
    private readonly IFieldTelemetryService _telemetry;
    private readonly IFarmFunctionsClient _functions;
    private readonly ILogger<FieldsController> _logger;

    public FieldsController(IFieldRepository fields, IDeviceRegisterRepository devices, IFieldTelemetryService telemetry,
        IFarmFunctionsClient functions, ILogger<FieldsController> logger)
    {
        _fields = fields;
        _devices = devices;
        _telemetry = telemetry;
        _functions = functions;
        _logger = logger;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var fields = await _fields.GetAllAsync(cancellationToken);
        if (fields.Count == 0) return RedirectToAction("Index", "Dashboard");
        return RedirectToAction(nameof(Dashboard), new { id = fields[0].FieldId });
    }

    public async Task<IActionResult> Dashboard(string id, int hours = FarmDefaults.DashboardHours, CancellationToken cancellationToken = default)
    {
        var field = await _fields.GetAsync(id, cancellationToken);
        if (field is null) return NotFound();
        hours = NormaliseHours(hours);

        var devices = (await _devices.GetByFieldAsync(field.FieldId, cancellationToken)).ToList();
        var telemetry = await _telemetry.GetFieldAsync(field.FieldId, hours, cancellationToken);

        return View(new FieldDashboardViewModel
        {
            Field = field,
            Devices = devices,
            Controller = devices.FirstOrDefault(d => DeviceTypes.IsIrrigationController(d.DeviceType)),
            Telemetry = telemetry,
            Hours = hours,
            CanIrrigate = User.IsInRole(UserRoles.Farmer) || User.IsInRole(UserRoles.Admin),
            CanEditField = User.IsInRole(UserRoles.Agronomist) || User.IsInRole(UserRoles.Admin),
            AllFields = (await _fields.GetAllAsync(cancellationToken)).ToList()
        });
    }

    /// <summary>JSON for the dashboard's live refresh (every 15 seconds).</summary>
    [HttpGet]
    public async Task<IActionResult> Telemetry(string id, int hours = FarmDefaults.DashboardHours, CancellationToken cancellationToken = default)
    {
        var field = await _fields.GetAsync(id, cancellationToken);
        if (field is null) return NotFound();

        var telemetry = await _telemetry.GetFieldAsync(field.FieldId, NormaliseHours(hours), cancellationToken);
        var controller = (await _devices.GetByFieldAsync(field.FieldId, cancellationToken))
            .FirstOrDefault(d => DeviceTypes.IsIrrigationController(d.DeviceType));

        return Json(new
        {
            snapshot = telemetry.Snapshot,
            viaFunction = telemetry.ViaFunction,
            source = telemetry.SourceLabel,
            elapsedMs = Math.Round(telemetry.ElapsedMs, 0),
            functionError = telemetry.FunctionError,
            functionServedBy = telemetry.FunctionServedBy,
            threshold = field.MoistureThresholdPct,
            controller = controller is null ? null : DeviceStatusJson(controller)
        }, FarmJson.Options);
    }

    /// <summary>48-hour moisture outlook (Monte Carlo), cached for a minute per field.</summary>
    [HttpGet]
    public async Task<IActionResult> Outlook(string id, [FromServices] MoistureOutlookService outlook,
        [FromServices] IMemoryCache cache, CancellationToken cancellationToken)
    {
        var field = await _fields.GetAsync(id, cancellationToken);
        if (field is null) return NotFound();

        var result = await cache.GetOrCreateAsync($"outlook:{field.FieldId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            var telemetry = await _telemetry.GetFieldAsync(field.FieldId, 1, cancellationToken);
            var current = telemetry.Snapshot.Fields.FirstOrDefault()?.AvgSoilMoisturePct
                          ?? (double)field.MoistureThresholdPct + 4;
            return outlook.Run(field.FieldId, current, (double)field.MoistureThresholdPct, iterations: 20_000);
        });
        return Json(result, FarmJson.Options);
    }

    /// <summary>"Trigger irrigation": calls the TriggerIrrigation function, which queues the command.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = UserRoles.CanIrrigate)]
    public async Task<IActionResult> TriggerIrrigation(TriggerIrrigationForm form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { ok = false, error = "Check the duration (1–240 minutes) and try again." });

        var request = new TriggerIrrigationRequest
        {
            FieldId = form.FieldId,
            DeviceId = form.DeviceId,
            Action = form.Action,
            DurationMinutes = form.DurationMinutes,
            Trigger = CommandTriggers.Manual,
            RequestedBy = User.Email(),
            Reason = string.IsNullOrWhiteSpace(form.Reason) ? $"Requested from the {form.FieldId} dashboard" : form.Reason
        };

        var call = await _functions.TriggerIrrigationAsync(request, cancellationToken);
        if (!call.Success || call.Value is null)
        {
            _logger.LogWarning("TriggerIrrigation failed for {Device}: {Error}", form.DeviceId, call.Error);
            return StatusCode(StatusCodes.Status502BadGateway, new { ok = false, error = call.Error, elapsedMs = Math.Round(call.ElapsedMs) });
        }

        return Json(new
        {
            ok = true,
            commandId = call.Value.CommandId,
            status = call.Value.Status,
            queue = call.Value.Queue,
            message = call.Value.Message,
            servedBy = call.ServedBy ?? call.Value.ServedBy,
            elapsedMs = Math.Round(call.ElapsedMs)
        }, FarmJson.Options);
    }

    [HttpGet, Authorize(Roles = UserRoles.CanManageFields)]
    public async Task<IActionResult> Edit(string id, CancellationToken cancellationToken)
    {
        var field = await _fields.GetAsync(id, cancellationToken);
        if (field is null) return NotFound();
        return View(new FieldEditViewModel
        {
            FieldId = field.FieldId,
            Name = field.Name,
            Crop = field.Crop,
            MoistureThresholdPct = field.MoistureThresholdPct,
            FlowRateLitresPerMin = field.FlowRateLitresPerMin,
            Notes = field.Notes
        });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = UserRoles.CanManageFields)]
    public async Task<IActionResult> Edit(FieldEditViewModel model, CancellationToken cancellationToken)
    {
        var field = await _fields.GetAsync(model.FieldId, cancellationToken);
        if (field is null) return NotFound();
        model.Name = field.Name;
        if (!ModelState.IsValid) return View(model);

        await _fields.UpdateAgronomyAsync(field.FieldId, model.Crop, model.MoistureThresholdPct, model.FlowRateLitresPerMin,
            model.Notes, User.Email(), cancellationToken);
        TempData["Success"] = $"{field.Name}: irrigation threshold set to {model.MoistureThresholdPct:0.#}%.";
        return RedirectToAction(nameof(Dashboard), new { id = field.FieldId });
    }

    private static int NormaliseHours(int hours) => hours switch
    {
        <= 6 => 6,
        <= 12 => 12,
        <= 24 => 24,
        _ => 48
    };

    internal static object DeviceStatusJson(DeviceRecord d) => new
    {
        deviceId = d.DeviceId,
        name = d.DeviceName,
        status = d.OperationalStatus,
        lastCommandId = d.LastCommandId,
        lastCommandAction = d.LastCommandAction,
        lastCommandAt = d.LastCommandAtUtc is null ? null : Display.When(d.LastCommandAtUtc),
        irrigationEndsAtUtc = d.IrrigationEndsAtUtc is null ? (DateTimeOffset?)null
            : new DateTimeOffset(DateTime.SpecifyKind(d.IrrigationEndsAtUtc.Value, DateTimeKind.Utc)),
        irrigationEnds = d.IrrigationEndsAtUtc is null ? null : Display.Time(d.IrrigationEndsAtUtc),
        statusUpdatedBy = d.StatusUpdatedBy,
        statusUpdatedAt = Display.When(d.StatusUpdatedAtUtc),
        statusClass = Display.StatusClass(d.OperationalStatus)
    };
}
