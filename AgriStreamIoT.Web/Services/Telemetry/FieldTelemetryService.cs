using System.Diagnostics;
using AgriStreamIoT.Shared.Telemetry;
using AgriStreamIoT.Web.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace AgriStreamIoT.Web.Services.Telemetry;

/// <summary>
/// Dashboard telemetry. Primary path: the GetLatestTelemetry Azure Function (as the brief requires).
/// Fallback: the same query run directly against Table Storage, so a stopped Function App never
/// leaves farmers without data. The page always shows which path was used.
/// </summary>
public sealed class FieldTelemetryService : IFieldTelemetryService
{
    private static readonly TimeSpan FarmCache = TimeSpan.FromSeconds(5);

    private readonly IFarmFunctionsClient _functions;
    private readonly IServiceProvider _services;
    private readonly IMemoryCache _cache;
    private readonly ILogger<FieldTelemetryService> _logger;

    public FieldTelemetryService(IFarmFunctionsClient functions, IServiceProvider services, IMemoryCache cache,
        ILogger<FieldTelemetryService> logger)
    {
        _functions = functions;
        _services = services;
        _cache = cache;
        _logger = logger;
    }

    public async Task<TelemetryResult> GetFieldAsync(string fieldId, int hours, CancellationToken cancellationToken = default)
    {
        var call = await _functions.GetFieldTelemetryAsync(fieldId, hours, cancellationToken);
        if (call.Success && call.Value is not null)
            return new TelemetryResult(call.Value, true, call.ElapsedMs, null, call.ServedBy);

        var stopwatch = Stopwatch.StartNew();
        var snapshot = await Direct().GetFieldSnapshotAsync(fieldId, hours, cancellationToken);
        snapshot.Source = "Direct Table Storage query";
        _logger.LogInformation("Telemetry for {FieldId} served directly from Table Storage: {Error}", fieldId, call.Error);
        return new TelemetryResult(snapshot, false, stopwatch.Elapsed.TotalMilliseconds, call.Error, null);
    }

    public async Task<TelemetryResult> GetFarmAsync(IReadOnlyCollection<string> fieldIds, CancellationToken cancellationToken = default)
    {
        var key = "farm-telemetry:" + string.Join(',', fieldIds);
        if (_cache.TryGetValue(key, out TelemetryResult? cached) && cached is not null)
            return cached;

        TelemetryResult result;
        var call = await _functions.GetFarmTelemetryAsync(fieldIds, cancellationToken);
        if (call.Success && call.Value is not null)
        {
            result = new TelemetryResult(call.Value, true, call.ElapsedMs, null, call.ServedBy);
        }
        else
        {
            var stopwatch = Stopwatch.StartNew();
            var snapshot = await Direct().GetFarmSnapshotAsync(fieldIds, cancellationToken);
            snapshot.Source = "Direct Table Storage query";
            result = new TelemetryResult(snapshot, false, stopwatch.Elapsed.TotalMilliseconds, call.Error, null);
        }

        _cache.Set(key, result, FarmCache);
        return result;
    }

    private ITelemetryQueryService Direct() => _services.GetRequiredService<ITelemetryQueryService>();
}
