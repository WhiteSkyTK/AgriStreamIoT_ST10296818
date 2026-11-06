using System.Collections.Concurrent;
using System.Diagnostics;
using AgriStreamIoT.Web.Interfaces;
using AgriStreamIoT.Web.Models;
using AgriStreamIoT.Web.Services.Seeding;

namespace AgriStreamIoT.Web.Services.Simulation;

/// <summary>
/// Simulates a drone landing and offloading a flight: N high-resolution images uploaded to the
/// drone-images container at once. Each upload fires the ProcessDroneImageUpload blob trigger.
/// </summary>
public sealed class DroneBurstService
{
    private readonly IFarmMediaService _media;
    private readonly IServiceProvider _services;
    private readonly ILogger<DroneBurstService> _logger;

    public DroneBurstService(IFarmMediaService media, IServiceProvider services, ILogger<DroneBurstService> logger)
    {
        _media = media;
        _services = services;
        _logger = logger;
    }

    public sealed record BurstResult(int Images, long TotalBytes, double ElapsedSeconds, IReadOnlyList<string> BlobNames)
    {
        public double MegabytesPerSecond => ElapsedSeconds <= 0 ? 0 : TotalBytes / 1_048_576.0 / ElapsedSeconds;
    }

    public async Task<BurstResult> RunAsync(int count, int width, int height, string uploadedBy, CancellationToken cancellationToken)
    {
        count = Math.Clamp(count, 1, 50);
        var fields = await LoadFieldIdsAsync(cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        var uploaded = new ConcurrentBag<UploadedMedia>();
        var flight = DateTime.UtcNow;

        await Parallel.ForEachAsync(Enumerable.Range(1, count),
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken },
            async (index, token) =>
            {
                var fieldId = fields[(index - 1) % fields.Count];
                var png = SyntheticDroneImage.Generate(width, height, seed: unchecked(flight.GetHashCode() + index * 7919));
                using var content = new MemoryStream(png);
                var fileName = $"DR-01_{fieldId}_{flight:yyyyMMdd-HHmm}_{index:000}.png";
                uploaded.Add(await _media.UploadAsync(content, fileName, "image/png", MediaCategory.DroneImagery,
                    fieldId, uploadedBy, token));
            });

        var result = new BurstResult(uploaded.Count, uploaded.Sum(u => u.SizeBytes), stopwatch.Elapsed.TotalSeconds,
            uploaded.Select(u => u.BlobName).ToList());
        _logger.LogInformation("Drone burst: {Images} images ({Mb:F1} MB) uploaded in {Seconds:F1} s.",
            result.Images, result.TotalBytes / 1_048_576.0, result.ElapsedSeconds);
        return result;
    }

    private async Task<List<string>> LoadFieldIdsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var fields = await _services.GetRequiredService<IFieldRepository>().GetAllAsync(cancellationToken);
            if (fields.Count > 0) return fields.Select(f => f.FieldId).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Field list unavailable; using sample fields.");
        }
        return FarmSeedData.Fields.Select(f => f.FieldId).ToList();
    }
}
