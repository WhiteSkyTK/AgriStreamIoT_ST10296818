namespace AgriStreamIoT.Shared.Sql;

/// <summary>One row of dbo.DeviceStatusHistory.</summary>
public sealed class DeviceStatusChange
{
    public long HistoryId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public string? OldStatus { get; set; }
    public string NewStatus { get; set; } = string.Empty;
    public string? CommandId { get; set; }
    public string ChangedBy { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime ChangedAtUtc { get; set; }
}
