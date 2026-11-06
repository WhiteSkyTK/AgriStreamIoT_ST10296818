using AgriStreamIoT.Shared.Sql;
using AgriStreamIoT.Web.Interfaces;
using AgriStreamIoT.Web.Models;
using Dapper;
using Microsoft.Extensions.Caching.Memory;

namespace AgriStreamIoT.Web.Services.Data;

/// <summary>
/// Dapper access to dbo.Fields. Field definitions rarely change, so the list is cached for a
/// minute; that keeps Azure SQL out of the hot path when hundreds of users load dashboards.
/// </summary>
public sealed class FieldRepository : IFieldRepository
{
    private const string CacheKey = "fields:all";
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

    private const string SelectFields = """
        SELECT FieldId, Name, Crop, AreaHectares, SoilType, IrrigationMethod, MoistureThresholdPct,
               FlowRateLitresPerMin, Latitude, Longitude, Notes, UpdatedBy, UpdatedAtUtc
        FROM dbo.Fields
        """;

    private readonly SqlConnectionFactory _database;
    private readonly IMemoryCache _cache;

    public FieldRepository(SqlConnectionFactory database, IMemoryCache cache)
    {
        _database = database;
        _cache = cache;
    }

    public async Task<IReadOnlyList<FieldRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(CacheKey, out IReadOnlyList<FieldRecord>? cached) && cached is not null)
            return cached;

        await using var connection = await _database.OpenAsync(cancellationToken);
        var rows = (await connection.QueryAsync<FieldRecord>(new CommandDefinition(
            SelectFields + " ORDER BY FieldId;", cancellationToken: cancellationToken))).AsList();
        _cache.Set(CacheKey, (IReadOnlyList<FieldRecord>)rows, CacheFor);
        return rows;
    }

    public async Task<FieldRecord?> GetAsync(string fieldId, CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(cancellationToken);
        return all.FirstOrDefault(f => string.Equals(f.FieldId, fieldId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> UpdateAgronomyAsync(string fieldId, string crop, decimal moistureThresholdPct,
        decimal flowRateLitresPerMin, string? notes, string updatedBy, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.Fields
            SET Crop = @Crop, MoistureThresholdPct = @Threshold, FlowRateLitresPerMin = @FlowRate,
                Notes = @Notes, UpdatedBy = @UpdatedBy, UpdatedAtUtc = SYSUTCDATETIME()
            WHERE FieldId = @FieldId;
            """,
            new
            {
                FieldId = fieldId, Crop = crop.Trim(), Threshold = moistureThresholdPct, FlowRate = flowRateLitresPerMin,
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(), UpdatedBy = updatedBy
            }, cancellationToken: cancellationToken));
        _cache.Remove(CacheKey);
        return rows == 1;
    }

    public async Task CreateAsync(FieldRecord field, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.Fields (FieldId, Name, Crop, AreaHectares, SoilType, IrrigationMethod,
                                    MoistureThresholdPct, FlowRateLitresPerMin, Latitude, Longitude, Notes, UpdatedBy)
            VALUES (@FieldId, @Name, @Crop, @AreaHectares, @SoilType, @IrrigationMethod,
                    @MoistureThresholdPct, @FlowRateLitresPerMin, @Latitude, @Longitude, @Notes, @UpdatedBy);
            """, field, cancellationToken: cancellationToken));
        _cache.Remove(CacheKey);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.Fields;", cancellationToken: cancellationToken));
    }
}
