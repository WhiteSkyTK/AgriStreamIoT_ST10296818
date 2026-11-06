namespace AgriStreamIoT.Shared.Constants;

/// <summary>Operational status values held in the master device register (dbo.Devices).</summary>
public static class DeviceStatuses
{
    public const string Online = "Online";
    public const string Irrigating = "Irrigating";
    public const string Offline = "Offline";
    public const string Maintenance = "Maintenance";
    public const string Fault = "Fault";

    public static readonly IReadOnlyList<string> All = new[] { Online, Irrigating, Offline, Maintenance, Fault };

    /// <summary>Statuses that block a START command (safety interlock).</summary>
    public static bool BlocksIrrigation(string? status) =>
        status is Offline or Maintenance or Fault;

    public static bool IsValid(string? status) =>
        status is not null && All.Contains(status, StringComparer.OrdinalIgnoreCase);

    public static string? Normalise(string? status) =>
        All.FirstOrDefault(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));
}
