namespace AgriStreamIoT.Shared;

/// <summary>
/// Thrown when a connection string or app setting needed by a feature is missing or invalid.
/// The web app turns this into a friendly setup page instead of a crash.
/// </summary>
public sealed class FarmConfigurationException : InvalidOperationException
{
    public FarmConfigurationException(string service, string settingName, string? problem = null)
        : base(problem is null
            ? $"{service} is not configured yet. Fill in the '{settingName}' setting."
            : $"{service}: {problem} Check the '{settingName}' setting.")
    {
        Service = service;
        SettingName = settingName;
    }

    /// <summary>Friendly name of the Azure service (e.g. "Azure SQL Database").</summary>
    public string Service { get; }

    /// <summary>The configuration key the user has to fill in.</summary>
    public string SettingName { get; }
}
