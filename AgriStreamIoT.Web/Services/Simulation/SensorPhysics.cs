using AgriStreamIoT.Shared.Time;

namespace AgriStreamIoT.Web.Services.Simulation;

/// <summary>
/// A small, plausible model of a Highveld summer day: soil dries fastest around midday,
/// irrigation and afternoon thunderstorms wet it, temperatures follow the sun.
/// Used for the sample history, the live sensor stream and the moisture outlook.
/// </summary>
public static class SensorPhysics
{
    /// <summary>0 at about 02:30, 1 at about 14:30 (the hottest part of the day).</summary>
    public static double DayCurve(DateTimeOffset time)
    {
        var hour = FarmClock.LocalHourOfDay(time);
        return 0.5 + 0.5 * Math.Sin(2 * Math.PI * (hour - 8.5) / 24);
    }

    /// <summary>Soil moisture lost per hour (%) through evapotranspiration.</summary>
    public static double DryingPctPerHour(DateTimeOffset time, double dryingFactor) =>
        (0.12 + 0.55 * FarmClock.SolarFactor(time)) * dryingFactor;

    public static double AirTemperatureC(DateTimeOffset time) => 12 + 14 * DayCurve(time);

    /// <summary>Soil lags the air: warmest around 16:00.</summary>
    public static double SoilTemperatureC(DateTimeOffset time, double offset)
    {
        var hour = FarmClock.LocalHourOfDay(time);
        return 16 + 5.5 * Math.Sin(2 * Math.PI * (hour - 10) / 24) + offset;
    }

    public static double HumidityPct(DateTimeOffset time) => 85 - 48 * DayCurve(time);

    /// <summary>
    /// Deterministic afternoon thunderstorms on roughly one day in five (15:30–16:30, 4 mm per half hour),
    /// so the stored history, the live stream and every app instance agree on when it rained.
    /// </summary>
    public static double RainMm(DateTimeOffset time, double intervalMinutes)
    {
        var local = FarmClock.ToLocal(time);
        var stormDay = (local.DayOfYear * 37 + local.Year) % 5 == 0;
        var hour = local.Hour + local.Minute / 60.0;
        if (!stormDay || hour < 15.5 || hour >= 16.5) return 0;
        return 4.0 * intervalMinutes / 30.0;
    }

    /// <summary>Standard normal random number (Box–Muller).</summary>
    public static double Gaussian(Random random)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    /// <summary>Stable small number per device so each probe reads slightly differently.</summary>
    public static double DeviceBias(string deviceId)
    {
        var hash = 17;
        foreach (var ch in deviceId) hash = unchecked(hash * 31 + ch);
        return ((hash & 0x7FFFFFFF) % 31 - 15) / 10.0;   // -1.5 .. +1.5
    }

    public static int SignalBase(string deviceId) => -70 - (int)(Math.Abs(DeviceBias(deviceId)) * 18);
}
