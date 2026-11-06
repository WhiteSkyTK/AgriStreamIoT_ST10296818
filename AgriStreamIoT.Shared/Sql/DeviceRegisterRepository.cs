using System.Data;
using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Messages;
using Dapper;

namespace AgriStreamIoT.Shared.Sql;

/// <summary>Dapper implementation of the master device register.</summary>
public sealed class DeviceRegisterRepository : IDeviceRegisterRepository
{
    private const string SelectDevices = """
        SELECT d.DeviceId, d.DeviceName, d.DeviceType, d.FieldId, f.Name AS FieldName, d.SerialNumber,
               d.FirmwareVersion, d.OperationalStatus, d.BatteryPct, d.InstalledOn, d.LastSeenUtc,
               d.LastCommandId, d.LastCommandAction, d.LastCommandAtUtc, d.IrrigationEndsAtUtc,
               d.StatusUpdatedBy, d.StatusUpdatedAtUtc, d.Notes
        FROM dbo.Devices d
        LEFT JOIN dbo.Fields f ON f.FieldId = d.FieldId
        """;

    private const string InsertHistory = """
        INSERT INTO dbo.DeviceStatusHistory (DeviceId, OldStatus, NewStatus, CommandId, ChangedBy, Reason, ChangedAtUtc)
        VALUES (@DeviceId, @OldStatus, @NewStatus, @CommandId, @ChangedBy, @Reason, @ChangedAtUtc);
        """;

    private readonly SqlConnectionFactory _database;

    public DeviceRegisterRepository(SqlConnectionFactory database) => _database = database;

    public async Task<IReadOnlyList<DeviceRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<DeviceRecord>(new CommandDefinition(
            SelectDevices + " ORDER BY CASE WHEN d.FieldId IS NULL THEN 1 ELSE 0 END, d.FieldId, d.DeviceType, d.DeviceId;",
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<DeviceRecord>> GetByFieldAsync(string fieldId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<DeviceRecord>(new CommandDefinition(
            SelectDevices + " WHERE d.FieldId = @FieldId ORDER BY d.DeviceType, d.DeviceId;",
            new { FieldId = fieldId }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<DeviceRecord?> GetAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<DeviceRecord>(new CommandDefinition(
            SelectDevices + " WHERE d.DeviceId = @DeviceId;", new { DeviceId = deviceId }, cancellationToken: cancellationToken));
    }

    public async Task CreateAsync(DeviceRecord device, string createdBy, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.Devices (DeviceId, DeviceName, DeviceType, FieldId, SerialNumber, FirmwareVersion,
                                     OperationalStatus, BatteryPct, InstalledOn, StatusUpdatedBy, StatusUpdatedAtUtc, Notes)
            VALUES (@DeviceId, @DeviceName, @DeviceType, @FieldId, @SerialNumber, @FirmwareVersion,
                    @OperationalStatus, @BatteryPct, @InstalledOn, @CreatedBy, @Now, @Notes);
            """,
            new
            {
                device.DeviceId, device.DeviceName, device.DeviceType, device.FieldId, device.SerialNumber,
                device.FirmwareVersion, device.OperationalStatus, device.BatteryPct, device.InstalledOn,
                CreatedBy = createdBy, Now = now, device.Notes
            }, transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(InsertHistory, new
        {
            device.DeviceId, OldStatus = (string?)null, NewStatus = device.OperationalStatus, CommandId = (string?)null,
            ChangedBy = createdBy, Reason = "Device registered in the master device register", ChangedAtUtc = now
        }, transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> UpdateDetailsAsync(DeviceRecord device, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.Devices
            SET DeviceName = @DeviceName, DeviceType = @DeviceType, FieldId = @FieldId, SerialNumber = @SerialNumber,
                FirmwareVersion = @FirmwareVersion, BatteryPct = @BatteryPct, InstalledOn = @InstalledOn, Notes = @Notes
            WHERE DeviceId = @DeviceId;
            """, device, cancellationToken: cancellationToken));
        return rows == 1;
    }

    public async Task<bool> DeleteAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.Devices WHERE DeviceId = @DeviceId;", new { DeviceId = deviceId }, cancellationToken: cancellationToken));
        return rows == 1;
    }

    public async Task<bool> SetStatusAsync(string deviceId, string newStatus, string changedBy, string? reason, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var oldStatus = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT OperationalStatus FROM dbo.Devices WITH (UPDLOCK, ROWLOCK) WHERE DeviceId = @DeviceId;",
            new { DeviceId = deviceId }, transaction, cancellationToken: cancellationToken));
        if (oldStatus is null) return false;

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.Devices
            SET OperationalStatus = @NewStatus, StatusUpdatedBy = @ChangedBy, StatusUpdatedAtUtc = @Now,
                IrrigationEndsAtUtc = CASE WHEN @NewStatus = N'Irrigating' THEN IrrigationEndsAtUtc ELSE NULL END
            WHERE DeviceId = @DeviceId;
            """, new { DeviceId = deviceId, NewStatus = newStatus, ChangedBy = changedBy, Now = now },
            transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(InsertHistory, new
        {
            DeviceId = deviceId, OldStatus = oldStatus, NewStatus = newStatus, CommandId = (string?)null,
            ChangedBy = changedBy, Reason = reason ?? "Status changed in the device register", ChangedAtUtc = now
        }, transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<CommandApplyResult> ApplyIrrigationCommandAsync(IrrigationCommand command, string processedBy, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        // UPDLOCK serialises concurrent commands for the same controller across Function instances.
        var device = await connection.QuerySingleOrDefaultAsync<IrrigationRules.DeviceState>(new CommandDefinition("""
            SELECT d.DeviceId, d.DeviceType, d.FieldId, d.OperationalStatus, d.LastCommandId, f.FlowRateLitresPerMin
            FROM dbo.Devices d WITH (UPDLOCK, ROWLOCK)
            LEFT JOIN dbo.Fields f ON f.FieldId = d.FieldId
            WHERE d.DeviceId = @DeviceId;
            """, new { command.DeviceId }, transaction, cancellationToken: cancellationToken));

        var result = IrrigationRules.Decide(command, device, now);

        if (result.Applied)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.Devices
                SET OperationalStatus = @NewStatus,
                    LastCommandId = @CommandId,
                    LastCommandAction = @Action,
                    LastCommandAtUtc = @Now,
                    IrrigationEndsAtUtc = @EndsAt,
                    LastSeenUtc = @Now,
                    StatusUpdatedBy = @ProcessedBy,
                    StatusUpdatedAtUtc = @Now
                WHERE DeviceId = @DeviceId;
                """,
                new
                {
                    result.NewStatus, command.CommandId, command.Action, Now = now,
                    EndsAt = result.IrrigationEndsAtUtc, ProcessedBy = processedBy, command.DeviceId
                }, transaction, cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(InsertHistory, new
            {
                command.DeviceId, OldStatus = result.PreviousStatus, result.NewStatus, command.CommandId,
                ChangedBy = $"{processedBy} ({command.Trigger}: {command.RequestedBy})",
                Reason = Truncate(command.Reason ?? result.Detail, 300), ChangedAtUtc = now
            }, transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task TouchLastSeenAsync(IEnumerable<(string DeviceId, DateTime SeenUtc, int? BatteryPct)> heartbeats, CancellationToken cancellationToken = default)
    {
        var parameters = heartbeats.Select(h => new { h.DeviceId, h.SeenUtc, h.BatteryPct }).ToList();
        if (parameters.Count == 0) return;
        await using var connection = await _database.OpenAsync(cancellationToken);
        // Dapper runs the statement once per element.
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.Devices
            SET LastSeenUtc = @SeenUtc, BatteryPct = COALESCE(@BatteryPct, BatteryPct)
            WHERE DeviceId = @DeviceId;
            """, parameters, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<DeviceStatusChange>> GetHistoryAsync(string? deviceId, int take, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<DeviceStatusChange>(new CommandDefinition("""
            SELECT TOP (@Take) h.HistoryId, h.DeviceId, d.DeviceName, h.OldStatus, h.NewStatus, h.CommandId,
                   h.ChangedBy, h.Reason, h.ChangedAtUtc
            FROM dbo.DeviceStatusHistory h
            JOIN dbo.Devices d ON d.DeviceId = h.DeviceId
            WHERE (@DeviceId IS NULL OR h.DeviceId = @DeviceId)
            ORDER BY h.ChangedAtUtc DESC, h.HistoryId DESC;
            """, new { Take = Math.Clamp(take, 1, 500), DeviceId = deviceId }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyDictionary<string, int>> CountByStatusAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<StatusCount>(new CommandDefinition(
            "SELECT OperationalStatus AS Status, COUNT(*) AS Total FROM dbo.Devices GROUP BY OperationalStatus;",
            cancellationToken: cancellationToken));
        var counts = DeviceStatuses.All.ToDictionary(s => s, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows) counts[row.Status] = row.Total;
        return counts;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    /// <summary>Row shape for <see cref="CountByStatusAsync"/> (public so Dapper can materialise it).</summary>
    public sealed class StatusCount
    {
        public string Status { get; set; } = string.Empty;
        public int Total { get; set; }
    }
}
