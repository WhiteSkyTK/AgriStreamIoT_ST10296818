using AgriStreamIoT.Shared;
using AgriStreamIoT.Shared.Constants;

namespace AgriStreamIoT.Functions.Services;

/// <summary>
/// Reads Function App settings. Locally they come from local.settings.json ("Values"),
/// in Azure from Function App > Settings > Environment variables.
/// </summary>
public static class FunctionSettings
{
    public static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value) || value.Contains("PASTE", StringComparison.OrdinalIgnoreCase))
        {
            var service = name == StorageNames.FunctionsSqlSetting ? "Azure SQL Database" : "Azure Storage";
            throw new FarmConfigurationException(service, name);
        }
        return value;
    }

    public static int Int(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;

    /// <summary>Short id of the instance running this code (shows Function App scale-out).</summary>
    public static string InstanceId
    {
        get
        {
            var id = Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID")
                     ?? Environment.GetEnvironmentVariable("CONTAINER_NAME")
                     ?? Environment.MachineName;
            return id.Length > 12 ? id[..12] : id;
        }
    }
}
