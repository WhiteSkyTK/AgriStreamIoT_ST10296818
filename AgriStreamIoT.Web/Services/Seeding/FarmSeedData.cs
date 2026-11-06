using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Sql;
using AgriStreamIoT.Web.Models;

namespace AgriStreamIoT.Web.Services.Seeding;

/// <summary>
/// Sample farm used on first run: six fields, 30 devices and one login per role.
/// Everything here is demo data and can be edited in the app afterwards.
/// </summary>
public static class FarmSeedData
{
    public sealed record DemoUser(string FullName, string Email, string Password, string Role, string Phone);

    /// <summary>Demo logins (shown on the sign-in page while AgriStream:ShowDemoAccounts is true).</summary>
    public static readonly IReadOnlyList<DemoUser> Users = new[]
    {
        new DemoUser("Farm Administrator", "admin@agristream.co.za", "Admin@2026", UserRoles.Admin, "+27 11 555 0100"),
        new DemoUser("Sipho Ndlovu", "farmer@agristream.co.za", "Farmer@2026", UserRoles.Farmer, "+27 82 555 0101"),
        new DemoUser("Dr Lerato Khumalo", "agronomist@agristream.co.za", "Agronomist@2026", UserRoles.Agronomist, "+27 83 555 0102")
    };

    public static IReadOnlyList<FieldRecord> Fields { get; } = new[]
    {
        Field("F01", "North Pivot 1", "Maize", 48.5m, "Clay loam", "Centre pivot", 28m, 1800m, -27.3712m, 26.6031m),
        Field("F02", "North Pivot 2", "Soybeans", 42.0m, "Sandy loam", "Centre pivot", 25m, 1600m, -27.3698m, 26.6187m),
        Field("F03", "East Orchard", "Citrus (navel oranges)", 22.3m, "Loam", "Drip", 30m, 420m, -27.3805m, 26.6302m),
        Field("F04", "River Block", "Wheat", 35.8m, "Silt loam", "Sprinkler", 24m, 1200m, -27.3921m, 26.6115m),
        Field("F05", "Vineyard Terraces", "Table grapes", 15.6m, "Sandy clay loam", "Drip", 22m, 300m, -27.3874m, 26.5968m),
        Field("F06", "Tunnel Houses", "Tomatoes", 3.2m, "Coco peat substrate", "Drip fertigation", 35m, 150m, -27.3769m, 26.5899m)
    };

    public static IReadOnlyList<DeviceRecord> Devices { get; } = BuildDevices();

    /// <summary>Per-field behaviour of the sensor model (how fast soil dries and wets).</summary>
    public sealed record FieldProfile(
        string FieldId,
        double StartMoisturePct,
        double DryingFactor,
        double IrrigationGainPctPerHour,
        double PhBase,
        double EcBase,
        double NitrogenBase,
        double SoilTempOffsetC);

    public static IReadOnlyDictionary<string, FieldProfile> Profiles { get; } = new Dictionary<string, FieldProfile>(StringComparer.OrdinalIgnoreCase)
    {
        ["F01"] = new("F01", 31.0, 1.10, 6.0, 6.4, 0.85, 38, 0.0),
        ["F02"] = new("F02", 26.5, 1.00, 6.0, 6.7, 0.70, 29, 0.4),
        ["F03"] = new("F03", 33.5, 0.80, 3.5, 6.9, 1.10, 33, -0.6),
        ["F04"] = new("F04", 27.0, 0.90, 5.0, 7.1, 0.95, 26, 0.2),
        ["F05"] = new("F05", 24.5, 0.70, 3.2, 6.2, 1.30, 22, 0.8),
        ["F06"] = new("F06", 37.5, 1.30, 4.5, 6.0, 1.80, 44, 4.0)
    };

    public static FieldProfile ProfileFor(string fieldId) =>
        Profiles.TryGetValue(fieldId, out var profile) ? profile : new FieldProfile(fieldId, 30, 1.0, 5.0, 6.5, 1.0, 30, 0);

    private static FieldRecord Field(string id, string name, string crop, decimal area, string soil, string method,
        decimal threshold, decimal flow, decimal lat, decimal lon) => new()
    {
        FieldId = id, Name = name, Crop = crop, AreaHectares = area, SoilType = soil, IrrigationMethod = method,
        MoistureThresholdPct = threshold, FlowRateLitresPerMin = flow, Latitude = lat, Longitude = lon,
        UpdatedBy = "seed"
    };

    private static List<DeviceRecord> BuildDevices()
    {
        var installed = new DateTime(2025, 8, 18);
        var list = new List<DeviceRecord>();
        var probeNames = new[] { "Soil probe A", "Soil probe B", "Soil probe C" };

        foreach (var field in Fields)
        {
            var index = int.Parse(field.FieldId[1..]);
            for (var n = 1; n <= 3; n++)
            {
                list.Add(new DeviceRecord
                {
                    DeviceId = $"SM-{field.FieldId}-{n:00}",
                    DeviceName = $"{probeNames[n - 1]} ({field.Name})",
                    DeviceType = DeviceTypes.SoilSensor,
                    FieldId = field.FieldId,
                    SerialNumber = $"AGS-SP-25-{index:00}{n:00}",
                    FirmwareVersion = "3.2.1",
                    OperationalStatus = DeviceStatuses.Online,
                    BatteryPct = 96 - index * 4 - n * 3,
                    InstalledOn = installed.AddDays(index * 3 + n)
                });
            }

            var isPivot = field.IrrigationMethod.Contains("pivot", StringComparison.OrdinalIgnoreCase);
            list.Add(new DeviceRecord
            {
                DeviceId = $"IC-{field.FieldId}",
                DeviceName = isPivot ? $"Pivot controller ({field.Name})" : $"Valve controller ({field.Name})",
                DeviceType = DeviceTypes.IrrigationController,
                FieldId = field.FieldId,
                SerialNumber = $"AGS-IC-25-{index:00}",
                FirmwareVersion = "1.8.4",
                OperationalStatus = DeviceStatuses.Online,
                InstalledOn = installed.AddDays(index)
            });
        }

        list.Add(new DeviceRecord { DeviceId = "WS-01", DeviceName = "Main weather station", DeviceType = DeviceTypes.WeatherStation, SerialNumber = "AGS-WS-25-01", FirmwareVersion = "5.0.2", OperationalStatus = DeviceStatuses.Online, BatteryPct = 100, InstalledOn = installed });
        list.Add(new DeviceRecord { DeviceId = "GW-01", DeviceName = "LoRaWAN gateway (farm office)", DeviceType = DeviceTypes.Gateway, SerialNumber = "AGS-GW-25-01", FirmwareVersion = "2.11.0", OperationalStatus = DeviceStatuses.Online, InstalledOn = installed });
        list.Add(new DeviceRecord { DeviceId = "GW-02", DeviceName = "LoRaWAN gateway (north pivots)", DeviceType = DeviceTypes.Gateway, SerialNumber = "AGS-GW-25-02", FirmwareVersion = "2.11.0", OperationalStatus = DeviceStatuses.Online, InstalledOn = installed.AddDays(2) });
        list.Add(new DeviceRecord { DeviceId = "DR-01", DeviceName = "Survey drone 1 (RGB, 20 MP)", DeviceType = DeviceTypes.Drone, SerialNumber = "AGS-DR-25-01", FirmwareVersion = "7.3.0", OperationalStatus = DeviceStatuses.Online, BatteryPct = 88, InstalledOn = installed.AddDays(10) });
        list.Add(new DeviceRecord { DeviceId = "DR-02", DeviceName = "Survey drone 2 (multispectral)", DeviceType = DeviceTypes.Drone, SerialNumber = "AGS-DR-25-02", FirmwareVersion = "7.3.0", OperationalStatus = DeviceStatuses.Offline, BatteryPct = 12, InstalledOn = installed.AddDays(10), Notes = "Docked for charging between flights." });

        // A few realistic exceptions so the register is not all green.
        Set(list, "SM-F04-03", DeviceStatuses.Offline, "No uplink since battery swap; check antenna.");
        Set(list, "IC-F05", DeviceStatuses.Maintenance, "Solenoid valve replacement booked; irrigation locked out.");
        Set(list, "SM-F06-02", DeviceStatuses.Fault, "Moisture readings drifting; recalibration needed.");
        return list;
    }

    private static void Set(List<DeviceRecord> list, string id, string status, string note)
    {
        var device = list.First(d => d.DeviceId == id);
        device.OperationalStatus = status;
        device.Notes = note;
    }
}
