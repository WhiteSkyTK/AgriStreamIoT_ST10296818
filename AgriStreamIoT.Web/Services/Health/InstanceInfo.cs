using System.Diagnostics;

namespace AgriStreamIoT.Web.Services.Health;

/// <summary>
/// Which App Service instance is answering. After a scale-out the load test sees several values
/// of X-Served-By; that is the evidence that autoscaling added instances.
/// </summary>
public static class InstanceInfo
{
    private static readonly Lazy<DateTimeOffset> Started = new(() =>
    {
        try { return new DateTimeOffset(Process.GetCurrentProcess().StartTime.ToUniversalTime(), TimeSpan.Zero); }
        catch { return DateTimeOffset.UtcNow; }
    });

    /// <summary>App Service instance id (first 8 characters) or the machine name locally.</summary>
    public static string ShortId
    {
        get
        {
            var id = Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID");
            return string.IsNullOrWhiteSpace(id) ? Environment.MachineName : id[..Math.Min(8, id.Length)];
        }
    }

    public static string SiteName => Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") ?? "local";

    public static string? Sku => Environment.GetEnvironmentVariable("WEBSITE_SKU");

    public static string? Region => Environment.GetEnvironmentVariable("REGION_NAME");

    public static bool RunningOnAppService => Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID") is not null;

    public static DateTimeOffset StartedUtc => Started.Value;

    public static object Snapshot()
    {
        var process = Process.GetCurrentProcess();
        return new
        {
            instance = ShortId,
            site = SiteName,
            sku = Sku,
            region = Region,
            machine = Environment.MachineName,
            processors = Environment.ProcessorCount,
            startedUtc = StartedUtc,
            uptimeMinutes = Math.Round((DateTimeOffset.UtcNow - StartedUtc).TotalMinutes, 1),
            workingSetMb = Math.Round(process.WorkingSet64 / 1_048_576.0, 1),
            gcHeapMb = Math.Round(GC.GetTotalMemory(false) / 1_048_576.0, 1),
            threads = process.Threads.Count,
            utc = DateTimeOffset.UtcNow
        };
    }
}
