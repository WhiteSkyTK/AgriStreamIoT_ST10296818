using AgriStreamIoT.Shared.Entities;
using AgriStreamIoT.Web.Services.Health;
using AgriStreamIoT.Web.Services.Simulation;
using AgriStreamIoT.Web.Services.Startup;

namespace AgriStreamIoT.Web.Models;

public sealed class MediaGalleryViewModel
{
    public List<MediaItem> Items { get; init; } = new();
    public Dictionary<string, ImageAuditEntity> Audits { get; init; } = new(StringComparer.Ordinal);
    public List<FieldRecord> Fields { get; init; } = new();
    public string? Category { get; init; }
    public string? Field { get; init; }
    public bool IsAdmin { get; init; }
}

public sealed class MediaUploadViewModel
{
    public MediaCategory Category { get; set; } = MediaCategory.DroneImagery;
    public string FieldId { get; set; } = string.Empty;
    public List<FieldRecord> Fields { get; set; } = new();
}

public sealed class TelemetryExplorerViewModel
{
    public string Tab { get; init; } = "readings";
    public List<FieldRecord> Fields { get; init; } = new();
    public string? Field { get; init; }
    public string? Device { get; init; }
    public int Hours { get; init; } = 24;
    public IReadOnlyList<SensorReadingEntity> Readings { get; init; } = Array.Empty<SensorReadingEntity>();
    public IReadOnlyList<EnvironmentalLogEntity> Environment { get; init; } = Array.Empty<EnvironmentalLogEntity>();
    public IReadOnlyList<IrrigationEventEntity> Irrigation { get; init; } = Array.Empty<IrrigationEventEntity>();
    public IReadOnlyList<ImageAuditEntity> Audits { get; init; } = Array.Empty<ImageAuditEntity>();
}

public sealed class PipelineViewModel
{
    public SystemHealthReport Health { get; init; } = new();
    public AppReadiness Readiness { get; init; } = new();
    public SimulatorControl Simulator { get; init; } = default!;
    public IReadOnlyList<AutomationAction> RecentActions { get; init; } = Array.Empty<AutomationAction>();
    public List<FieldRecord> Fields { get; init; } = new();
    public object Instance { get; init; } = new();
    public string InstanceId { get; init; } = string.Empty;
}

public sealed class UsersViewModel
{
    public IReadOnlyList<AppUser> Users { get; init; } = Array.Empty<AppUser>();
    public int? CurrentUserId { get; init; }
}

public sealed class SetupViewModel
{
    public string? Missing { get; init; }
    public bool SqlConfigured { get; init; }
    public bool StorageConfigured { get; init; }
    public string? FunctionsBaseUrl { get; init; }
    public bool FunctionsKeySet { get; init; }
    public AppReadiness Readiness { get; init; } = new();
    public string Environment { get; init; } = string.Empty;
}

public sealed class ErrorViewModel
{
    public string? RequestId { get; set; }
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}
