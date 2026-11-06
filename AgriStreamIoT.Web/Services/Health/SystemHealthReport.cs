namespace AgriStreamIoT.Web.Services.Health;

/// <summary>Result of checking one Azure component.</summary>
public sealed record ComponentHealth(string Name, string Role, bool Ok, string Detail, double? LatencyMs);

/// <summary>Everything the Pipeline page shows about the running system.</summary>
public sealed class SystemHealthReport
{
    public List<ComponentHealth> Components { get; } = new();
    public int QueueDepth { get; set; }
    public int PoisonDepth { get; set; }
    public IReadOnlyList<string> QueuedMessages { get; set; } = Array.Empty<string>();
    public int DroneImages { get; set; }
    public long DroneBytes { get; set; }
    public int MediaFiles { get; set; }
    public long MediaBytes { get; set; }
    public string? FunctionsBaseUrl { get; set; }
    public string? FunctionServedBy { get; set; }
    public DateTimeOffset CheckedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool AllOk => Components.All(c => c.Ok);
}
