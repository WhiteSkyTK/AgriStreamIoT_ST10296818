using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Messages;
using AgriStreamIoT.Shared.Sql;
using AgriStreamIoT.Shared.Telemetry;
using AgriStreamIoT.Web.Helpers;
using AgriStreamIoT.Web.Interfaces;
using AgriStreamIoT.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace AgriStreamIoT.Web.Controllers;

/// <summary>
/// The master device register (Azure SQL dbo.Devices). Everyone can view it; admins maintain it.
/// The ProcessIrrigationCommand function updates controller status here.
/// </summary>
[Authorize]
public sealed class DevicesController : Controller
{
    private readonly IDeviceRegisterRepository _devices;
    private readonly IFieldRepository _fields;
    private readonly IServiceProvider _services;

    public DevicesController(IDeviceRegisterRepository devices, IFieldRepository fields, IServiceProvider services)
    {
        _devices = devices;
        _fields = fields;
        _services = services;
    }

    public async Task<IActionResult> Index(string? field, string? type, string? status, CancellationToken cancellationToken)
    {
        var all = await _devices.GetAllAsync(cancellationToken);
        var filtered = all
            .Where(d => string.IsNullOrEmpty(field) || (field == "FARM" ? d.FieldId is null : d.FieldId == field))
            .Where(d => string.IsNullOrEmpty(type) || d.DeviceType == type)
            .Where(d => string.IsNullOrEmpty(status) || d.OperationalStatus == status)
            .ToList();

        return View(new DeviceListViewModel
        {
            Devices = filtered,
            Fields = (await _fields.GetAllAsync(cancellationToken)).ToList(),
            Counts = DeviceStatuses.All.ToDictionary(s => s, s => all.Count(d => d.OperationalStatus == s)),
            Total = all.Count,
            Field = field,
            Type = type,
            Status = status,
            IsAdmin = User.IsInRole(UserRoles.Admin)
        });
    }

    public async Task<IActionResult> Details(string id, CancellationToken cancellationToken)
    {
        var device = await _devices.GetAsync(id, cancellationToken);
        if (device is null) return NotFound();

        var irrigation = new List<IrrigationEventDto>();
        if (DeviceTypes.IsIrrigationController(device.DeviceType) && device.FieldId is not null)
        {
            try
            {
                var events = await _services.GetRequiredService<ITelemetryQueryService>()
                    .GetIrrigationEventsAsync(new[] { device.FieldId }, 15, cancellationToken);
                irrigation = events.Where(e => e.DeviceId == device.DeviceId).Select(e => e.ToDto()).ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ViewBag.StorageWarning = "Irrigation history unavailable: " + ex.GetBaseException().Message;
            }
        }

        return View(new DeviceDetailsViewModel
        {
            Device = device,
            History = await _devices.GetHistoryAsync(device.DeviceId, 30, cancellationToken),
            Irrigation = irrigation,
            IsAdmin = User.IsInRole(UserRoles.Admin)
        });
    }

    /// <summary>JSON polled by the irrigation panel until the queue trigger has applied the command.</summary>
    [HttpGet]
    public async Task<IActionResult> Status(string id, CancellationToken cancellationToken)
    {
        var device = await _devices.GetAsync(id, cancellationToken);
        if (device is null) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return Json(FieldsController.DeviceStatusJson(device), FarmJson.Options);
    }

    [HttpGet, Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> Create(string? field, CancellationToken cancellationToken) =>
        View("Edit", new DeviceEditViewModel
        {
            IsNew = true,
            FieldId = field,
            Fields = (await _fields.GetAllAsync(cancellationToken)).ToList()
        });

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> Create(DeviceEditViewModel model, CancellationToken cancellationToken)
    {
        model.IsNew = true;
        Validate(model);
        if (!ModelState.IsValid)
        {
            model.Fields = (await _fields.GetAllAsync(cancellationToken)).ToList();
            return View("Edit", model);
        }

        try
        {
            await _devices.CreateAsync(model.ToRecord(), User.Email(), cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            ModelState.AddModelError(string.Empty, "A device with this ID or serial number is already registered.");
            model.Fields = (await _fields.GetAllAsync(cancellationToken)).ToList();
            return View("Edit", model);
        }

        TempData["Success"] = $"{model.DeviceId.ToUpperInvariant()} added to the device register.";
        return RedirectToAction(nameof(Details), new { id = model.DeviceId.ToUpperInvariant() });
    }

    [HttpGet, Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> Edit(string id, CancellationToken cancellationToken)
    {
        var device = await _devices.GetAsync(id, cancellationToken);
        if (device is null) return NotFound();
        var model = DeviceEditViewModel.From(device);
        model.Fields = (await _fields.GetAllAsync(cancellationToken)).ToList();
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> Edit(DeviceEditViewModel model, CancellationToken cancellationToken)
    {
        model.IsNew = false;
        Validate(model);
        if (!ModelState.IsValid)
        {
            model.Fields = (await _fields.GetAllAsync(cancellationToken)).ToList();
            return View(model);
        }

        try
        {
            if (!await _devices.UpdateDetailsAsync(model.ToRecord(), cancellationToken)) return NotFound();
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            ModelState.AddModelError(nameof(model.SerialNumber), "Another device already uses this serial number.");
            model.Fields = (await _fields.GetAllAsync(cancellationToken)).ToList();
            return View(model);
        }

        TempData["Success"] = $"{model.DeviceId} updated.";
        return RedirectToAction(nameof(Details), new { id = model.DeviceId });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> SetStatus(string id, string status, string? reason, CancellationToken cancellationToken)
    {
        var canonical = DeviceStatuses.Normalise(status);
        if (canonical is null)
        {
            TempData["Error"] = "Choose a valid status.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var ok = await _devices.SetStatusAsync(id, canonical, User.Email(),
            string.IsNullOrWhiteSpace(reason) ? $"Set to {canonical} in the device register" : reason.Trim(), cancellationToken);
        TempData[ok ? "Success" : "Error"] = ok ? $"{id} is now {canonical}." : $"{id} was not found.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        var ok = await _devices.DeleteAsync(id, cancellationToken);
        TempData[ok ? "Success" : "Error"] = ok ? $"{id} removed from the device register." : $"{id} was not found.";
        return RedirectToAction(nameof(Index));
    }

    private void Validate(DeviceEditViewModel model)
    {
        if (!DeviceTypes.IsValid(model.DeviceType))
            ModelState.AddModelError(nameof(model.DeviceType), "Choose a device type.");
        if (model.IsNew && !DeviceStatuses.IsValid(model.OperationalStatus))
            ModelState.AddModelError(nameof(model.OperationalStatus), "Choose a status.");
        if (model.DeviceType is DeviceTypes.SoilSensor or DeviceTypes.IrrigationController && string.IsNullOrEmpty(model.FieldId))
            ModelState.AddModelError(nameof(model.FieldId), "Soil sensors and irrigation controllers belong to a field.");
    }
}
