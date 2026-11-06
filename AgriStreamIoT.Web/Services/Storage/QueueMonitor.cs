using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Web.Interfaces;
using Azure;
using Azure.Storage.Queues;

namespace AgriStreamIoT.Web.Services.Storage;

/// <summary>
/// Looks at the irrigation-commands queue. The web app never sends to it directly: commands go
/// through the TriggerIrrigation function. Messages are Base64 (the Functions queue format).
/// </summary>
public sealed class QueueMonitor : IQueueMonitor
{
    private readonly QueueClient _queue;
    private readonly QueueClient _poison;

    public QueueMonitor(string connectionString)
    {
        var options = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };
        _queue = new QueueClient(connectionString, StorageNames.IrrigationCommandsQueue, options);
        _poison = new QueueClient(connectionString, StorageNames.IrrigationCommandsPoisonQueue, options);
    }

    public async Task<QueueStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await _queue.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var properties = await _queue.GetPropertiesAsync(cancellationToken);
        var poisonCount = 0;
        if ((await _poison.ExistsAsync(cancellationToken)).Value)
            poisonCount = (await _poison.GetPropertiesAsync(cancellationToken)).Value.ApproximateMessagesCount;

        var visible = new List<string>();
        try
        {
            var peeked = await _queue.PeekMessagesAsync(maxMessages: 10, cancellationToken: cancellationToken);
            visible.AddRange(peeked.Value.Select(m => m.Body.ToString()));
        }
        catch (Exception ex) when (ex is RequestFailedException or FormatException)
        {
            // Peeking is informational only.
        }

        return new QueueStatus(StorageNames.IrrigationCommandsQueue,
            properties.Value.ApproximateMessagesCount, poisonCount, visible);
    }
}
