using AgriStreamIoT.Shared.Telemetry;
using AgriStreamIoT.Web.Helpers;
using AgriStreamIoT.Web.Interfaces;
using AgriStreamIoT.Web.Services.Analytics;
using AgriStreamIoT.Web.Services.Health;
using AgriStreamIoT.Web.Services.Seeding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace AgriStreamIoT.Web.Controllers.Api;

/// <summary>
/// Small read-only JSON API. These endpoints are anonymous so Azure Load Testing (URL-based test)
/// and the console load tester can hit them without signing in:
///   GET /api/analytics/outlook?fieldId=F01&amp;iterations=50000   compute-heavy (drives CPU autoscale)
///   GET /api/farm/summary                                       farm board (cached 5 s)
///   GET /api/instance                                           which instance answered
/// </summary>
[ApiController]
[Route("api")]
[AllowAnonymous]
public sealed class FarmApiController : ControllerBase
{
    private readonly IServiceProvider _services;
    private readonly IMemoryCache _cache;

    public FarmApiController(IServiceProvider services, IMemoryCache cache)
    {
        _services = services;
        _cache = cache;
    }

    [HttpGet("analytics/outlook")]
    public async Task<IActionResult> Outlook(string fieldId = "F01", int iterations = 20_000, CancellationToken cancellationToken = default)
    {
        fieldId = fieldId.Trim().ToUpperInvariant();
        var (moisture, threshold) = await CurrentConditionsAsync(fieldId, cancellationToken);
        var result = _services.GetRequiredService<MoistureOutlookService>()
            .Run(fieldId, moisture, threshold, Math.Clamp(iterations, 100, 200_000));
        return Ok(result);
    }

    [HttpGet("farm/summary")]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        var fields = await _services.GetRequiredService<IFieldRepository>().GetAllAsync(cancellationToken);
        var telemetry = await _services.GetRequiredService<IFieldTelemetryService>()
            .GetFarmAsync(fields.Select(f => f.FieldId).ToList(), cancellationToken);

        return Ok(new
        {
            generatedAtUtc = telemetry.Snapshot.GeneratedAtUtc,
            source = telemetry.SourceLabel,
            servedBy = InstanceInfo.ShortId,
            fields = fields.Select(f =>
            {
                var t = telemetry.Snapshot.Fields.FirstOrDefault(x => x.FieldId == f.FieldId);
                var state = Display.MoistureState(t?.AvgSoilMoisturePct, f.MoistureThresholdPct);
                return new
                {
                    f.FieldId, f.Name, f.Crop, thresholdPct = f.MoistureThresholdPct,
                    moisturePct = t?.AvgSoilMoisturePct, sensors = t?.SensorsReporting ?? 0,
                    state = state.Label, latestUtc = t?.LatestReadingUtc
                };
            }),
            environment = telemetry.Snapshot.Environment
        });
    }

    [HttpGet("instance")]
    public IActionResult Instance() => Ok(InstanceInfo.Snapshot());

    /// <summary>Current field moisture and threshold, cached for 30 s so the outlook stays CPU-bound under load.</summary>
    private async Task<(double Moisture, double Threshold)> CurrentConditionsAsync(string fieldId, CancellationToken ct)
    {
        return await _cache.GetOrCreateAsync($"conditions:{fieldId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            var profile = FarmSeedData.ProfileFor(fieldId);
            double threshold = 25, moisture = profile.StartMoisturePct;
            try
            {
                var field = await _services.GetRequiredService<IFieldRepository>().GetAsync(fieldId, ct);
                if (field is not null) threshold = (double)field.MoistureThresholdPct;
                var latest = await _services.GetRequiredService<ITelemetryQueryService>()
                    .GetLatestPerSensorAsync(fieldId, ct);
                if (latest.Count > 0) moisture = latest.Average(r => r.SoilMoisturePct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Works even before Azure is configured: falls back to the sample profile.
            }
            return (moisture, threshold);
        });
    }
}
