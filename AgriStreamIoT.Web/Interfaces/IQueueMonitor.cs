namespace AgriStreamIoT.Web.Interfaces;

/// <summary>Read-only view of the irrigation command queue (for the pipeline page).</summary>
public interface IQueueMonitor
{
    Task<QueueStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>Depth of the command queue and its poison queue, plus a peek at waiting messages.</summary>
public sealed record QueueStatus(
    string QueueName,
    int ApproximateMessages,
    int PoisonMessages,
    IReadOnlyList<string> VisibleMessages);
