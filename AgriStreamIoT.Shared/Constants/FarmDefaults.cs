namespace AgriStreamIoT.Shared.Constants;

/// <summary>Defaults shared by the web app and the Function App.</summary>
public static class FarmDefaults
{
    /// <summary>The farm weather station whose readings feed the environmental logs.</summary>
    public const string PrimaryWeatherStationId = "WS-01";

    /// <summary>Default look-back window for the field dashboard chart.</summary>
    public const int DashboardHours = 24;

    /// <summary>Seconds a telemetry snapshot may be served from cache (protects the Table partition at peak load).</summary>
    public const int TelemetryCacheSeconds = 10;
}
