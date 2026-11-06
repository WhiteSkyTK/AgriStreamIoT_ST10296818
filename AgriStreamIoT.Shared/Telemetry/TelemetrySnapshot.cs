namespace AgriStreamIoT.Shared.Telemetry;

/// <summary>
/// Everything a dashboard needs in one response. Returned by the GetLatestTelemetry function
/// (and by the direct Table Storage fallback in the web app).
/// </summary>
public sealed class TelemetrySnapshot
{
    /// <summary>"field" (one field in detail) or "farm" (summary of several fields).</summary>
    public string Scope { get; set; } = "field";
    public string? FieldId { get; set; }
    public int Hours { get; set; }
    public DateTimeOffset GeneratedAtUtc { get; set; }

    /// <summary>Time spent querying Table Storage (0 when served from cache).</summary>
    public double QueryMs { get; set; }
    public int RowsRead { get; set; }
    public bool Cached { get; set; }
    public double CacheAgeSeconds { get; set; }

    /// <summary>Which code path produced it, e.g. "Azure Function GetLatestTelemetry".</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Instance that served it (shows scale-out in the Function App).</summary>
    public string? ServedBy { get; set; }

    /// <summary>Latest reading of every sensor (field scope).</summary>
    public List<DeviceReadingDto> LatestReadings { get; set; } = new();

    /// <summary>Per-field roll-up (farm scope).</summary>
    public List<FieldTelemetrySummary> Fields { get; set; } = new();

    /// <summary>Downsampled history per sensor for the chart (field scope).</summary>
    public List<TelemetryPoint> Series { get; set; } = new();
    public int SeriesBucketMinutes { get; set; }

    public EnvironmentReadingDto? Environment { get; set; }
    public List<IrrigationEventDto> RecentIrrigation { get; set; } = new();

    /// <summary>Shallow copy used when serving from cache.</summary>
    public TelemetrySnapshot CopyForCache(double ageSeconds)
    {
        var copy = (TelemetrySnapshot)MemberwiseClone();
        copy.Cached = true;
        copy.CacheAgeSeconds = Math.Round(ageSeconds, 1);
        copy.QueryMs = 0;
        return copy;
    }
}
