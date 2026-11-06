using AgriStreamIoT.Shared.Constants;
using Azure.Data.Tables;

namespace AgriStreamIoT.Shared.Storage;

/// <summary>
/// Owns the Azure Table Storage tables used for high-frequency, non-relational farm data.
/// Register it as a singleton: TableClient is thread-safe and reuses HTTP connections.
/// </summary>
public sealed class FarmTableStore
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile bool _tablesReady;

    public FarmTableStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new FarmConfigurationException("Azure Storage", "ConnectionStrings:FarmStorage");

        TableServiceClient service;
        try
        {
            service = new TableServiceClient(connectionString);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
        {
            throw new FarmConfigurationException("Azure Storage", "ConnectionStrings:FarmStorage",
                "the value is not a valid storage account connection string.");
        }

        SensorTelemetry = service.GetTableClient(StorageNames.SensorTelemetryTable);
        IrrigationEvents = service.GetTableClient(StorageNames.IrrigationEventsTable);
        EnvironmentalLogs = service.GetTableClient(StorageNames.EnvironmentalLogsTable);
        ImageAudit = service.GetTableClient(StorageNames.ImageAuditTable);
        AccountName = service.AccountName;
    }

    /// <summary>Soil sensor readings. PartitionKey = FieldId, RowKey = newest-first ticks + DeviceId.</summary>
    public TableClient SensorTelemetry { get; }

    /// <summary>Irrigation commands as processed. PartitionKey = FieldId.</summary>
    public TableClient IrrigationEvents { get; }

    /// <summary>Weather station logs. PartitionKey = StationId.</summary>
    public TableClient EnvironmentalLogs { get; }

    /// <summary>Drone image processing audit trail. PartitionKey = container, RowKey = blob name.</summary>
    public TableClient ImageAudit { get; }

    public string AccountName { get; }

    /// <summary>Creates the tables once per process. Safe to call on every request.</summary>
    public async Task EnsureTablesAsync(CancellationToken cancellationToken = default)
    {
        if (_tablesReady) return;
        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_tablesReady) return;
            await SensorTelemetry.CreateIfNotExistsAsync(cancellationToken);
            await IrrigationEvents.CreateIfNotExistsAsync(cancellationToken);
            await EnvironmentalLogs.CreateIfNotExistsAsync(cancellationToken);
            await ImageAudit.CreateIfNotExistsAsync(cancellationToken);
            _tablesReady = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
