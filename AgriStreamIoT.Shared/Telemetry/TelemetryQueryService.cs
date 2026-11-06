using System.Diagnostics;
using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Entities;
using AgriStreamIoT.Shared.Storage;
using Azure.Data.Tables;

namespace AgriStreamIoT.Shared.Telemetry;

/// <summary>
/// Queries the Table Storage telemetry. Every query targets a single partition (one field or one
/// weather station) and reads rows newest-first, so "latest" questions need only the first page.
/// </summary>
public sealed class TelemetryQueryService : ITelemetryQueryService
{
    private const int MaxSeriesRows = 6000;     // safety cap for a 72 h chart window
    private const int LatestScanRows = 200;     // rows scanned when the window is empty
    private const int FarmScanRowsPerField = 60; // ~40 minutes of readings per field

    private readonly FarmTableStore _store;

    public TelemetryQueryService(FarmTableStore store) => _store = store;

    public async Task<TelemetrySnapshot> GetFieldSnapshotAsync(string fieldId, int hours, CancellationToken cancellationToken = default)
    {
        await _store.EnsureTablesAsync(cancellationToken);
        hours = Math.Clamp(hours, 1, 72);
        var stopwatch = Stopwatch.StartNew();
        var now = DateTimeOffset.UtcNow;

        var readingsTask = QueryAsync<SensorReadingEntity>(
            _store.SensorTelemetry, SinceFilter(fieldId, now.AddHours(-hours)), MaxSeriesRows, cancellationToken);
        var environmentTask = NewestAsync<EnvironmentalLogEntity>(
            _store.EnvironmentalLogs, FarmDefaults.PrimaryWeatherStationId, 1, cancellationToken);
        var irrigationTask = NewestAsync<IrrigationEventEntity>(
            _store.IrrigationEvents, fieldId, 8, cancellationToken);
        await Task.WhenAll(readingsTask, environmentTask, irrigationTask);

        var readings = readingsTask.Result;
        if (readings.Count == 0)
        {
            // Nothing inside the window (e.g. the app was off for days): show the newest rows we have.
            readings = await NewestAsync<SensorReadingEntity>(_store.SensorTelemetry, fieldId, LatestScanRows, cancellationToken);
        }

        var latest = LatestPerDevice(readings, now);
        var bucketMinutes = BucketMinutesFor(hours);

        return new TelemetrySnapshot
        {
            Scope = "field",
            FieldId = fieldId,
            Hours = hours,
            GeneratedAtUtc = now,
            RowsRead = readings.Count + environmentTask.Result.Count + irrigationTask.Result.Count,
            LatestReadings = latest,
            Fields = new List<FieldTelemetrySummary> { TelemetryMapper.Summarise(fieldId, latest) },
            Series = Downsample(readings, bucketMinutes),
            SeriesBucketMinutes = bucketMinutes,
            Environment = environmentTask.Result.FirstOrDefault()?.ToDto(),
            RecentIrrigation = irrigationTask.Result.Select(e => e.ToDto()).ToList(),
            QueryMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1)
        };
    }

    public async Task<TelemetrySnapshot> GetFarmSnapshotAsync(IReadOnlyCollection<string> fieldIds, CancellationToken cancellationToken = default)
    {
        await _store.EnsureTablesAsync(cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        var now = DateTimeOffset.UtcNow;
        var ids = fieldIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // One small query per field partition, all in parallel.
        var readingTasks = ids.ToDictionary(id => id,
            id => NewestAsync<SensorReadingEntity>(_store.SensorTelemetry, id, FarmScanRowsPerField, cancellationToken));
        var irrigationTasks = ids.Select(id => NewestAsync<IrrigationEventEntity>(_store.IrrigationEvents, id, 3, cancellationToken)).ToList();
        var environmentTask = NewestAsync<EnvironmentalLogEntity>(
            _store.EnvironmentalLogs, FarmDefaults.PrimaryWeatherStationId, 1, cancellationToken);

        await Task.WhenAll(readingTasks.Values.Cast<Task>().Concat(irrigationTasks).Append(environmentTask));

        var fields = ids.Select(id => TelemetryMapper.Summarise(id, LatestPerDevice(readingTasks[id].Result, now))).ToList();

        return new TelemetrySnapshot
        {
            Scope = "farm",
            Hours = 1,
            GeneratedAtUtc = now,
            RowsRead = readingTasks.Values.Sum(t => t.Result.Count) + irrigationTasks.Sum(t => t.Result.Count) + environmentTask.Result.Count,
            Fields = fields,
            LatestReadings = fields.SelectMany(f => f.Sensors).ToList(),
            Environment = environmentTask.Result.FirstOrDefault()?.ToDto(),
            RecentIrrigation = irrigationTasks.SelectMany(t => t.Result)
                .OrderByDescending(e => e.ProcessedAtUtc).Take(8).Select(e => e.ToDto()).ToList(),
            QueryMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1)
        };
    }

    public async Task<IReadOnlyList<SensorReadingEntity>> GetRecentReadingsAsync(
        IReadOnlyCollection<string> fieldIds, string? deviceId, int take, CancellationToken cancellationToken = default)
    {
        await _store.EnsureTablesAsync(cancellationToken);
        take = Math.Clamp(take, 1, 1000);
        var tasks = fieldIds.Distinct(StringComparer.OrdinalIgnoreCase).Select(fieldId =>
        {
            var filter = string.IsNullOrWhiteSpace(deviceId)
                ? TableClient.CreateQueryFilter($"PartitionKey eq {fieldId}")
                : TableClient.CreateQueryFilter($"PartitionKey eq {fieldId} and DeviceId eq {deviceId}");
            return QueryAsync<SensorReadingEntity>(_store.SensorTelemetry, filter, take, cancellationToken);
        }).ToList();
        await Task.WhenAll(tasks);
        return tasks.SelectMany(t => t.Result).OrderByDescending(r => r.ReadingUtc).Take(take).ToList();
    }

    public async Task<IReadOnlyList<EnvironmentalLogEntity>> GetEnvironmentalLogsAsync(
        string stationId, int hours, int take, CancellationToken cancellationToken = default)
    {
        await _store.EnsureTablesAsync(cancellationToken);
        var since = DateTimeOffset.UtcNow.AddHours(-Math.Clamp(hours, 1, 168));
        return await QueryAsync<EnvironmentalLogEntity>(
            _store.EnvironmentalLogs, SinceFilter(stationId, since), Math.Clamp(take, 1, 2000), cancellationToken);
    }

    public async Task<IReadOnlyList<IrrigationEventEntity>> GetIrrigationEventsAsync(
        IReadOnlyCollection<string> fieldIds, int takePerField, CancellationToken cancellationToken = default)
    {
        await _store.EnsureTablesAsync(cancellationToken);
        var tasks = fieldIds.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => NewestAsync<IrrigationEventEntity>(_store.IrrigationEvents, id, Math.Clamp(takePerField, 1, 500), cancellationToken))
            .ToList();
        await Task.WhenAll(tasks);
        return tasks.SelectMany(t => t.Result).OrderByDescending(e => e.ProcessedAtUtc).ToList();
    }

    public async Task<IReadOnlyList<SensorReadingEntity>> GetLatestPerSensorAsync(string fieldId, CancellationToken cancellationToken = default)
    {
        await _store.EnsureTablesAsync(cancellationToken);
        var rows = await NewestAsync<SensorReadingEntity>(_store.SensorTelemetry, fieldId, LatestScanRows, cancellationToken);
        return rows.GroupBy(r => r.DeviceId).Select(g => g.First()).ToList();
    }

    public async Task<EnvironmentalLogEntity?> GetNewestEnvironmentalLogAsync(string stationId, CancellationToken cancellationToken = default)
    {
        await _store.EnsureTablesAsync(cancellationToken);
        var rows = await NewestAsync<EnvironmentalLogEntity>(_store.EnvironmentalLogs, stationId, 1, cancellationToken);
        return rows.FirstOrDefault();
    }

    // ── helpers ──────────────────────────────────────────────────────

    private static string SinceFilter(string partitionKey, DateTimeOffset since)
    {
        var upperBound = TableKeys.RowKeyUpperBoundSince(since);
        return TableClient.CreateQueryFilter($"PartitionKey eq {partitionKey} and RowKey le {upperBound}");
    }

    private static Task<List<T>> NewestAsync<T>(TableClient table, string partitionKey, int take, CancellationToken ct)
        where T : class, ITableEntity, new() =>
        QueryAsync<T>(table, TableClient.CreateQueryFilter($"PartitionKey eq {partitionKey}"), take, ct);

    private static async Task<List<T>> QueryAsync<T>(TableClient table, string filter, int maxRows, CancellationToken ct)
        where T : class, ITableEntity, new()
    {
        var results = new List<T>();
        var pageSize = Math.Min(Math.Max(maxRows, 1), 1000);
        await foreach (var entity in table.QueryAsync<T>(filter: filter, maxPerPage: pageSize, cancellationToken: ct))
        {
            results.Add(entity);
            if (results.Count >= maxRows) break;
        }
        return results;
    }

    /// <summary>Rows arrive newest-first, so the first row seen for each device is its latest.</summary>
    private static List<DeviceReadingDto> LatestPerDevice(IEnumerable<SensorReadingEntity> readings, DateTimeOffset now) =>
        readings.GroupBy(r => r.DeviceId)
            .Select(g => g.First().ToDto(now))
            .OrderBy(r => r.DeviceId, StringComparer.Ordinal)
            .ToList();

    /// <summary>About 96 points per sensor whatever the window (24 h = 15-minute buckets).</summary>
    public static int BucketMinutesFor(int hours) => Math.Max(2, (int)Math.Round(hours * 60 / 96.0));

    private static List<TelemetryPoint> Downsample(IEnumerable<SensorReadingEntity> readings, int bucketMinutes)
    {
        var bucketTicks = TimeSpan.FromMinutes(bucketMinutes).Ticks;
        return readings
            .GroupBy(r => (r.DeviceId, Bucket: r.ReadingUtc.UtcTicks / bucketTicks))
            .Select(g => new TelemetryPoint
            {
                DeviceId = g.Key.DeviceId,
                // Bucket start: every sensor shares the same x values, so the chart can compare them at one time.
                TimeUtc = new DateTimeOffset(g.Key.Bucket * bucketTicks, TimeSpan.Zero),
                SoilMoisturePct = Math.Round(g.Average(x => x.SoilMoisturePct), 2),
                SoilTemperatureC = Math.Round(g.Average(x => x.SoilTemperatureC), 2)
            })
            .OrderBy(p => p.DeviceId, StringComparer.Ordinal)
            .ThenBy(p => p.TimeUtc)
            .ToList();
    }
}
