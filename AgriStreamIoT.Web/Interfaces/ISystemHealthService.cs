using AgriStreamIoT.Web.Services.Health;

namespace AgriStreamIoT.Web.Interfaces;

/// <summary>Live checks of every Azure component (Pipeline page).</summary>
public interface ISystemHealthService
{
    Task<SystemHealthReport> CheckAsync(CancellationToken cancellationToken = default);
}
