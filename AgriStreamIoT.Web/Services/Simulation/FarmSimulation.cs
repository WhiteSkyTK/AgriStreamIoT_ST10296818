using System.Collections.Concurrent;
using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Entities;
using AgriStreamIoT.Shared.Time;
using AgriStreamIoT.Web.Services.Seeding;

namespace AgriStreamIoT.Web.Services.Simulation;

/// <summary>
/// Holds the simulated state of every soil probe and produces the next reading.
/// The state is primed from the newest stored readings, so the stream continues where it left off.
/// </summary>
public sealed class FarmSimulation
{
    private readonly ConcurrentDictionary<string, SensorState> _sensors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random = new();
    private readonly object _randomLock = new();

    public sealed class SensorState
    {
        public string DeviceId { get; init; } = string.Empty;
        public string FieldId { get; init; } = string.Empty;
        public double MoisturePct { get; set; }
        public double Ph { get; set; }
        public double Ec { get; set; }
        public double NitrogenPpm { get; set; }
        public double BatteryPct { get; set; }
        public int SignalBase { get; init; }
        public bool Faulty { get; set; }
    }

    /// <summary>Gets the state of a probe, creating it from the field profile the first time.</summary>
    public SensorState GetState(string deviceId, string fieldId, int? batteryPct = null) =>
        _sensors.GetOrAdd(deviceId, id =>
        {
            var profile = FarmSeedData.ProfileFor(fieldId);
            return new SensorState
            {
                DeviceId = id,
                FieldId = fieldId,
                MoisturePct = profile.StartMoisturePct + SensorPhysics.DeviceBias(id),
                Ph = profile.PhBase + SensorPhysics.DeviceBias(id) / 20,
                Ec = profile.EcBase,
                NitrogenPpm = profile.NitrogenBase,
                BatteryPct = batteryPct ?? 90,
                SignalBase = SensorPhysics.SignalBase(id)
            };
        });

    /// <summary>Continue from a stored reading (the reported value of a faulty probe is not its true state).</summary>
    public void Prime(SensorReadingEntity latest, bool faulty)
    {
        var state = GetState(latest.DeviceId, latest.FieldId, latest.BatteryPct);
        state.Faulty = faulty;
        state.MoisturePct = faulty ? state.MoisturePct : latest.SoilMoisturePct;
        state.Ph = latest.SoilPh;
        state.Ec = latest.SoilEcDsM;
        state.NitrogenPpm = latest.NitrogenPpm;
        state.BatteryPct = latest.BatteryPct;
    }

    public void ForceMoisture(string fieldId, double moisturePct)
    {
        foreach (var state in _sensors.Values.Where(s => string.Equals(s.FieldId, fieldId, StringComparison.OrdinalIgnoreCase)))
            state.MoisturePct = moisturePct + SensorPhysics.DeviceBias(state.DeviceId) / 3;
    }

    public SensorReadingEntity NextReading(SensorState state, DateTimeOffset time, double minutes, bool irrigating, string source)
    {
        lock (_randomLock) return Step(state, time, minutes, irrigating, source, _random);
    }

    public EnvironmentalLogEntity NextWeather(string stationId, DateTimeOffset time, double intervalMinutes, string source)
    {
        lock (_randomLock) return Weather(stationId, time, intervalMinutes, source, _random);
    }

    /// <summary>One time step for one probe.</summary>
    public static SensorReadingEntity Step(SensorState s, DateTimeOffset time, double minutes, bool irrigating, string source, Random random)
    {
        var profile = FarmSeedData.ProfileFor(s.FieldId);
        var hours = minutes / 60.0;
        var covered = string.Equals(s.FieldId, "F06", StringComparison.OrdinalIgnoreCase);   // tunnels: no rain
        var rain = covered ? 0 : SensorPhysics.RainMm(time, minutes);

        s.MoisturePct += (irrigating ? profile.IrrigationGainPctPerHour : 0) * hours
                         - SensorPhysics.DryingPctPerHour(time, profile.DryingFactor) * hours
                         + rain * 0.5
                         + SensorPhysics.Gaussian(random) * 0.06 * Math.Sqrt(Math.Max(minutes, 1) / 15.0);
        s.MoisturePct = Math.Clamp(s.MoisturePct, 9, 46);   // wilting point .. field capacity

        s.Ph += (profile.PhBase - s.Ph) * 0.04 + SensorPhysics.Gaussian(random) * 0.012;
        s.Ec += (profile.EcBase - s.Ec) * 0.04 + SensorPhysics.Gaussian(random) * 0.008 + (irrigating ? 0.003 : 0);
        s.NitrogenPpm = Math.Clamp(s.NitrogenPpm + (irrigating && covered ? 0.9 : -0.025) * (minutes / 15.0), 8, 70);
        s.BatteryPct = Math.Max(4, s.BatteryPct - 0.012 * (minutes / 15.0));

        var reported = s.MoisturePct;
        if (s.Faulty) reported += 5 + SensorPhysics.Gaussian(random) * 3.5;   // drifting probe

        return new SensorReadingEntity
        {
            DeviceId = s.DeviceId,
            FieldId = s.FieldId,
            ReadingUtc = time,
            SoilMoisturePct = Math.Round(Math.Clamp(reported, 4, 60), 2),
            SoilTemperatureC = Math.Round(SensorPhysics.SoilTemperatureC(time, profile.SoilTempOffsetC) + SensorPhysics.Gaussian(random) * 0.2, 2),
            AirTemperatureC = Math.Round(SensorPhysics.AirTemperatureC(time) + (covered ? 3 : 0) + SensorPhysics.Gaussian(random) * 0.4, 2),
            HumidityPct = Math.Round(Math.Clamp(SensorPhysics.HumidityPct(time) + (covered ? 10 : 0) + SensorPhysics.Gaussian(random) * 2, 10, 100), 1),
            SoilPh = Math.Round(s.Ph, 2),
            SoilEcDsM = Math.Round(Math.Max(0.1, s.Ec), 3),
            NitrogenPpm = Math.Round(s.NitrogenPpm, 1),
            BatteryPct = (int)Math.Round(s.BatteryPct),
            SignalDbm = s.SignalBase + random.Next(-4, 5),
            Source = source
        }.WithKeys();
    }

    /// <summary>One weather station log for the interval ending at <paramref name="time"/>.</summary>
    public static EnvironmentalLogEntity Weather(string stationId, DateTimeOffset time, double intervalMinutes, string source, Random random)
    {
        var solar = FarmClock.SolarFactor(time);
        var rain = SensorPhysics.RainMm(time, intervalMinutes);
        var storming = rain > 0;

        var air = SensorPhysics.AirTemperatureC(time) + SensorPhysics.Gaussian(random) * 0.5 - (storming ? 3.5 : 0);
        var humidity = Math.Clamp(SensorPhysics.HumidityPct(time) + SensorPhysics.Gaussian(random) * 3 + (storming ? 28 : 0), 12, 100);
        var radiation = Math.Max(0, 980 * solar * (storming ? 0.3 : 1) + SensorPhysics.Gaussian(random) * 25);
        var wind = Math.Max(0, 5 + 13 * solar + SensorPhysics.Gaussian(random) * 3 + (storming ? 22 : 0));
        var et0PerHour = Math.Max(0, 0.00085 * radiation * (air + 17) / 40 + 0.012 * wind / 10);
        var uv = Math.Max(0, 11.5 * solar * (storming ? 0.35 : 1));

        var (alert, notes) = air >= 33 ? ("Warning", "Heat stress risk: irrigate early and ventilate the tunnels.")
            : wind >= 40 ? ("Warning", "High wind: drone flights suspended.")
            : storming ? ("Advisory", $"Thunderstorm: {rain:F1} mm in the last {intervalMinutes:F0} minutes.")
            : uv >= 11 ? ("Advisory", "Extreme UV: limit midday field work.")
            : ("None", (string?)null);

        return new EnvironmentalLogEntity
        {
            StationId = stationId,
            ReadingUtc = time,
            AirTemperatureC = Math.Round(air, 2),
            HumidityPct = Math.Round(humidity, 1),
            RainfallMm = Math.Round(rain, 1),
            WindSpeedKmh = Math.Round(wind, 1),
            WindDirectionDeg = (int)((300 + 40 * Math.Sin(time.ToUnixTimeSeconds() / 5400.0) + random.Next(-15, 16) + 360) % 360),
            SolarRadiationWm2 = Math.Round(radiation, 0),
            Et0Mm = Math.Round(et0PerHour * intervalMinutes / 60, 3),
            PressureHpa = Math.Round(868 + 2.5 * Math.Sin(time.ToUnixTimeSeconds() / 43200.0) + SensorPhysics.Gaussian(random) * 0.3, 1),
            UvIndex = Math.Round(uv, 1),
            AlertLevel = alert,
            Notes = notes,
            Source = source
        }.WithKeys();
    }

    /// <summary>True when the field's probe is the known-faulty demo probe.</summary>
    public static bool IsFaulty(string deviceId, string status) => status == DeviceStatuses.Fault;
}
