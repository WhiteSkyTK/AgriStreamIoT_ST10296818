using AgriStreamIoT.Shared.Entities;

namespace AgriStreamIoT.Shared.Telemetry;

/// <summary>Maps Table Storage entities to the DTOs sent to dashboards.</summary>
public static class TelemetryMapper
{
    public static DeviceReadingDto ToDto(this SensorReadingEntity e, DateTimeOffset nowUtc) => new()
    {
        DeviceId = e.DeviceId,
        FieldId = e.FieldId,
        ReadingUtc = e.ReadingUtc,
        SoilMoisturePct = Math.Round(e.SoilMoisturePct, 1),
        SoilTemperatureC = Math.Round(e.SoilTemperatureC, 1),
        AirTemperatureC = Math.Round(e.AirTemperatureC, 1),
        HumidityPct = Math.Round(e.HumidityPct, 0),
        SoilPh = Math.Round(e.SoilPh, 2),
        SoilEcDsM = Math.Round(e.SoilEcDsM, 2),
        NitrogenPpm = Math.Round(e.NitrogenPpm, 0),
        BatteryPct = e.BatteryPct,
        SignalDbm = e.SignalDbm,
        AgeSeconds = Math.Max(0, Math.Round((nowUtc - e.ReadingUtc).TotalSeconds))
    };

    public static EnvironmentReadingDto ToDto(this EnvironmentalLogEntity e) => new()
    {
        StationId = e.StationId,
        ReadingUtc = e.ReadingUtc,
        AirTemperatureC = Math.Round(e.AirTemperatureC, 1),
        HumidityPct = Math.Round(e.HumidityPct, 0),
        RainfallMm = Math.Round(e.RainfallMm, 1),
        WindSpeedKmh = Math.Round(e.WindSpeedKmh, 1),
        WindDirectionDeg = e.WindDirectionDeg,
        SolarRadiationWm2 = Math.Round(e.SolarRadiationWm2, 0),
        Et0Mm = Math.Round(e.Et0Mm, 2),
        PressureHpa = Math.Round(e.PressureHpa, 1),
        UvIndex = Math.Round(e.UvIndex, 1),
        AlertLevel = e.AlertLevel,
        Notes = e.Notes
    };

    public static IrrigationEventDto ToDto(this IrrigationEventEntity e) => new()
    {
        CommandId = e.CommandId,
        DeviceId = e.DeviceId,
        FieldId = e.FieldId,
        Action = e.Action,
        DurationMinutes = e.DurationMinutes,
        Trigger = e.Trigger,
        RequestedBy = e.RequestedBy,
        Reason = e.Reason,
        Outcome = e.Outcome,
        OutcomeDetail = e.OutcomeDetail,
        NewStatus = e.NewStatus,
        RequestedAtUtc = e.RequestedAtUtc,
        ProcessedAtUtc = e.ProcessedAtUtc,
        LatencySeconds = Math.Round(e.LatencySeconds, 1),
        EstimatedWaterLitres = Math.Round(e.EstimatedWaterLitres, 0)
    };

    /// <summary>Rolls the latest reading of each sensor up into a field summary.</summary>
    public static FieldTelemetrySummary Summarise(string fieldId, IReadOnlyCollection<DeviceReadingDto> latest) => new()
    {
        FieldId = fieldId,
        SensorsReporting = latest.Count,
        AvgSoilMoisturePct = latest.Count == 0 ? null : Math.Round(latest.Average(r => r.SoilMoisturePct), 1),
        MinSoilMoisturePct = latest.Count == 0 ? null : Math.Round(latest.Min(r => r.SoilMoisturePct), 1),
        AvgSoilTemperatureC = latest.Count == 0 ? null : Math.Round(latest.Average(r => r.SoilTemperatureC), 1),
        AvgSoilPh = latest.Count == 0 ? null : Math.Round(latest.Average(r => r.SoilPh), 2),
        AvgSoilEcDsM = latest.Count == 0 ? null : Math.Round(latest.Average(r => r.SoilEcDsM), 2),
        LatestReadingUtc = latest.Count == 0 ? null : latest.Max(r => r.ReadingUtc),
        Sensors = latest.ToList()
    };
}
