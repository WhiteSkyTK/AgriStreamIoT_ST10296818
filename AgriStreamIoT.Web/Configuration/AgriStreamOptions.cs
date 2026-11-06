namespace AgriStreamIoT.Web.Configuration;

/// <summary>The "AgriStream" section of appsettings.json.</summary>
public sealed class AgriStreamOptions
{
    public const string SectionName = "AgriStream";

    /// <summary>Create the demo fields, devices, users and 24 h of telemetry on first run.</summary>
    public bool SeedDemoData { get; set; } = true;

    /// <summary>Show the demo logins on the sign-in page.</summary>
    public bool ShowDemoAccounts { get; set; } = true;

    public SimulatorOptions Simulator { get; set; } = new();

    public AutomationOptions Automation { get; set; } = new();
}

/// <summary>Live sensor stream used for demos (one instance writes, guarded by a blob lease).</summary>
public sealed class SimulatorOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Seconds between readings from each soil sensor.</summary>
    public int IntervalSeconds { get; set; } = 120;
}

/// <summary>Sensor-driven irrigation: dry soil raises an irrigation command automatically.</summary>
public sealed class AutomationOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Minimum minutes between automatic commands for the same field.</summary>
    public int CooldownMinutes { get; set; } = 20;

    public int IrrigationMinutes { get; set; } = 30;
}
