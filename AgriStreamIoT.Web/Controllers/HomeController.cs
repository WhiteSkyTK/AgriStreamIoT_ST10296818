using System.Diagnostics;
using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Sql;
using AgriStreamIoT.Web.Configuration;
using AgriStreamIoT.Web.Interfaces;
using AgriStreamIoT.Web.Models;
using AgriStreamIoT.Web.Services.Startup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AgriStreamIoT.Web.Controllers;

[AllowAnonymous]
public sealed class HomeController : Controller
{
    private readonly IServiceProvider _services;
    private readonly ILogger<HomeController> _logger;

    public HomeController(IServiceProvider services, ILogger<HomeController> logger)
    {
        _services = services;
        _logger = logger;
    }

    /// <summary>Public landing page with the live farm board; signed-in users go to the dashboard.</summary>
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Dashboard");

        var model = new LandingViewModel();
        try
        {
            var fields = await _services.GetRequiredService<IFieldRepository>().GetAllAsync(cancellationToken);
            var devices = await _services.GetRequiredService<IDeviceRegisterRepository>().GetAllAsync(cancellationToken);
            var telemetry = await _services.GetRequiredService<IFieldTelemetryService>()
                .GetFarmAsync(fields.Select(f => f.FieldId).ToList(), cancellationToken);
            model = new LandingViewModel
            {
                Fields = fields.Select(f => new FieldCard
                {
                    Field = f,
                    Telemetry = telemetry.Snapshot.Fields.FirstOrDefault(t => t.FieldId == f.FieldId),
                    Controller = devices.FirstOrDefault(d => d.FieldId == f.FieldId && DeviceTypes.IsIrrigationController(d.DeviceType)),
                    Sensors = devices.Count(d => d.FieldId == f.FieldId && d.DeviceType == DeviceTypes.SoilSensor)
                }).ToList(),
                Environment = telemetry.Snapshot.Environment,
                HasLiveData = true,
                GeneratedAtUtc = telemetry.Snapshot.GeneratedAtUtc
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The landing page must always render, even before Azure is configured.
            _logger.LogInformation("Landing page without live data: {Message}", ex.Message);
        }
        return View(model);
    }

    /// <summary>Which settings are missing and how far start-up has got.</summary>
    public IActionResult Setup(string? missing,
        [FromServices] FarmConnectionStrings connections,
        [FromServices] IOptionsMonitor<FunctionsOptions> functions,
        [FromServices] AppReadiness readiness,
        [FromServices] IWebHostEnvironment environment)
    {
        return View(new SetupViewModel
        {
            Missing = missing,
            SqlConfigured = connections.SqlConfigured,
            StorageConfigured = connections.StorageConfigured,
            FunctionsBaseUrl = functions.CurrentValue.BaseUri?.ToString(),
            FunctionsKeySet = !string.IsNullOrWhiteSpace(functions.CurrentValue.Key),
            Readiness = readiness,
            Environment = environment.EnvironmentName
        });
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
