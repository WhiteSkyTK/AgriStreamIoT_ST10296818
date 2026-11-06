namespace AgriStreamIoT.Shared.Constants;

/// <summary>Kinds of devices in the master device register.</summary>
public static class DeviceTypes
{
    public const string SoilSensor = "SoilSensor";
    public const string IrrigationController = "IrrigationController";
    public const string WeatherStation = "WeatherStation";
    public const string Drone = "Drone";
    public const string Gateway = "Gateway";

    public static readonly IReadOnlyList<string> All = new[]
    {
        SoilSensor, IrrigationController, WeatherStation, Drone, Gateway
    };

    public static bool IsIrrigationController(string? type) =>
        string.Equals(type, IrrigationController, StringComparison.OrdinalIgnoreCase);

    public static bool IsValid(string? type) =>
        type is not null && All.Contains(type, StringComparer.OrdinalIgnoreCase);

    /// <summary>Human-friendly label for the UI.</summary>
    public static string Label(string? type) => type switch
    {
        SoilSensor => "Soil sensor",
        IrrigationController => "Irrigation controller",
        WeatherStation => "Weather station",
        Drone => "Survey drone",
        Gateway => "LoRaWAN gateway",
        _ => type ?? "Unknown"
    };
}
