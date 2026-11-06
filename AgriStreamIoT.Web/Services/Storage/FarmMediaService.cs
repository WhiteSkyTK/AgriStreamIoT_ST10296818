using System.Text;
using AgriStreamIoT.Shared;
using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Web.Interfaces;
using AgriStreamIoT.Web.Models;
using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace AgriStreamIoT.Web.Services.Storage;

/// <summary>
/// Uploads and lists drone imagery (drone-images container, watched by the blob trigger) and other
/// farm media (farm-media container). Large files are uploaded in parallel 4 MB blocks.
/// </summary>
public sealed class FarmMediaService : IFarmMediaService
{
    private readonly BlobServiceClient _service;
    private readonly BlobContainerClient _drone;
    private readonly BlobContainerClient _media;
    private volatile bool _containersReady;

    public FarmMediaService(string connectionString)
    {
        try
        {
            _service = new BlobServiceClient(connectionString);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
        {
            throw new FarmConfigurationException("Azure Storage", "ConnectionStrings:FarmStorage",
                "the value is not a valid storage account connection string.");
        }
        _drone = _service.GetBlobContainerClient(StorageNames.DroneImagesContainer);
        _media = _service.GetBlobContainerClient(StorageNames.FarmMediaContainer);
    }

    public string AccountName => _service.AccountName;

    public async Task EnsureContainersAsync(CancellationToken cancellationToken = default)
    {
        if (_containersReady) return;
        // Private containers: images are shown through short-lived SAS links, never public access.
        await _drone.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        await _media.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        _containersReady = true;
    }

    public async Task<UploadedMedia> UploadAsync(Stream content, string originalFileName, string? contentType,
        MediaCategory category, string fieldId, string uploadedBy, CancellationToken cancellationToken = default)
    {
        await EnsureContainersAsync(cancellationToken);
        var container = category == MediaCategory.DroneImagery ? _drone : _media;
        var fileName = Path.GetFileName(originalFileName);
        var blobName = BuildBlobName(fieldId, fileName);
        var blob = container.GetBlobClient(blobName);

        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = string.IsNullOrWhiteSpace(contentType) ? GuessContentType(fileName) : contentType
            },
            // Read by the blob-triggered function for its audit entry. Metadata must be ASCII.
            Metadata = new Dictionary<string, string>
            {
                ["fieldid"] = AsciiOnly(fieldId),
                ["uploadedby"] = AsciiOnly(uploadedBy),
                ["originalname"] = Uri.EscapeDataString(fileName),
                ["category"] = category.ToString()
            },
            TransferOptions = new StorageTransferOptions
            {
                InitialTransferSize = 8 * 1024 * 1024,
                MaximumTransferSize = 4 * 1024 * 1024,
                MaximumConcurrency = 4
            }
        };

        long size = 0;
        if (content.CanSeek) size = content.Length;
        await blob.UploadAsync(content, options, cancellationToken);
        if (size == 0)
            size = (await blob.GetPropertiesAsync(cancellationToken: cancellationToken)).Value.ContentLength;

        return new UploadedMedia(container.Name, blobName, fileName, size);
    }

    public async Task<IReadOnlyList<MediaItem>> ListAsync(MediaCategory? category, string? fieldId, int take, CancellationToken cancellationToken = default)
    {
        await EnsureContainersAsync(cancellationToken);
        var containers = category switch
        {
            MediaCategory.DroneImagery => new[] { _drone },
            MediaCategory.FarmMedia => new[] { _media },
            _ => new[] { _drone, _media }
        };
        var prefix = string.IsNullOrWhiteSpace(fieldId) ? null : fieldId.Trim().ToUpperInvariant() + "/";

        var items = new List<MediaItem>();
        foreach (var container in containers)
        {
            await foreach (var blob in container.GetBlobsAsync(
                traits: BlobTraits.Metadata,
                states: BlobStates.None,
                prefix: prefix,
                cancellationToken: cancellationToken))
            {
                var meta = new Dictionary<string, string>(blob.Metadata ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                items.Add(new MediaItem
                {
                    Container = container.Name,
                    BlobName = blob.Name,
                    FileName = meta.TryGetValue("originalname", out var original) ? Uri.UnescapeDataString(original) : Path.GetFileName(blob.Name),
                    FieldId = meta.TryGetValue("fieldid", out var field) ? field : blob.Name.Split('/')[0],
                    UploadedBy = meta.TryGetValue("uploadedby", out var by) ? by : null,
                    ContentType = blob.Properties.ContentType,
                    SizeBytes = blob.Properties.ContentLength ?? 0,
                    UploadedAtUtc = blob.Properties.CreatedOn ?? blob.Properties.LastModified,
                    ViewUrl = ReadUrl(container, blob.Name)
                });
                if (items.Count >= 5000) break;
            }
        }

        return items.OrderByDescending(i => i.UploadedAtUtc).Take(Math.Clamp(take, 1, 1000)).ToList();
    }

    public async Task<(Stream Content, string ContentType)?> OpenReadAsync(string container, string blobName, CancellationToken cancellationToken = default)
    {
        var client = ContainerFor(container)?.GetBlobClient(blobName);
        if (client is null) return null;
        try
        {
            var response = await client.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return (response.Value.Content, response.Value.Details.ContentType ?? "application/octet-stream");
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string container, string blobName, CancellationToken cancellationToken = default)
    {
        var client = ContainerFor(container)?.GetBlobClient(blobName);
        if (client is null) return false;
        var response = await client.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
        return response.Value;
    }

    public async Task<(int Count, long Bytes)> GetUsageAsync(string container, CancellationToken cancellationToken = default)
    {
        await EnsureContainersAsync(cancellationToken);
        var client = ContainerFor(container);
        if (client is null) return (0, 0);
        int count = 0;
        long bytes = 0;
        await foreach (var blob in client.GetBlobsAsync(cancellationToken: cancellationToken))
        {
            count++;
            bytes += blob.Properties.ContentLength ?? 0;
        }
        return (count, bytes);
    }

    // ── helpers ──────────────────────────────────────────────────────

    private BlobContainerClient? ContainerFor(string name) => name switch
    {
        StorageNames.DroneImagesContainer => _drone,
        StorageNames.FarmMediaContainer => _media,
        _ => null
    };

    /// <summary>A one-hour read-only SAS link, or the web app proxy when the key is not available.</summary>
    private static string ReadUrl(BlobContainerClient container, string blobName)
    {
        var blob = container.GetBlobClient(blobName);
        if (blob.CanGenerateSasUri)
            return blob.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddHours(1)).ToString();
        return $"/Media/Raw?container={Uri.EscapeDataString(container.Name)}&name={Uri.EscapeDataString(blobName)}";
    }

    /// <summary>"F01/20261106-101530-3f9a2c-north-pivot.jpg": field folder, time, unique id, clean name.</summary>
    public static string BuildBlobName(string fieldId, string fileName)
    {
        var field = string.IsNullOrWhiteSpace(fieldId) ? "FARM" : AsciiOnly(fieldId).ToUpperInvariant();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var clean = new StringBuilder();
        foreach (var ch in stem.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') clean.Append(ch);
            else if (clean.Length > 0 && clean[^1] != '-') clean.Append('-');
        }
        var safeStem = clean.ToString().Trim('-');
        if (safeStem.Length == 0) safeStem = "image";
        if (safeStem.Length > 60) safeStem = safeStem[..60];
        var unique = Guid.NewGuid().ToString("N")[..6];
        return $"{field}/{DateTime.UtcNow:yyyyMMdd-HHmmss}-{unique}-{safeStem}{extension}";
    }

    private static string AsciiOnly(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value) sb.Append(ch is >= ' ' and <= '~' ? ch : '_');
        return sb.ToString();
    }

    private static string GuessContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".tif" or ".tiff" => "image/tiff",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".mp4" => "video/mp4",
        ".mov" => "video/quicktime",
        ".pdf" => "application/pdf",
        ".csv" => "text/csv",
        _ => "application/octet-stream"
    };
}
