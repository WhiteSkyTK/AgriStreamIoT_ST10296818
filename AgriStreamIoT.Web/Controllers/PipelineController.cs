using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Web.Helpers;
using AgriStreamIoT.Web.Interfaces;
using AgriStreamIoT.Web.Models;
using AgriStreamIoT.Web.Services.Health;
using AgriStreamIoT.Web.Services.Seeding;
using AgriStreamIoT.Web.Services.Simulation;
using AgriStreamIoT.Web.Services.Startup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriStreamIoT.Web.Controllers;

/// <summary>
/// Admin view of the serverless pipeline: live health of every Azure component, the queue, the
/// sensor stream, and buttons that create peak-hour bursts on demand for the demo and screenshots.
/// </summary>
[Authorize(Roles = UserRoles.Admin)]
public sealed class PipelineController : Controller
{
    private readonly ISystemHealthService _health;
    private readonly AppReadiness _readiness;
    private readonly SimulatorControl _simulator;
    private readonly IServiceProvider _services;

    public PipelineController(ISystemHealthService health, AppReadiness readiness, SimulatorControl simulator, IServiceProvider services)
    {
        _health = health;
        _readiness = readiness;
        _simulator = simulator;
        _services = services;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var fields = new List<FieldRecord>();
        try { fields = (await _services.GetRequiredService<IFieldRepository>().GetAllAsync(cancellationToken)).ToList(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { fields = FarmSeedData.Fields.ToList(); }

        return View(new PipelineViewModel
        {
            Health = await _health.CheckAsync(cancellationToken),
            Readiness = _readiness,
            Simulator = _simulator,
            RecentActions = _simulator.RecentActions,
            Fields = fields,
            Instance = InstanceInfo.Snapshot(),
            InstanceId = InstanceInfo.ShortId
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult ToggleSimulator(bool enabled)
    {
        _simulator.Enabled = enabled;
        TempData["Success"] = enabled
            ? $"Sensor stream on: every probe reports every {_simulator.IntervalSeconds} s (this instance)."
            : "Sensor stream paused on this instance.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>High-frequency ingestion demo: many readings per probe, written as batch transactions.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SensorBurst(int readingsPerSensor, CancellationToken cancellationToken)
    {
        var result = await _services.GetRequiredService<SensorStreamRunner>().BurstAsync(readingsPerSensor, cancellationToken);
        var perSecond = result.ElapsedMs <= 0 ? 0 : result.Readings / (result.ElapsedMs / 1000);
        TempData["Success"] = $"Sensor burst: {result.Readings} readings written in {result.ElapsedMs:N0} ms " +
                              $"({perSecond:N0} rows/s) using {result.Batches} Table Storage batch transaction(s).";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>"An automated sensor detects a problem": dry readings for one field, automation reacts now.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DrySoilAlert(string fieldId, CancellationToken cancellationToken)
    {
        var result = await _services.GetRequiredService<SensorStreamRunner>().DrySoilAlertAsync(fieldId, cancellationToken);
        var action = result.Actions.FirstOrDefault(a => a.FieldId == fieldId);
        TempData[action?.Outcome == "Queued" ? "Success" : "Error"] = action is null
            ? $"{fieldId}: probes now report dry soil, but the automation took no action (is the field's controller already irrigating?)."
            : $"{fieldId} dry-soil alert → {action.Outcome}: {action.Detail}";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Peak-hour drone burst: synthetic high-resolution images uploaded in parallel to Blob Storage.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DroneBurst(int count, int width, int height, CancellationToken cancellationToken)
    {
        var result = await _services.GetRequiredService<DroneBurstService>()
            .RunAsync(count, Math.Clamp(width, 640, 6000), Math.Clamp(height, 480, 6000), User.Email(), cancellationToken);
        TempData["Success"] = $"Drone burst: {result.Images} images ({Display.Bytes(result.TotalBytes)}) uploaded in " +
                              $"{result.ElapsedSeconds:N1} s ({result.MegabytesPerSecond:N1} MB/s). Watch the audits appear in Drone & media.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Fill any gap in the sample telemetry (e.g. after the app was stopped for a day).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Backfill(CancellationToken cancellationToken)
    {
        var summary = await _services.GetRequiredService<TelemetrySeeder>().SeedOrBackfillAsync(cancellationToken);
        TempData["Success"] = summary.Readings + summary.WeatherLogs == 0
            ? "Telemetry is already up to date."
            : $"Back-filled {summary.Readings} readings, {summary.WeatherLogs} weather logs and {summary.IrrigationEvents} irrigation events.";
        return RedirectToAction(nameof(Index));
    }
}
