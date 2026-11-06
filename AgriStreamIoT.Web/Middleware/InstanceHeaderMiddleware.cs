using AgriStreamIoT.Web.Services.Health;

namespace AgriStreamIoT.Web.Middleware;

/// <summary>
/// Adds "X-Served-By: &lt;instance&gt;" to every response. During the load test the number of
/// distinct values shows how many App Service instances autoscale brought online.
/// </summary>
public sealed class InstanceHeaderMiddleware
{
    private readonly RequestDelegate _next;

    public InstanceHeaderMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Served-By"] = InstanceInfo.ShortId;
            return Task.CompletedTask;
        });
        return _next(context);
    }
}
