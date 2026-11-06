using System.Text.RegularExpressions;
using AgriStreamIoT.Shared.Constants;

namespace AgriStreamIoT.Shared.Messages;

/// <summary>Shape validation for irrigation commands (business rules run later against Azure SQL).</summary>
public static partial class IrrigationCommandValidator
{
    [GeneratedRegex("^[A-Za-z0-9-]{2,20}$")]
    private static partial Regex IdPattern();

    public static IReadOnlyList<string> Validate(IrrigationCommand command)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(command.DeviceId) || !IdPattern().IsMatch(command.DeviceId))
            errors.Add("deviceId is required (letters, digits and dashes, e.g. IC-F01).");

        if (string.IsNullOrWhiteSpace(command.FieldId) || !IdPattern().IsMatch(command.FieldId))
            errors.Add("fieldId is required (e.g. F01).");

        if (!IrrigationActions.IsValid(command.Action))
            errors.Add("action must be START or STOP.");

        if (command.Action == IrrigationActions.Start &&
            (command.DurationMinutes < IrrigationActions.MinDurationMinutes ||
             command.DurationMinutes > IrrigationActions.MaxDurationMinutes))
            errors.Add($"durationMinutes must be between {IrrigationActions.MinDurationMinutes} and {IrrigationActions.MaxDurationMinutes} for START.");

        if (command.RequestedBy.Length > 120)
            errors.Add("requestedBy is too long (max 120 characters).");

        if (command.Reason is { Length: > 300 })
            errors.Add("reason is too long (max 300 characters).");

        return errors;
    }
}
