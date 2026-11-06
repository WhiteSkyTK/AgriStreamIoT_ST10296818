using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgriStreamIoT.Shared.Messages;

/// <summary>
/// One JSON format for every message and API payload (camelCase, case-insensitive),
/// shared by the web app and the Function App.
/// </summary>
public static class FarmJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    /// <summary>Parses a queue message; returns null instead of throwing for malformed JSON.</summary>
    public static IrrigationCommand? TryParseCommand(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<IrrigationCommand>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
