using AgriStreamIoT.Shared.Constants;

namespace AgriStreamIoT.Web.Models;

/// <summary>What kind of media an upload is (decides the blob container).</summary>
public enum MediaCategory
{
    /// <summary>drone-images container: triggers the ProcessDroneImageUpload function.</summary>
    DroneImagery,

    /// <summary>farm-media container: field photos, soil reports, videos.</summary>
    FarmMedia
}

/// <summary>A blob in one of the farm containers, as shown in the gallery.</summary>
public sealed class MediaItem
{
    public string Container { get; set; } = string.Empty;
    public string BlobName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? FieldId { get; set; }
    public string? UploadedBy { get; set; }
    public string? ContentType { get; set; }
    public long SizeBytes { get; set; }
    public DateTimeOffset? UploadedAtUtc { get; set; }

    /// <summary>Time-limited read URL (SAS) or a proxy URL through the web app.</summary>
    public string? ViewUrl { get; set; }

    public MediaCategory Category => Container == StorageNames.DroneImagesContainer
        ? MediaCategory.DroneImagery
        : MediaCategory.FarmMedia;

    public bool IsImage => ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>Result of one upload.</summary>
public sealed record UploadedMedia(string Container, string BlobName, string FileName, long SizeBytes);
