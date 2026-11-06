using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Messages;
using Azure.Storage.Queues;

namespace AgriStreamIoT.Functions.Services;

/// <summary>
/// Schedules the automatic STOP for a timed irrigation cycle by putting a message on the same
/// queue with a visibility delay equal to the cycle length. No timer function is needed, so the
/// solution keeps exactly four functions.
/// </summary>
public sealed class IrrigationScheduler
{
    private readonly QueueClient _queue;
    private volatile bool _queueReady;

    public IrrigationScheduler(string storageConnectionString)
    {
        // The Functions queue extension reads and writes Base64 messages, so this client must too.
        _queue = new QueueClient(storageConnectionString, StorageNames.IrrigationCommandsQueue,
            new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
    }

    public async Task<IrrigationCommand> ScheduleAutoStopAsync(IrrigationCommand start, CancellationToken cancellationToken)
    {
        if (!_queueReady)
        {
            await _queue.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            _queueReady = true;
        }

        var stop = IrrigationCommand.AutoStopFor(start, DateTimeOffset.UtcNow);
        var delay = TimeSpan.FromMinutes(Math.Clamp(start.DurationMinutes, 1, IrrigationActions.MaxDurationMinutes));
        await _queue.SendMessageAsync(FarmJson.Serialize(stop),
            visibilityTimeout: delay,
            timeToLive: delay + TimeSpan.FromDays(1),
            cancellationToken: cancellationToken);
        return stop;
    }
}
