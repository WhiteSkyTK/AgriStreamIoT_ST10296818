namespace AgriStreamIoT.Web.Services.Startup;

/// <summary>Start-up progress of each Azure dependency, shown on the setup and pipeline pages.</summary>
public sealed class AppReadiness
{
    public ComponentState Sql { get; } = new("Azure SQL Database", "Schema, demo fields, devices and logins");
    public ComponentState Storage { get; } = new("Azure Storage", "Tables, blob containers, queue and 24 h of sample telemetry");

    public bool AllReady => Sql.Ready && Storage.Ready;
}

/// <summary>Thread-safe status of one start-up step.</summary>
public sealed class ComponentState
{
    private readonly object _gate = new();

    public ComponentState(string name, string description)
    {
        Name = name;
        Description = description;
    }

    public string Name { get; }
    public string Description { get; }
    public bool Ready { get; private set; }
    public bool NotConfigured { get; private set; }
    public string? LastError { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? ReadyAtUtc { get; private set; }

    public void MarkAttempt()
    {
        lock (_gate) Attempts++;
    }

    public void MarkReady()
    {
        lock (_gate)
        {
            Ready = true;
            NotConfigured = false;
            LastError = null;
            ReadyAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public void MarkFailed(string error, bool notConfigured)
    {
        lock (_gate)
        {
            Ready = false;
            NotConfigured = notConfigured;
            LastError = error;
        }
    }
}
