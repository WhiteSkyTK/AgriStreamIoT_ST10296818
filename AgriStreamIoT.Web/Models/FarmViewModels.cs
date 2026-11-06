using System.ComponentModel.DataAnnotations;
using AgriStreamIoT.Shared.Entities;
using AgriStreamIoT.Shared.Sql;
using AgriStreamIoT.Shared.Telemetry;
using AgriStreamIoT.Web.Services.Telemetry;

namespace AgriStreamIoT.Web.Models;

/// <summary>A field with its latest conditions, as shown on cards and the farm board.</summary>
public sealed class FieldCard
{
    public FieldRecord Field { get; init; } = new();
    public FieldTelemetrySummary? Telemetry { get; init; }
    public DeviceRecord? Controller { get; init; }
    public int Sensors { get; init; }
    public int SensorsWithIssues { get; init; }
}

public sealed class LandingViewModel
{
    public List<FieldCard> Fields { get; init; } = new();
    public EnvironmentReadingDto? Environment { get; init; }
    public bool HasLiveData { get; init; }
    public DateTimeOffset? GeneratedAtUtc { get; init; }
}

public sealed class DashboardViewModel
{
    public List<FieldCard> Fields { get; init; } = new();
    public TelemetryResult? Telemetry { get; init; }
    public IReadOnlyDictionary<string, int> DeviceCounts { get; init; } = new Dictionary<string, int>();
    public int TotalDevices { get; init; }
    public List<IrrigationEventDto> RecentIrrigation { get; init; } = new();
    public List<ImageAuditEntity> RecentAudits { get; init; } = new();
    public int ImagesProcessedToday { get; init; }
    public double WaterTodayLitres { get; init; }
    public string? StorageWarning { get; init; }
}

public sealed class FieldDashboardViewModel
{
    public FieldRecord Field { get; init; } = new();
    public List<DeviceRecord> Devices { get; init; } = new();
    public DeviceRecord? Controller { get; init; }
    public TelemetryResult? Telemetry { get; init; }
    public int Hours { get; init; }
    public bool CanIrrigate { get; init; }
    public bool CanEditField { get; init; }
    public List<FieldRecord> AllFields { get; init; } = new();
}

public sealed class FieldEditViewModel
{
    public string FieldId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(60)]
    public string Crop { get; set; } = string.Empty;

    [Range(5, 45, ErrorMessage = "Choose a threshold between 5% and 45%.")]
    [Display(Name = "Irrigate below (soil moisture %)")]
    public decimal MoistureThresholdPct { get; set; }

    [Range(10, 20000, ErrorMessage = "Enter a flow rate between 10 and 20 000 L/min.")]
    [Display(Name = "System flow rate (litres per minute)")]
    public decimal FlowRateLitresPerMin { get; set; }

    [StringLength(400)]
    [Display(Name = "Agronomy notes")]
    public string? Notes { get; set; }
}

/// <summary>Posted by the "Trigger irrigation" panel.</summary>
public sealed class TriggerIrrigationForm
{
    [Required] public string FieldId { get; set; } = string.Empty;
    [Required] public string DeviceId { get; set; } = string.Empty;
    [Required] public string Action { get; set; } = "START";
    [Range(1, 240)] public int DurationMinutes { get; set; } = 30;
    [StringLength(200)] public string? Reason { get; set; }
}
