using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Entities;
using AgriStreamIoT.Shared.Sql;
using AgriStreamIoT.Shared.Telemetry;
using AgriStreamIoT.Shared.Time;
using AgriStreamIoT.Web.Interfaces;
using AgriStreamIoT.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriStreamIoT.Web.Controllers;

/// <summary>Farm overview for every role: field conditions, device health and pipeline activity.</summary>
[Authorize]
public sealed class DashboardController : Controller
{
    private readonly IFieldRepository _fields;
    private readonly IDeviceRegisterRepository _devices;
    private readonly IFieldTelemetryService _telemetry;
    private readonly IServiceProvider _services;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(IFieldRepository fields, IDeviceRegisterRepository devices,
        IFieldTelemetryService telemetry, IServiceProvider services, ILogger<DashboardController> logger)
    {
        _fields = fields;
        _devices = devices;
        _telemetry = telemetry;
        _services = services;
        _logger = logger;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var fields = await _fields.GetAllAsync(cancellationToken);
        var devices = await _devices.GetAllAsync(cancellationToken);
        var telemetry = await _telemetry.GetFarmAsync(fields.Select(f => f.FieldId).ToList(), cancellationToken);

        // Image audits are a nice-to-have on this page; storage problems should not hide the dashboard.
        List<ImageAuditEntity> audits = new();
        string? storageWarning = null;
        try
        {
            audits = (await _services.GetRequiredService<ImageAuditStore>()
                .GetRecentAsync(StorageNames.DroneImagesContainer, 200, cancellationToken)).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            storageWarning = "Drone image audit trail unavailable: " + ex.GetBaseException().Message;
            _logger.LogWarning(ex, "Image audits unavailable on dashboard.");
        }

        var todayStartUtc = new DateTimeOffset(FarmClock.NowLocal.Date, FarmClock.LocalOffset).ToUniversalTime();
        var cards = fields.Select(f => new FieldCard
        {
            Field = f,
            Telemetry = telemetry.Snapshot.Fields.FirstOrDefault(t => t.FieldId == f.FieldId),
            Controller = devices.FirstOrDefault(d => d.FieldId == f.FieldId && DeviceTypes.IsIrrigationController(d.DeviceType)),
            Sensors = devices.Count(d => d.FieldId == f.FieldId && d.DeviceType == DeviceTypes.SoilSensor),
            SensorsWithIssues = devices.Count(d => d.FieldId == f.FieldId && d.DeviceType == DeviceTypes.SoilSensor
                                                   && d.OperationalStatus is DeviceStatuses.Offline or DeviceStatuses.Fault)
        }).ToList();

        var counts = DeviceStatuses.All.ToDictionary(s => s, s => devices.Count(d => d.OperationalStatus == s));

        return View(new DashboardViewModel
        {
            Fields = cards,
            Telemetry = telemetry,
            DeviceCounts = counts,
            TotalDevices = devices.Count,
            RecentIrrigation = telemetry.Snapshot.RecentIrrigation,
            RecentAudits = audits.Take(6).ToList(),
            ImagesProcessedToday = audits.Count(a => a.ProcessedAtUtc >= todayStartUtc && a.Status == ImageAuditStatuses.Processed),
            WaterTodayLitres = telemetry.Snapshot.RecentIrrigation
                .Where(e => e.ProcessedAtUtc >= todayStartUtc && e.Outcome == CommandOutcomes.Applied)
                .Sum(e => e.EstimatedWaterLitres),
            StorageWarning = storageWarning
        });
    }
}
