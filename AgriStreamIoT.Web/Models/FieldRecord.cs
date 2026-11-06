namespace AgriStreamIoT.Web.Models;

/// <summary>A row of dbo.Fields in Azure SQL.</summary>
public sealed class FieldRecord
{
    public string FieldId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Crop { get; set; } = string.Empty;
    public decimal AreaHectares { get; set; }
    public string SoilType { get; set; } = string.Empty;
    public string IrrigationMethod { get; set; } = string.Empty;

    /// <summary>Irrigate when average soil moisture falls below this (%).</summary>
    public decimal MoistureThresholdPct { get; set; }
    public decimal FlowRateLitresPerMin { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? Notes { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
