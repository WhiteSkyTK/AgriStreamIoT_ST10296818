using AgriStreamIoT.Functions.Services;
using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Sql;
using AgriStreamIoT.Shared.Storage;
using AgriStreamIoT.Shared.Telemetry;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Application Insights: request telemetry comes from the Functions host; this adds the worker's
// own logs (e.g. "Drone image processed") so they show up in Azure Monitor / Log Analytics.
builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// By default the Application Insights logger only keeps Warning and above; keep Information too.
builder.Logging.Services.Configure<LoggerFilterOptions>(options =>
{
    var defaultRule = options.Rules.FirstOrDefault(rule => rule.ProviderName ==
        "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");
    if (defaultRule is not null) options.Rules.Remove(defaultRule);
});

builder.Services.AddMemoryCache();

// Each service is created on first use, so a missing setting only affects the function that needs it
// and the error message names the setting to fill in.
builder.Services.AddSingleton(_ => new FarmTableStore(FunctionSettings.Required(StorageNames.FunctionsStorageSetting)));
builder.Services.AddSingleton<ITelemetryQueryService, TelemetryQueryService>();
builder.Services.AddSingleton<TelemetryWriter>();
builder.Services.AddSingleton<ImageAuditStore>();
builder.Services.AddSingleton<TelemetrySnapshotCache>();
builder.Services.AddSingleton(_ => new SqlConnectionFactory(FunctionSettings.Required(StorageNames.FunctionsSqlSetting)));
builder.Services.AddSingleton<IDeviceRegisterRepository, DeviceRegisterRepository>();
builder.Services.AddSingleton(_ => new IrrigationScheduler(FunctionSettings.Required(StorageNames.FunctionsStorageSetting)));

builder.Build().Run();
