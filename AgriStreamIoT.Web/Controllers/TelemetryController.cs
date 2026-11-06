using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Entities;
using AgriStreamIoT.Shared.Telemetry;
using AgriStreamIoT.Web.Interfaces;
using AgriStreamIoT.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriStreamIoT.Web.Controllers;

/// <summary>Browse the raw Table Storage data: sensor readings, weather logs, irrigation events, image audits.</summary>
[Authorize]
public sealed class TelemetryController : Controller
{
    private readonly ITelemetryQueryService _telemetry;
    private readonly ImageAuditStore _audits;
    private readonly IFieldRepository _fields;

    public TelemetryController(ITelemetryQueryService telemetry, ImageAuditStore audits, IFieldRepository fields)
    {
        _telemetry = telemetry;
        _audits = audits;
        _fields = fields;
    }

    public async Task<IActionResult> Index(string tab = "readings", string? field = null, string? device = null,
        int hours = 24, CancellationToken cancellationToken = default)
    {
        var fields = (await _fields.GetAllAsync(cancellationToken)).ToList();
        var fieldIds = string.IsNullOrEmpty(field) ? fields.Select(f => f.FieldId).ToList() : new List<string> { field };
        hours = Math.Clamp(hours, 1, 168);

        IReadOnlyList<SensorReadingEntity> readings = Array.Empty<SensorReadingEntity>();
        IReadOnlyList<EnvironmentalLogEntity> environment = Array.Empty<EnvironmentalLogEntity>();
        IReadOnlyList<IrrigationEventEntity> irrigation = Array.Empty<IrrigationEventEntity>();
        IReadOnlyList<ImageAuditEntity> audits = Array.Empty<ImageAuditEntity>();

        switch (tab)
        {
            case "environment":
                environment = await _telemetry.GetEnvironmentalLogsAsync(FarmDefaults.PrimaryWeatherStationId, hours, 200, cancellationToken);
                break;
            case "irrigation":
                irrigation = (await _telemetry.GetIrrigationEventsAsync(fieldIds, 50, cancellationToken)).Take(150).ToList();
                break;
            case "images":
                audits = await _audits.GetRecentAsync(StorageNames.DroneImagesContainer, 150, cancellationToken);
                break;
            default:
                tab = "readings";
                readings = await _telemetry.GetRecentReadingsAsync(fieldIds, string.IsNullOrWhiteSpace(device) ? null : device, 200, cancellationToken);
                break;
        }

        return View(new TelemetryExplorerViewModel
        {
            Tab = tab,
            Fields = fields,
            Field = field,
            Device = device,
            Hours = hours,
            Readings = readings,
            Environment = environment,
            Irrigation = irrigation,
            Audits = audits
        });
    }
}
