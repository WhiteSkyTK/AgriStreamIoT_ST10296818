using System.Diagnostics;
using AgriStreamIoT.Shared;
using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Sql;
using AgriStreamIoT.Shared.Telemetry;
using AgriStreamIoT.Shared.Time;
using AgriStreamIoT.Web.Interfaces;
using Dapper;

namespace AgriStreamIoT.Web.Services.Health;

/// <summary>Checks Azure SQL, Table/Blob/Queue storage and the Function App, each independently.</summary>
public sealed class SystemHealthService : ISystemHealthService
{
    private readonly IServiceProvider _services;
    private readonly IFarmFunctionsClient _functions;

    public SystemHealthService(IServiceProvider services, IFarmFunctionsClient functions)
    {
        _services = services;
        _functions = functions;
    }

    public async Task<SystemHealthReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        var report = new SystemHealthReport { FunctionsBaseUrl = _functions.BaseUrl };

        report.Components.Add(await Probe("Azure SQL Database", "Logins, fields, master device register", async () =>
        {
            var database = _services.GetRequiredService<SqlConnectionFactory>();
            await using var connection = await database.OpenAsync(cancellationToken);
            async Task<int> Count(string table) => await connection.ExecuteScalarAsync<int>(
                new CommandDefinition($"SELECT COUNT(*) FROM dbo.{table};", cancellationToken: cancellationToken));
            var users = await Count("Users");
            var fields = await Count("Fields");
            var devices = await Count("Devices");
            return $"{database.Database}: {users} users, {fields} fields, {devices} devices";
        }));

        report.Components.Add(await Probe("Azure Table Storage", "Sensor telemetry, irrigation events, environmental logs", async () =>
        {
            var latest = await _services.GetRequiredService<ITelemetryQueryService>().GetLatestPerSensorAsync("F01", cancellationToken);
            var newest = latest.Count == 0 ? "no readings yet" : $"newest F01 reading {FarmClock.ToLocal(latest.Max(r => r.ReadingUtc)):HH:mm:ss}";
            return $"4 tables ready, {newest}";
        }));

        report.Components.Add(await Probe("Azure Blob Storage", "Drone imagery and farm media", async () =>
        {
            var media = _services.GetRequiredService<IFarmMediaService>();
            (report.DroneImages, report.DroneBytes) = await media.GetUsageAsync(StorageNames.DroneImagesContainer, cancellationToken);
            (report.MediaFiles, report.MediaBytes) = await media.GetUsageAsync(StorageNames.FarmMediaContainer, cancellationToken);
            return $"{report.DroneImages} drone images, {report.MediaFiles} other files ({(report.DroneBytes + report.MediaBytes) / 1_048_576.0:F1} MB)";
        }));

        report.Components.Add(await Probe("Azure Queue Storage", "irrigation-commands queue", async () =>
        {
            var status = await _services.GetRequiredService<IQueueMonitor>().GetStatusAsync(cancellationToken);
            report.QueueDepth = status.ApproximateMessages;
            report.PoisonDepth = status.PoisonMessages;
            report.QueuedMessages = status.VisibleMessages;
            return $"{status.ApproximateMessages} message(s) waiting or scheduled, {status.PoisonMessages} in poison queue";
        }));

        var stopwatch = Stopwatch.StartNew();
        var call = await _functions.GetFieldTelemetryAsync("F01", 1, cancellationToken);
        report.FunctionServedBy = call.ServedBy;
        report.Components.Add(new ComponentHealth("Azure Functions", "GetLatestTelemetry (HTTP GET) probe",
            call.Success,
            call.Success
                ? $"HTTP {call.StatusCode} from {call.ServedBy ?? "function host"}{(call.Value?.Cached == true ? " (cached snapshot)" : string.Empty)}"
                : call.Error ?? "No response",
            Math.Round(stopwatch.Elapsed.TotalMilliseconds, 0)));

        return report;
    }

    private static async Task<ComponentHealth> Probe(string name, string role, Func<Task<string>> check)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var detail = await check();
            return new ComponentHealth(name, role, true, detail, Math.Round(stopwatch.Elapsed.TotalMilliseconds, 0));
        }
        catch (FarmConfigurationException ex)
        {
            return new ComponentHealth(name, role, false, ex.Message, null);
        }
        catch (Exception ex)
        {
            return new ComponentHealth(name, role, false, ex.GetBaseException().Message, Math.Round(stopwatch.Elapsed.TotalMilliseconds, 0));
        }
    }
}
