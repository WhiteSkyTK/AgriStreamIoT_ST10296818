using AgriStreamIoT.Shared.Sql;
using AgriStreamIoT.Shared.Storage;
using AgriStreamIoT.Shared.Telemetry;
using AgriStreamIoT.Web.Interfaces;
using AgriStreamIoT.Web.Services.Analytics;
using AgriStreamIoT.Web.Services.Data;
using AgriStreamIoT.Web.Services.Functions;
using AgriStreamIoT.Web.Services.Health;
using AgriStreamIoT.Web.Services.Seeding;
using AgriStreamIoT.Web.Services.Simulation;
using AgriStreamIoT.Web.Services.Startup;
using AgriStreamIoT.Web.Services.Storage;
using AgriStreamIoT.Web.Services.Telemetry;
using Microsoft.Extensions.Options;

namespace AgriStreamIoT.Web.Configuration;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers every AgriStream service. Azure clients are created on first use.</summary>
    public static IServiceCollection AddAgriStream(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AgriStreamOptions>(configuration.GetSection(AgriStreamOptions.SectionName));
        services.Configure<FunctionsOptions>(configuration.GetSection(FunctionsOptions.SectionName));
        services.AddMemoryCache();

        services.AddSingleton<FarmConnectionStrings>();

        // ── Azure SQL Database (relational) ─────────────────────────
        services.AddSingleton(sp => new SqlConnectionFactory(sp.GetRequiredService<FarmConnectionStrings>().RequireSql()));
        services.AddSingleton<IDeviceRegisterRepository, DeviceRegisterRepository>();
        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<IFieldRepository, FieldRepository>();

        // ── Azure Storage: Tables, Blobs, Queues ─────────────────────
        services.AddSingleton(sp => new FarmTableStore(sp.GetRequiredService<FarmConnectionStrings>().RequireStorage()));
        services.AddSingleton<ITelemetryQueryService, TelemetryQueryService>();
        services.AddSingleton<TelemetryWriter>();
        services.AddSingleton<ImageAuditStore>();
        services.AddSingleton<IFarmMediaService>(sp => new FarmMediaService(sp.GetRequiredService<FarmConnectionStrings>().RequireStorage()));
        services.AddSingleton<IQueueMonitor>(sp => new QueueMonitor(sp.GetRequiredService<FarmConnectionStrings>().RequireStorage()));
        services.AddSingleton<ISimulatorLease>(sp => new BlobSimulatorLease(sp.GetRequiredService<FarmConnectionStrings>().RequireStorage()));

        // ── Azure Functions (HTTP GET telemetry, HTTP POST irrigation) ──
        // Typed client (transient): reads the current settings each time, so a pasted URL/key applies at once.
        services.AddHttpClient<IFarmFunctionsClient, FarmFunctionsClient>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<FunctionsOptions>>().CurrentValue;
            if (options.BaseUri is not null) http.BaseAddress = options.BaseUri;
            http.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 3, 120));
            if (!string.IsNullOrWhiteSpace(options.Key))
                http.DefaultRequestHeaders.Add("x-functions-key", options.Key.Trim());
        });

        // ── Application services ────────────────────────────────────
        // Services that call the Functions client are transient (the typed HttpClient is transient).
        services.AddTransient<IFieldTelemetryService, FieldTelemetryService>();
        services.AddTransient<ISystemHealthService, SystemHealthService>();
        services.AddTransient<IrrigationAutomation>();
        services.AddTransient<DroneBurstService>();

        services.AddSingleton<MoistureOutlookService>();
        services.AddSingleton<AppReadiness>();
        services.AddSingleton<DatabaseSeeder>();
        services.AddSingleton<TelemetrySeeder>();
        services.AddSingleton<SimulatorControl>();
        services.AddSingleton<FarmSimulation>();
        services.AddSingleton<SensorStreamRunner>();

        // ── Background work ─────────────────────────────────────────
        services.AddHostedService<StartupInitializer>();      // schema + seed data, with retries
        services.AddHostedService<SensorSimulatorService>();  // live sensor stream + auto irrigation

        return services;
    }
}
