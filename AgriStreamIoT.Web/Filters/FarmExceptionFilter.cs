using AgriStreamIoT.Shared;
using AgriStreamIoT.Web.Models;
using Azure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.SqlClient;

namespace AgriStreamIoT.Web.Filters;

/// <summary>
/// Turns infrastructure problems into clear pages instead of stack traces:
///   missing connection string  -> Setup page naming the setting to fill in
///   Azure SQL / Storage errors -> "service unavailable" page with the reason and what to try
/// JSON callers (fetch from the dashboards, /api) get a 503 with the same message.
/// </summary>
public sealed class FarmExceptionFilter : IExceptionFilter
{
    private readonly ILogger<FarmExceptionFilter> _logger;
    private readonly IModelMetadataProvider _metadata;

    public FarmExceptionFilter(ILogger<FarmExceptionFilter> logger, IModelMetadataProvider metadata)
    {
        _logger = logger;
        _metadata = metadata;
    }

    public void OnException(ExceptionContext context)
    {
        var exception = context.Exception;
        ServiceProblemViewModel? problem = exception switch
        {
            FarmConfigurationException config => new ServiceProblemViewModel(config.Service, config.Message,
                $"Add the value for '{config.SettingName}' to appsettings.Local.json (local) or the App Service environment variables (Azure).",
                IsConfiguration: true, Setting: config.SettingName),
            SqlException sql => new ServiceProblemViewModel("Azure SQL Database", sql.Message,
                sql.Number is 40613 or 40197 or 40501
                    ? "The serverless database is resuming from pause. Refresh in 30–60 seconds."
                    : "Check the FarmSql connection string, the password and that the SQL server firewall allows this machine (or 'Allow Azure services').",
                IsConfiguration: false, Setting: "ConnectionStrings:FarmSql"),
            RequestFailedException storage => new ServiceProblemViewModel("Azure Storage", storage.Message,
                "Check the FarmStorage connection string (Storage account > Access keys) and that the account allows public network access.",
                IsConfiguration: false, Setting: "ConnectionStrings:FarmStorage"),
            _ => null
        };

        if (problem is null) return;   // anything else goes to the normal error page

        _logger.LogWarning(exception, "{Service} problem on {Path}: {Message}", problem.Service,
            context.HttpContext.Request.Path, problem.Message);

        if (WantsJson(context.HttpContext.Request))
        {
            context.Result = new ObjectResult(new { error = problem.Message, service = problem.Service, hint = problem.Hint })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
        }
        else if (problem.IsConfiguration)
        {
            context.Result = new RedirectToActionResult("Setup", "Home", new { missing = problem.Setting });
        }
        else
        {
            context.Result = new ViewResult
            {
                ViewName = "ServiceUnavailable",
                StatusCode = StatusCodes.Status503ServiceUnavailable,
                ViewData = new ViewDataDictionary(_metadata, context.ModelState) { Model = problem }
            };
        }
        context.ExceptionHandled = true;
    }

    private static bool WantsJson(HttpRequest request) =>
        request.Path.StartsWithSegments("/api") ||
        request.Headers.Accept.Any(a => a?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true) ||
        string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
}
