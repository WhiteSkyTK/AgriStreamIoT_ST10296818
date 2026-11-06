using System.Diagnostics;
using AgriStreamIoT.Shared.Time;
using AgriStreamIoT.Web.Services.Health;
using AgriStreamIoT.Web.Services.Seeding;
using AgriStreamIoT.Web.Services.Simulation;

namespace AgriStreamIoT.Web.Services.Analytics;

/// <summary>
/// 48-hour soil moisture outlook by Monte Carlo simulation: thousands of possible weather paths
/// (hotter or cooler days, random afternoon storms) give a P10–P90 band and the chance that the
/// field needs irrigation. It is deliberately compute-heavy, which makes it the endpoint used to
/// drive CPU-based autoscaling during the load test.
/// </summary>
public sealed class MoistureOutlookService
{
    private const double BinWidth = 0.25;   // % moisture per histogram bin
    private const int Bins = 200;           // 0 .. 50 %

    public sealed record HourBand(int Hour, DateTimeOffset TimeUtc, double P10, double P50, double P90);

    public sealed record OutlookResult(
        string FieldId,
        double CurrentMoisturePct,
        double ThresholdPct,
        int Iterations,
        int HoursAhead,
        double ProbabilityBelowThreshold24h,
        double ProbabilityBelowThreshold48h,
        int? MedianHoursToThreshold,
        IReadOnlyList<HourBand> Bands,
        double ComputeMs,
        string ServedBy);

    public OutlookResult Run(string fieldId, double currentMoisturePct, double thresholdPct, int iterations, int hoursAhead = 48)
    {
        iterations = Math.Clamp(iterations, 100, 200_000);
        hoursAhead = Math.Clamp(hoursAhead, 6, 96);
        var stopwatch = Stopwatch.StartNew();
        var profile = FarmSeedData.ProfileFor(fieldId);
        var start = DateTimeOffset.UtcNow;
        var random = new Random();

        // Hour-by-hour baseline drying and storm chance.
        var baseDrying = new double[hoursAhead];
        var stormChance = new double[hoursAhead];
        for (var h = 0; h < hoursAhead; h++)
        {
            var time = start.AddHours(h);
            baseDrying[h] = SensorPhysics.DryingPctPerHour(time, profile.DryingFactor);
            var localHour = FarmClock.LocalHourOfDay(time);
            stormChance[h] = fieldId == "F06" ? 0 : localHour is >= 14 and < 19 ? 0.05 : 0.004;
        }

        var histogram = new int[hoursAhead * Bins];
        var firstCrossing = new int[hoursAhead + 1];

        for (var i = 0; i < iterations; i++)
        {
            var moisture = currentMoisturePct;
            var dayFactor = Math.Max(0.4, 1 + 0.22 * SensorPhysics.Gaussian(random));   // hotter / cooler spell
            var crossed = -1;
            for (var h = 0; h < hoursAhead; h++)
            {
                moisture -= baseDrying[h] * dayFactor * (1 + 0.12 * SensorPhysics.Gaussian(random));
                if (random.NextDouble() < stormChance[h]) moisture += (2 + random.NextDouble() * 10) * 0.5;
                moisture = Math.Clamp(moisture, 9, 46);

                var bin = Math.Clamp((int)(moisture / BinWidth), 0, Bins - 1);
                histogram[h * Bins + bin]++;
                if (crossed < 0 && moisture < thresholdPct) crossed = h;
            }
            firstCrossing[crossed < 0 ? hoursAhead : crossed]++;
        }

        var bands = new List<HourBand>(hoursAhead);
        for (var h = 0; h < hoursAhead; h++)
        {
            bands.Add(new HourBand(h + 1, start.AddHours(h + 1),
                Percentile(histogram, h, iterations, 0.10),
                Percentile(histogram, h, iterations, 0.50),
                Percentile(histogram, h, iterations, 0.90)));
        }

        double CrossedWithin(int hours) => firstCrossing.Take(Math.Min(hours, hoursAhead)).Sum() / (double)iterations;

        int? median = null;
        var cumulative = 0;
        for (var h = 0; h < hoursAhead; h++)
        {
            cumulative += firstCrossing[h];
            if (cumulative >= iterations / 2.0) { median = h + 1; break; }
        }

        return new OutlookResult(fieldId, Math.Round(currentMoisturePct, 1), thresholdPct, iterations, hoursAhead,
            Math.Round(CrossedWithin(24), 3), Math.Round(CrossedWithin(48), 3), median, bands,
            Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1), InstanceInfo.ShortId);
    }

    private static double Percentile(int[] histogram, int hour, int total, double p)
    {
        var target = p * total;
        var running = 0;
        for (var b = 0; b < Bins; b++)
        {
            running += histogram[hour * Bins + b];
            if (running >= target) return Math.Round((b + 0.5) * BinWidth, 2);
        }
        return Bins * BinWidth;
    }
}
