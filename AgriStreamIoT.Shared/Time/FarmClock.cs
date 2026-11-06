namespace AgriStreamIoT.Shared.Time;

/// <summary>
/// The farm runs on South African Standard Time (UTC+2, no daylight saving).
/// All data is stored in UTC; this helper converts for display and for the day/night sensor model.
/// </summary>
public static class FarmClock
{
    public static readonly TimeSpan LocalOffset = TimeSpan.FromHours(2);

    public const string TimeZoneLabel = "SAST";

    public static DateTimeOffset NowLocal => DateTimeOffset.UtcNow.ToOffset(LocalOffset);

    public static DateTimeOffset ToLocal(DateTimeOffset value) => value.ToOffset(LocalOffset);

    /// <summary>SQL DATETIME2 values come back as DateTime with Kind=Unspecified; they are stored as UTC.</summary>
    public static DateTimeOffset ToLocal(DateTime utcValue) =>
        new DateTimeOffset(DateTime.SpecifyKind(utcValue, DateTimeKind.Utc)).ToOffset(LocalOffset);

    /// <summary>Hour of the day on the farm, as a fraction (e.g. 13.5 = 13:30).</summary>
    public static double LocalHourOfDay(DateTimeOffset value)
    {
        var local = ToLocal(value);
        return local.Hour + local.Minute / 60.0;
    }

    /// <summary>Peak operational window (07:00–17:00) when drones fly and sensor traffic peaks.</summary>
    public static bool IsPeakDaylight(DateTimeOffset value)
    {
        var hour = LocalHourOfDay(value);
        return hour >= 7 && hour < 17;
    }

    /// <summary>0 at night, rising to 1 at solar noon (about 12:30 on the farm).</summary>
    public static double SolarFactor(DateTimeOffset value)
    {
        var hour = LocalHourOfDay(value);
        const double sunrise = 5.75, sunset = 19.0;
        if (hour <= sunrise || hour >= sunset) return 0;
        var x = (hour - sunrise) / (sunset - sunrise);   // 0..1 across the day
        return Math.Sin(Math.PI * x);
    }
}
