using System.Collections.Concurrent;
using AgriStreamIoT.Web.Configuration;
using Microsoft.Extensions.Options;

namespace AgriStreamIoT.Web.Services.Simulation;

/// <summary>Runtime switches and statistics of the live sensor stream on this instance.</summary>
public sealed class SimulatorControl
{
    private readonly ConcurrentQueue<AutomationAction> _actions = new();
    private bool? _enabledOverride;
    private long _readingsWritten;

    public SimulatorControl(IOptionsMonitor<AgriStreamOptions> options) => Options = options;

    private IOptionsMonitor<AgriStreamOptions> Options { get; }

    /// <summary>On/off: the Pipeline page can override the appsettings value until the app restarts.</summary>
    public bool Enabled
    {
        get => _enabledOverride ?? Options.CurrentValue.Simulator.Enabled;
        set => _enabledOverride = value;
    }

    public int IntervalSeconds => Math.Clamp(Options.CurrentValue.Simulator.IntervalSeconds, 15, 3600);

    public SemaphoreSlim TickLock { get; } = new(1, 1);

    public DateTimeOffset? LastTickUtc { get; set; }
    public bool LastTickWasLeader { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LastWeatherUtc { get; set; }
    public long ReadingsWritten => Interlocked.Read(ref _readingsWritten);

    /// <summary>Last automatic irrigation request per field (cool-down).</summary>
    public ConcurrentDictionary<string, DateTimeOffset> LastAutoTrigger { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void AddReadings(int count) => Interlocked.Add(ref _readingsWritten, count);

    public void Record(AutomationAction action)
    {
        _actions.Enqueue(action);
        while (_actions.Count > 25 && _actions.TryDequeue(out _)) { }
    }

    public IReadOnlyList<AutomationAction> RecentActions => _actions.Reverse().ToList();
}

/// <summary>Something the sensor automation did (or decided not to do).</summary>
public sealed record AutomationAction(DateTimeOffset AtUtc, string FieldId, string DeviceId, string Outcome, string Detail);
