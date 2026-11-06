using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Telemetry;
using Microsoft.Extensions.Caching.Memory;

namespace AgriStreamIoT.Functions.Services;

/// <summary>
/// Cache-aside for telemetry snapshots. During peak daylight hours many farmers refresh the same
/// field dashboard; serving a snapshot for a few seconds keeps the Table partition well inside its
/// throughput target while the data stays effectively live (sensors report every 1–2 minutes).
/// </summary>
public sealed class TelemetrySnapshotCache
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl;

    public TelemetrySnapshotCache(IMemoryCache cache)
    {
        _cache = cache;
        _ttl = TimeSpan.FromSeconds(Math.Clamp(
            FunctionSettings.Int("TelemetryCacheSeconds", FarmDefaults.TelemetryCacheSeconds), 0, 300));
    }

    public async Task<TelemetrySnapshot> GetOrAddAsync(string key, Func<Task<TelemetrySnapshot>> factory)
    {
        if (_ttl == TimeSpan.Zero) return await factory();

        if (_cache.TryGetValue(key, out CachedSnapshot? hit) && hit is not null)
            return hit.Snapshot.CopyForCache((DateTimeOffset.UtcNow - hit.CreatedUtc).TotalSeconds);

        var fresh = await factory();
        _cache.Set(key, new CachedSnapshot(fresh, DateTimeOffset.UtcNow), _ttl);
        return fresh;
    }

    private sealed record CachedSnapshot(TelemetrySnapshot Snapshot, DateTimeOffset CreatedUtc);
}
