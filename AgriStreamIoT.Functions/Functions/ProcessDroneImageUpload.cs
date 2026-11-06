using System.Diagnostics;
using AgriStreamIoT.Functions.Services;
using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Entities;
using AgriStreamIoT.Shared.Imaging;
using AgriStreamIoT.Shared.Telemetry;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AgriStreamIoT.Functions.Functions;

/// <summary>
/// FUNCTION 1 of 4 – BLOB TRIGGER.
/// Fires when a drone image lands in the "drone-images" container (bursts of uploads during
/// daylight flights). It logs the upload details and writes a processing-status audit entry to
/// the ImageProcessingAudit table: first "Processing", then "Processed" (valid image) or "Rejected".
/// Only the first 512 KB of the blob is downloaded to validate the format and read the resolution.
/// </summary>
public sealed class ProcessDroneImageUpload
{
    private readonly ImageAuditStore _audits;
    private readonly ILogger<ProcessDroneImageUpload> _logger;

    public ProcessDroneImageUpload(ImageAuditStore audits, ILogger<ProcessDroneImageUpload> logger)
    {
        _audits = audits;
        _logger = logger;
    }

    [Function(nameof(ProcessDroneImageUpload))]
    public async Task Run(
        [BlobTrigger(StorageNames.DroneImagesContainer + "/{name}", Connection = StorageNames.FunctionsStorageSetting)]
        BlobClient blob,
        string name,
        FunctionContext context)
    {
        var cancellationToken = context.CancellationToken;
        var stopwatch = Stopwatch.StartNew();

        BlobProperties properties;
        try
        {
            properties = (await blob.GetPropertiesAsync(cancellationToken: cancellationToken)).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Drone image {BlobName} was deleted before it could be processed.", name);
            return;
        }

        var metadata = new Dictionary<string, string>(properties.Metadata, StringComparer.OrdinalIgnoreCase);
        var (partitionKey, rowKey) = ImageAuditEntity.KeysFor(StorageNames.DroneImagesContainer, name);
        var audit = new ImageAuditEntity
        {
            PartitionKey = partitionKey,
            RowKey = rowKey,
            BlobName = name,
            ContainerName = StorageNames.DroneImagesContainer,
            FieldId = Read(metadata, "fieldid"),
            UploadedBy = Read(metadata, "uploadedby"),
            OriginalFileName = Uri.UnescapeDataString(Read(metadata, "originalname") ?? Path.GetFileName(name)),
            ContentType = properties.ContentType,
            SizeBytes = properties.ContentLength,
            UploadedAtUtc = properties.CreatedOn,
            Status = ImageAuditStatuses.Processing,
            StatusDetail = "Blob trigger fired; validating image header.",
            FunctionName = nameof(ProcessDroneImageUpload),
            InvocationId = context.InvocationId,
            ProcessedOn = FunctionSettings.InstanceId
        };

        // Step 1: record that the upload was received.
        await _audits.UpsertAsync(audit, cancellationToken);

        _logger.LogInformation(
            "Drone image received: {BlobName} ({SizeMb:F2} MB, {ContentType}) for field {FieldId}, uploaded by {UploadedBy}.",
            name, properties.ContentLength / 1_048_576.0, properties.ContentType, audit.FieldId ?? "n/a", audit.UploadedBy ?? "n/a");

        try
        {
            // Step 2: validate the image from its header bytes.
            var info = properties.ContentLength == 0
                ? ImageInfo.Unknown
                : ImageInspector.Inspect(await DownloadHeaderAsync(blob, properties.ContentLength, cancellationToken));

            audit.Format = info.Format;
            audit.WidthPx = info.Width;
            audit.HeightPx = info.Height;
            audit.Megapixels = info.Megapixels;

            if (info.IsImage)
            {
                audit.Status = ImageAuditStatuses.Processed;
                audit.StatusDetail = info.Width > 0
                    ? $"{info.Format} {info.Width}×{info.Height} ({info.Megapixels:F1} MP) validated and catalogued."
                    : $"{info.Format} image validated and catalogued.";
            }
            else
            {
                audit.Status = ImageAuditStatuses.Rejected;
                audit.StatusDetail = properties.ContentLength == 0
                    ? "Empty file: nothing to process."
                    : "Not a supported image (JPEG, PNG, TIFF, WebP, GIF or BMP).";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            audit.Status = ImageAuditStatuses.Failed;
            audit.StatusDetail = $"Processing failed: {ex.Message}";
            _logger.LogError(ex, "Processing drone image {BlobName} failed.", name);
            throw;   // let the runtime retry (up to 5 times, then the blob goes to the poison queue)
        }
        finally
        {
            audit.ProcessedAtUtc = DateTimeOffset.UtcNow;
            audit.ProcessingMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1);
            if (properties.CreatedOn != default)
                audit.PipelineLatencySeconds = Math.Round((audit.ProcessedAtUtc.Value - properties.CreatedOn).TotalSeconds, 1);

            // Step 3: final processing status.
            await _audits.UpsertAsync(audit, CancellationToken.None);
        }

        _logger.LogInformation("Drone image {BlobName}: {Status} - {Detail} ({ElapsedMs} ms).",
            name, audit.Status, audit.StatusDetail, audit.ProcessingMs);
    }

    private static async Task<byte[]> DownloadHeaderAsync(BlobClient blob, long size, CancellationToken cancellationToken)
    {
        var length = Math.Min(size, ImageInspector.HeaderBytes);
        var response = await blob.DownloadStreamingAsync(
            new BlobDownloadOptions { Range = new HttpRange(0, length) }, cancellationToken);
        await using var content = response.Value.Content;
        using var buffer = new MemoryStream((int)length);
        await content.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static string? Read(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
