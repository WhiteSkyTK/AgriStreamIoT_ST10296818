using System.Globalization;
using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Time;

namespace AgriStreamIoT.Web.Helpers;

/// <summary>Formatting used across the views (times shown in farm time, SAST).</summary>
public static class Display
{
    // en-GB: day-month dates and a dot as decimal separator (en-ZA uses a comma, which clashes with the charts).
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-GB");

    public static string Time(DateTimeOffset? value, string format = "HH:mm") =>
        value is null ? "–" : FarmClock.ToLocal(value.Value).ToString(format, Culture);

    public static string Time(DateTime? utcValue, string format = "HH:mm") =>
        utcValue is null ? "–" : FarmClock.ToLocal(utcValue.Value).ToString(format, Culture);

    public static string When(DateTimeOffset? value) => Time(value, "d MMM, HH:mm");

    public static string When(DateTime? utcValue) => Time(utcValue, "d MMM, HH:mm");

    /// <summary>"just now", "4 min ago", "3 h ago", "2 days ago".</summary>
    public static string Ago(DateTimeOffset? value)
    {
        if (value is null) return "never";
        var age = DateTimeOffset.UtcNow - value.Value;
        if (age < TimeSpan.FromSeconds(0)) age = TimeSpan.Zero;
        return age.TotalSeconds < 45 ? "just now"
            : age.TotalMinutes < 60 ? $"{Math.Max(1, (int)age.TotalMinutes)} min ago"
            : age.TotalHours < 24 ? $"{(int)age.TotalHours} h ago"
            : $"{(int)age.TotalDays} day{((int)age.TotalDays == 1 ? "" : "s")} ago";
    }

    public static string Ago(DateTime? utcValue) =>
        utcValue is null ? "never" : Ago(new DateTimeOffset(System.DateTime.SpecifyKind(utcValue.Value, DateTimeKind.Utc)));

    public static string Bytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        >= 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes} B"
    };

    public static string Number(double value, int decimals = 1) => value.ToString("N" + decimals, Culture);

    public static string Litres(double litres) =>
        litres >= 1000 ? $"{(litres / 1000).ToString("N1", Culture)} kL" : $"{litres.ToString("N0", Culture)} L";

    /// <summary>CSS modifier for a device status pill.</summary>
    public static string StatusClass(string? status) => status switch
    {
        DeviceStatuses.Online => "is-online",
        DeviceStatuses.Irrigating => "is-irrigating",
        DeviceStatuses.Maintenance => "is-maintenance",
        DeviceStatuses.Fault => "is-fault",
        DeviceStatuses.Offline => "is-offline",
        _ => "is-offline"
    };

    /// <summary>Bootstrap icon for a device type.</summary>
    public static string DeviceIcon(string? type) => type switch
    {
        DeviceTypes.SoilSensor => "bi-moisture",
        DeviceTypes.IrrigationController => "bi-droplet-half",
        DeviceTypes.WeatherStation => "bi-cloud-sun",
        DeviceTypes.Drone => "bi-airplane",
        DeviceTypes.Gateway => "bi-broadcast",
        _ => "bi-cpu"
    };

    /// <summary>Moisture against the field threshold: dry (below), watch (within 3%), good.</summary>
    public static (string Css, string Label) MoistureState(double? moisture, decimal threshold)
    {
        if (moisture is null) return ("is-unknown", "No data");
        var t = (double)threshold;
        return moisture < t ? ("is-dry", "Below threshold")
            : moisture < t + 3 ? ("is-watch", "Near threshold")
            : ("is-good", "Healthy");
    }

    /// <summary>Gauge fill as a percentage of the 0–50% moisture scale.</summary>
    public static string GaugePercent(double? value, double scaleMax = 50) =>
        value is null ? "0" : Math.Clamp(value.Value / scaleMax * 100, 0, 100).ToString("F1", CultureInfo.InvariantCulture);

    public static string Invariant(double value, int decimals = 2) =>
        Math.Round(value, decimals).ToString(CultureInfo.InvariantCulture);

    public static string Invariant(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
