namespace AgriStreamIoT.Shared.Constants;

/// <summary>What caused an irrigation command to be raised.</summary>
public static class CommandTriggers
{
    /// <summary>A user clicked "Trigger irrigation" in the web app.</summary>
    public const string Manual = "Manual";

    /// <summary>The automation detected soil moisture below the field threshold.</summary>
    public const string Sensor = "Sensor";

    /// <summary>The delayed STOP scheduled when a timed START was applied.</summary>
    public const string AutoStop = "AutoStop";

    /// <summary>Historical sample data (seeded schedule).</summary>
    public const string Schedule = "Schedule";

    private static readonly string[] Known = { Manual, Sensor, AutoStop, Schedule };

    /// <summary>Returns the canonical trigger name; anything unknown is treated as Manual.</summary>
    public static string Normalise(string? trigger) =>
        Known.FirstOrDefault(k => string.Equals(k, trigger, StringComparison.OrdinalIgnoreCase)) ?? Manual;
}
