using AgriStreamIoT.Shared.Constants;

namespace AgriStreamIoT.Shared.Sql;

/// <summary>What happened when an irrigation command was applied to the device register.</summary>
public sealed record CommandApplyResult(
    string Outcome,
    string Detail,
    string? PreviousStatus,
    string? NewStatus,
    DateTime? IrrigationEndsAtUtc = null)
{
    public bool Applied => Outcome == CommandOutcomes.Applied;

    /// <summary>Water a START will use (field flow rate x duration). 0 for anything else.</summary>
    public double EstimatedWaterLitres { get; init; }

    public static CommandApplyResult AppliedChange(string? previous, string next, string detail, DateTime? endsAtUtc = null) =>
        new(CommandOutcomes.Applied, detail, previous, next, endsAtUtc);

    public static CommandApplyResult Ignored(string? current, string detail) =>
        new(CommandOutcomes.Ignored, detail, current, current);

    public static CommandApplyResult Rejected(string? current, string detail) =>
        new(CommandOutcomes.Rejected, detail, current, current);
}
