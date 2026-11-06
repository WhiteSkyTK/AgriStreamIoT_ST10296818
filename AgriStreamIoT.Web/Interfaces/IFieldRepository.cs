using AgriStreamIoT.Web.Models;

namespace AgriStreamIoT.Web.Interfaces;

/// <summary>The farm's fields in Azure SQL (dbo.Fields). Reads are cached briefly.</summary>
public interface IFieldRepository
{
    Task<IReadOnlyList<FieldRecord>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<FieldRecord?> GetAsync(string fieldId, CancellationToken cancellationToken = default);
    Task<bool> UpdateAgronomyAsync(string fieldId, string crop, decimal moistureThresholdPct, decimal flowRateLitresPerMin,
        string? notes, string updatedBy, CancellationToken cancellationToken = default);
    Task CreateAsync(FieldRecord field, CancellationToken cancellationToken = default);
    Task<int> CountAsync(CancellationToken cancellationToken = default);
}
