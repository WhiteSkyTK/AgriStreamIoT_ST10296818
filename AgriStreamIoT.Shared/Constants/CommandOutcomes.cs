namespace AgriStreamIoT.Shared.Constants;

/// <summary>Result of processing an irrigation command against the master device register.</summary>
public static class CommandOutcomes
{
    /// <summary>The device status was changed in Azure SQL.</summary>
    public const string Applied = "Applied";

    /// <summary>Nothing to do (duplicate delivery, already stopped, superseded auto-stop).</summary>
    public const string Ignored = "Ignored";

    /// <summary>Business rule refused the command (unknown device, safety interlock).</summary>
    public const string Rejected = "Rejected";
}
