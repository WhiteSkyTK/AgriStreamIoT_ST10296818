namespace AgriStreamIoT.Shared.Constants;

/// <summary>
/// Every Azure Storage resource name used by the solution, in one place so the web app
/// and the Function App can never drift apart.
/// </summary>
public static class StorageNames
{
    // ── Blob containers ─────────────────────────────────────────────
    /// <summary>High-resolution drone imagery. Watched by the ProcessDroneImageUpload blob trigger.</summary>
    public const string DroneImagesContainer = "drone-images";

    /// <summary>Other farm media uploaded through the web app (field photos, soil reports, videos).</summary>
    public const string FarmMediaContainer = "farm-media";

    // ── Tables (high-frequency, non-relational data) ────────────────
    public const string SensorTelemetryTable = "SensorTelemetry";
    public const string IrrigationEventsTable = "IrrigationEvents";
    public const string EnvironmentalLogsTable = "EnvironmentalLogs";
    public const string ImageAuditTable = "ImageProcessingAudit";

    // ── Queues ──────────────────────────────────────────────────────
    public const string IrrigationCommandsQueue = "irrigation-commands";

    /// <summary>Created automatically by the Functions runtime after 5 failed attempts.</summary>
    public const string IrrigationCommandsPoisonQueue = "irrigation-commands-poison";

    // ── Function App setting names ──────────────────────────────────
    /// <summary>App setting that holds the farm storage account connection string (Blob, Table, Queue).</summary>
    public const string FunctionsStorageSetting = "FarmStorage";

    /// <summary>App setting that holds the Azure SQL connection string.</summary>
    public const string FunctionsSqlSetting = "FarmSql";
}
