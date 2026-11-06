using AgriStreamIoT.Shared.Entities;
using AgriStreamIoT.Shared.Storage;
using Azure.Data.Tables;

namespace AgriStreamIoT.Shared.Telemetry;

/// <summary>
/// Write side of the Table Storage telemetry. Readings are written as entity-group transactions
/// (up to 100 rows of the same partition per request) which is how high-frequency data is
/// ingested cheaply.
/// </summary>
public sealed class TelemetryWriter
{
    private const int MaxBatch = 100;
    private readonly FarmTableStore _store;

    public TelemetryWriter(FarmTableStore store) => _store = store;

    public async Task<int> WriteReadingsAsync(IEnumerable<SensorReadingEntity> readings, CancellationToken cancellationToken = default)
    {
        await _store.EnsureTablesAsync(cancellationToken);
        return await WriteBatchesAsync(_store.SensorTelemetry, readings, cancellationToken);
    }

    public async Task<int> WriteEnvironmentalLogsAsync(IEnumerable<EnvironmentalLogEntity> logs, CancellationToken cancellationToken = default)
    {
        await _store.EnsureTablesAsync(cancellationToken);
        return await WriteBatchesAsync(_store.EnvironmentalLogs, logs, cancellationToken);
    }

    public async Task<int> WriteIrrigationEventsAsync(IEnumerable<IrrigationEventEntity> events, CancellationToken cancellationToken = default)
    {
        await _store.EnsureTablesAsync(cancellationToken);
        return await WriteBatchesAsync(_store.IrrigationEvents, events, cancellationToken);
    }

    public async Task RecordIrrigationEventAsync(IrrigationEventEntity irrigationEvent, CancellationToken cancellationToken = default)
    {
        await _store.EnsureTablesAsync(cancellationToken);
        await _store.IrrigationEvents.UpsertEntityAsync(irrigationEvent, TableUpdateMode.Replace, cancellationToken);
    }

    private static async Task<int> WriteBatchesAsync<T>(TableClient table, IEnumerable<T> entities, CancellationToken ct)
        where T : class, ITableEntity
    {
        var written = 0;
        foreach (var partition in entities.GroupBy(e => e.PartitionKey))
        {
            foreach (var chunk in partition.Chunk(MaxBatch))
            {
                var actions = chunk.Select(e => new TableTransactionAction(TableTransactionActionType.UpsertReplace, e));
                await table.SubmitTransactionAsync(actions, ct);
                written += chunk.Length;
            }
        }
        return written;
    }
}
