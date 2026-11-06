using AgriStreamIoT.Shared.Messages;

namespace AgriStreamIoT.Shared.Sql;

/// <summary>The master device register in Azure SQL (dbo.Devices + dbo.DeviceStatusHistory).</summary>
public interface IDeviceRegisterRepository
{
    Task<IReadOnlyList<DeviceRecord>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeviceRecord>> GetByFieldAsync(string fieldId, CancellationToken cancellationToken = default);
    Task<DeviceRecord?> GetAsync(string deviceId, CancellationToken cancellationToken = default);

    Task CreateAsync(DeviceRecord device, string createdBy, CancellationToken cancellationToken = default);
    Task<bool> UpdateDetailsAsync(DeviceRecord device, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>Manual status change by an admin (writes a history row).</summary>
    Task<bool> SetStatusAsync(string deviceId, string newStatus, string changedBy, string? reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies an irrigation command inside a transaction: reads the device under an update lock,
    /// runs <see cref="IrrigationRules"/>, updates the operational status and writes history.
    /// Used by the ProcessIrrigationCommand queue trigger.
    /// </summary>
    Task<CommandApplyResult> ApplyIrrigationCommandAsync(IrrigationCommand command, string processedBy, CancellationToken cancellationToken = default);

    /// <summary>Records that sensors reported in (simulator / gateway heartbeat).</summary>
    Task TouchLastSeenAsync(IEnumerable<(string DeviceId, DateTime SeenUtc, int? BatteryPct)> heartbeats, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceStatusChange>> GetHistoryAsync(string? deviceId, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, int>> CountByStatusAsync(CancellationToken cancellationToken = default);
}
