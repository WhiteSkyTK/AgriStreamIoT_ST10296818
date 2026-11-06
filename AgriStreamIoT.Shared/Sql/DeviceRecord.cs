namespace AgriStreamIoT.Shared.Sql;

/// <summary>One row of the master device register (dbo.Devices), plus the field name.</summary>
public sealed class DeviceRecord
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string? FieldId { get; set; }
    public string? FieldName { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public string FirmwareVersion { get; set; } = string.Empty;
    public string OperationalStatus { get; set; } = string.Empty;
    public int? BatteryPct { get; set; }
    public DateTime InstalledOn { get; set; }

    // All DATETIME2 columns hold UTC.
    public DateTime? LastSeenUtc { get; set; }
    public string? LastCommandId { get; set; }
    public string? LastCommandAction { get; set; }
    public DateTime? LastCommandAtUtc { get; set; }
    public DateTime? IrrigationEndsAtUtc { get; set; }
    public string? StatusUpdatedBy { get; set; }
    public DateTime StatusUpdatedAtUtc { get; set; }
    public string? Notes { get; set; }
}
