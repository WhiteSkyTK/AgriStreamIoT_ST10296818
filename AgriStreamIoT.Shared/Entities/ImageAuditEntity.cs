using AgriStreamIoT.Shared.Storage;
using Azure;
using Azure.Data.Tables;

namespace AgriStreamIoT.Shared.Entities;

/// <summary>
/// Processing-status audit entry written by the ProcessDroneImageUpload blob trigger.
/// PartitionKey = container name, RowKey = blob name (made key-safe), so a re-run updates the same row.
/// </summary>
public sealed class ImageAuditEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string BlobName { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string? FieldId { get; set; }
    public string? UploadedBy { get; set; }
    public string? OriginalFileName { get; set; }
    public string? ContentType { get; set; }
    public long SizeBytes { get; set; }
    public string? Format { get; set; }
    public int WidthPx { get; set; }
    public int HeightPx { get; set; }
    public double Megapixels { get; set; }

    /// <summary>Processing, Processed, Rejected or Failed.</summary>
    public string Status { get; set; } = ImageAuditStatuses.Processing;
    public string? StatusDetail { get; set; }
    public DateTimeOffset? UploadedAtUtc { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public double ProcessingMs { get; set; }

    /// <summary>Seconds from the blob landing in storage to the audit being completed.</summary>
    public double PipelineLatencySeconds { get; set; }
    public string FunctionName { get; set; } = string.Empty;
    public string? InvocationId { get; set; }
    public string? ProcessedOn { get; set; }

    public static (string PartitionKey, string RowKey) KeysFor(string containerName, string blobName) =>
        (containerName, TableKeys.SafeKey(blobName));
}

/// <summary>Status values for <see cref="ImageAuditEntity.Status"/>.</summary>
public static class ImageAuditStatuses
{
    public const string Processing = "Processing";
    public const string Processed = "Processed";
    public const string Rejected = "Rejected";
    public const string Failed = "Failed";
}
