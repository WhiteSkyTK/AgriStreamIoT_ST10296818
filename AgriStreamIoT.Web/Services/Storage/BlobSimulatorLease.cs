using AgriStreamIoT.Web.Interfaces;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;

namespace AgriStreamIoT.Web.Services.Storage;

/// <summary>
/// Leader election with a 60-second blob lease on pipeline-locks/sensor-simulator.lock.
/// The instance that holds the lease writes simulated readings; the others skip the tick.
/// </summary>
public sealed class BlobSimulatorLease : ISimulatorLease
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);
    private readonly BlobContainerClient _container;
    private readonly BlobClient _lockBlob;
    private readonly BlobLeaseClient _lease;
    private volatile bool _ready;

    public BlobSimulatorLease(string connectionString)
    {
        _container = new BlobContainerClient(connectionString, "pipeline-locks");
        _lockBlob = _container.GetBlobClient("sensor-simulator.lock");
        // A fixed lease id per process lets this instance renew its own lease.
        _lease = _lockBlob.GetBlobLeaseClient(Guid.NewGuid().ToString());
    }

    public async Task<bool> TryAcquireAsync(CancellationToken cancellationToken = default)
    {
        if (!_ready)
        {
            await _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            if (!(await _lockBlob.ExistsAsync(cancellationToken)).Value)
            {
                try
                {
                    await _lockBlob.UploadAsync(BinaryData.FromString("AgriStream sensor simulator lock"),
                        overwrite: false, cancellationToken: cancellationToken);
                }
                catch (RequestFailedException ex) when (ex.Status == 409)
                {
                    // Another instance created it first.
                }
            }
            _ready = true;
        }

        try
        {
            await _lease.AcquireAsync(LeaseDuration, cancellationToken: cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // 409 LeaseAlreadyPresent: held by another instance - unless it is ours, then renew.
            try
            {
                await _lease.RenewAsync(cancellationToken: cancellationToken);
                return true;
            }
            catch (RequestFailedException)
            {
                return false;
            }
        }
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _lease.ReleaseAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException)
        {
            // Not held or already expired.
        }
    }
}
