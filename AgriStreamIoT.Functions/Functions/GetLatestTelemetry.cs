using AgriStreamIoT.Functions.Services;
using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AgriStreamIoT.Functions.Functions;

/// <summary>
/// FUNCTION 2 of 4 – HTTP TRIGGER (GET).
/// Called by the MVC app when a farmer opens a field dashboard. Returns the latest reading of every
/// sensor, the chart history, current weather and recent irrigation events from Table Storage in a
/// single response for near-instant rendering.
///   GET /api/telemetry/latest?fieldId=F01&amp;hours=24     one field in detail
///   GET /api/telemetry/latest?fieldIds=F01,F02,F03       farm summary
/// </summary>
public sealed class GetLatestTelemetry
{
    private readonly ITelemetryQueryService _telemetry;
    private readonly TelemetrySnapshotCache _cache;
    private readonly ILogger<GetLatestTelemetry> _logger;

    public GetLatestTelemetry(ITelemetryQueryService telemetry, TelemetrySnapshotCache cache, ILogger<GetLatestTelemetry> logger)
    {
        _telemetry = telemetry;
        _cache = cache;
        _logger = logger;
    }

    [Function(nameof(GetLatestTelemetry))]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "telemetry/latest")] HttpRequest request,
        FunctionContext context)
    {
        string? fieldId = request.Query["fieldId"];
        string? fieldIdsCsv = request.Query["fieldIds"];
        var hours = int.TryParse(request.Query["hours"], out var h) ? Math.Clamp(h, 1, 72) : FarmDefaults.DashboardHours;
        var bypassCache = string.Equals(request.Query["cache"], "false", StringComparison.OrdinalIgnoreCase);

        request.HttpContext.Response.Headers["X-Served-By"] = FunctionSettings.InstanceId;

        if (string.IsNullOrWhiteSpace(fieldId) && string.IsNullOrWhiteSpace(fieldIdsCsv))
        {
            return new BadRequestObjectResult(new
            {
                error = "Pass ?fieldId=F01 for one field, or ?fieldIds=F01,F02 for a farm summary."
            });
        }

        TelemetrySnapshot snapshot;
        if (!string.IsNullOrWhiteSpace(fieldId))
        {
            var id = fieldId.Trim().ToUpperInvariant();
            Task<TelemetrySnapshot> Load() => _telemetry.GetFieldSnapshotAsync(id, hours, context.CancellationToken);
            snapshot = bypassCache ? await Load() : await _cache.GetOrAddAsync($"field:{id}:{hours}", Load);
        }
        else
        {
            var ids = fieldIdsCsv!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(id => id.ToUpperInvariant()).Distinct().Take(50).ToList();
            Task<TelemetrySnapshot> Load() => _telemetry.GetFarmSnapshotAsync(ids, context.CancellationToken);
            snapshot = bypassCache ? await Load() : await _cache.GetOrAddAsync($"farm:{string.Join(',', ids)}", Load);
        }

        snapshot.Source = $"Azure Function {nameof(GetLatestTelemetry)}";
        snapshot.ServedBy = FunctionSettings.InstanceId;

        _logger.LogInformation(
            "Telemetry served for {Scope} {Target}: {Sensors} sensors, {Rows} rows, {QueryMs} ms, cached={Cached}.",
            snapshot.Scope, snapshot.FieldId ?? fieldIdsCsv, snapshot.LatestReadings.Count, snapshot.RowsRead,
            snapshot.QueryMs, snapshot.Cached);

        return new OkObjectResult(snapshot);
    }
}
