namespace AgriStreamIoT.Shared.Constants;

/// <summary>Actions an irrigation command can ask a controller to perform.</summary>
public static class IrrigationActions
{
    public const string Start = "START";
    public const string Stop = "STOP";

    public static bool IsValid(string? action) => action is Start or Stop;

    public static string? Normalise(string? action) => action?.Trim().ToUpperInvariant() switch
    {
        Start => Start,
        Stop => Stop,
        _ => null
    };

    public const int MinDurationMinutes = 1;
    public const int MaxDurationMinutes = 240;
    public const int DefaultDurationMinutes = 30;
}
