using System.ComponentModel.DataAnnotations;
using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Sql;
using AgriStreamIoT.Shared.Telemetry;

namespace AgriStreamIoT.Web.Models;

public sealed class DeviceListViewModel
{
    public List<DeviceRecord> Devices { get; init; } = new();
    public List<FieldRecord> Fields { get; init; } = new();
    public IReadOnlyDictionary<string, int> Counts { get; init; } = new Dictionary<string, int>();
    public int Total { get; init; }
    public string? Field { get; init; }
    public string? Type { get; init; }
    public string? Status { get; init; }
    public bool IsAdmin { get; init; }
}

public sealed class DeviceDetailsViewModel
{
    public DeviceRecord Device { get; init; } = new();
    public IReadOnlyList<DeviceStatusChange> History { get; init; } = Array.Empty<DeviceStatusChange>();
    public List<IrrigationEventDto> Irrigation { get; init; } = new();
    public bool IsAdmin { get; init; }
}

public sealed class DeviceEditViewModel
{
    public bool IsNew { get; set; }

    [Required, RegularExpression("^[A-Za-z0-9-]{2,20}$", ErrorMessage = "Use 2–20 letters, digits or dashes, e.g. SM-F01-04.")]
    [Display(Name = "Device ID")]
    public string DeviceId { get; set; } = string.Empty;

    [Required, StringLength(100)]
    [Display(Name = "Name")]
    public string DeviceName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Type")]
    public string DeviceType { get; set; } = DeviceTypes.SoilSensor;

    [Display(Name = "Field")]
    public string? FieldId { get; set; }

    [Required, StringLength(40)]
    [Display(Name = "Serial number")]
    public string SerialNumber { get; set; } = string.Empty;

    [Required, StringLength(20)]
    [Display(Name = "Firmware")]
    public string FirmwareVersion { get; set; } = "1.0.0";

    [Display(Name = "Starting status")]
    public string OperationalStatus { get; set; } = DeviceStatuses.Online;

    [Range(0, 100)]
    [Display(Name = "Battery %")]
    public int? BatteryPct { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Installed on")]
    public DateTime InstalledOn { get; set; } = DateTime.UtcNow.Date;

    [StringLength(400)]
    public string? Notes { get; set; }

    public List<FieldRecord> Fields { get; set; } = new();

    public DeviceRecord ToRecord() => new()
    {
        DeviceId = DeviceId.Trim().ToUpperInvariant(),
        DeviceName = DeviceName.Trim(),
        DeviceType = DeviceType,
        FieldId = string.IsNullOrWhiteSpace(FieldId) ? null : FieldId,
        SerialNumber = SerialNumber.Trim(),
        FirmwareVersion = FirmwareVersion.Trim(),
        OperationalStatus = DeviceStatuses.Normalise(OperationalStatus) ?? DeviceStatuses.Online,
        BatteryPct = BatteryPct,
        InstalledOn = InstalledOn.Date,
        Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim()
    };

    public static DeviceEditViewModel From(DeviceRecord d) => new()
    {
        DeviceId = d.DeviceId,
        DeviceName = d.DeviceName,
        DeviceType = d.DeviceType,
        FieldId = d.FieldId,
        SerialNumber = d.SerialNumber,
        FirmwareVersion = d.FirmwareVersion,
        OperationalStatus = d.OperationalStatus,
        BatteryPct = d.BatteryPct,
        InstalledOn = d.InstalledOn,
        Notes = d.Notes
    };
}
