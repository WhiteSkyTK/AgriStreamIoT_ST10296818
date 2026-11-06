using AgriStreamIoT.Shared.Entities;
using AgriStreamIoT.Shared.Storage;
using Azure.Data.Tables;

namespace AgriStreamIoT.Shared.Telemetry;

/// <summary>Reads and writes the drone image processing audit trail (ImageProcessingAudit table).</summary>
public sealed class ImageAuditStore
{
    private readonly FarmTableStore _store;

    public ImageAuditStore(FarmTableStore store) => _store = store;

    public async Task UpsertAsync(ImageAuditEntity audit, CancellationToken cancellationToken = default)
    {
        await _store.EnsureTablesAsync(cancellationToken);
        await _store.ImageAudit.UpsertEntityAsync(audit, TableUpdateMode.Replace, cancellationToken);
    }

    public async Task<ImageAuditEntity?> GetAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
    {
        await _store.EnsureTablesAsync(cancellationToken);
        var (pk, rk) = ImageAuditEntity.KeysFor(containerName, blobName);
        var response = await _store.ImageAudit.GetEntityIfExistsAsync<ImageAuditEntity>(pk, rk, cancellationToken: cancellationToken);
        return response.HasValue ? response.Value : null;
    }

    /// <summary>Most recent audits for a container, newest first.</summary>
    public async Task<IReadOnlyList<ImageAuditEntity>> GetRecentAsync(string containerName, int take, CancellationToken cancellationToken = default)
    {
        await _store.EnsureTablesAsync(cancellationToken);
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {containerName}");
        var all = new List<ImageAuditEntity>();
        await foreach (var audit in _store.ImageAudit.QueryAsync<ImageAuditEntity>(filter: filter, maxPerPage: 1000, cancellationToken: cancellationToken))
        {
            all.Add(audit);
            if (all.Count >= 5000) break;
        }
        return all
            .OrderByDescending(a => a.UploadedAtUtc ?? a.ProcessedAtUtc ?? a.Timestamp)
            .Take(Math.Clamp(take, 1, 5000))
            .ToList();
    }

    public async Task DeleteAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
    {
        await _store.EnsureTablesAsync(cancellationToken);
        var (pk, rk) = ImageAuditEntity.KeysFor(containerName, blobName);
        await _store.ImageAudit.DeleteEntityAsync(pk, rk, cancellationToken: cancellationToken);
    }
}
