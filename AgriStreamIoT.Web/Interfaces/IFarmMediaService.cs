using AgriStreamIoT.Web.Models;

namespace AgriStreamIoT.Web.Interfaces;

/// <summary>Azure Blob Storage for drone imagery and other farm media.</summary>
public interface IFarmMediaService
{
    Task EnsureContainersAsync(CancellationToken cancellationToken = default);

    Task<UploadedMedia> UploadAsync(Stream content, string originalFileName, string? contentType, MediaCategory category,
        string fieldId, string uploadedBy, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaItem>> ListAsync(MediaCategory? category, string? fieldId, int take, CancellationToken cancellationToken = default);

    /// <summary>Opens a blob for streaming through the web app (used when a SAS URL cannot be made).</summary>
    Task<(Stream Content, string ContentType)?> OpenReadAsync(string container, string blobName, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string container, string blobName, CancellationToken cancellationToken = default);

    Task<(int Count, long Bytes)> GetUsageAsync(string container, CancellationToken cancellationToken = default);

    string AccountName { get; }
}
