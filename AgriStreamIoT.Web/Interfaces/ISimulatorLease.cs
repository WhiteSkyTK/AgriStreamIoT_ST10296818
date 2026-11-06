namespace AgriStreamIoT.Web.Interfaces;

/// <summary>
/// Makes sure only one web app instance runs the sensor simulator at a time, even after
/// autoscale has added instances.
/// </summary>
public interface ISimulatorLease
{
    /// <summary>True when this instance holds (or just took) the lease.</summary>
    Task<bool> TryAcquireAsync(CancellationToken cancellationToken = default);

    Task ReleaseAsync(CancellationToken cancellationToken = default);
}
