using System.Globalization;
using System.Text;

namespace AgriStreamIoT.Shared.Storage;

/// <summary>
/// Key design for the Table Storage tables.
/// Rows are written with a "newest first" RowKey so the most recent readings come back first
/// from a single-partition query; that is what makes the dashboard query cheap and fast.
/// </summary>
public static class TableKeys
{
    /// <summary>(MaxTicks - ticks) as a fixed-width 19 digit string. Newer timestamps sort first.</summary>
    public static string InvertedTicks(DateTimeOffset timestamp) =>
        (DateTimeOffset.MaxValue.UtcTicks - timestamp.UtcTicks).ToString("D19", CultureInfo.InvariantCulture);

    /// <summary>RowKey for time-series rows: "{invertedTicks}_{suffix}".</summary>
    public static string NewestFirst(DateTimeOffset timestamp, string suffix) =>
        $"{InvertedTicks(timestamp)}_{SafeKey(suffix)}";

    /// <summary>
    /// Upper RowKey bound for "everything at or after <paramref name="since"/>".
    /// Newer rows have smaller inverted ticks, so the filter is RowKey le bound.
    /// '~' sorts after '_' so rows exactly at <paramref name="since"/> are included.
    /// </summary>
    public static string RowKeyUpperBoundSince(DateTimeOffset since) => InvertedTicks(since) + "~";

    /// <summary>
    /// Makes a value safe for PartitionKey/RowKey. Table Storage forbids / \ # ? and control characters.
    /// </summary>
    public static string SafeKey(string value)
    {
        if (string.IsNullOrEmpty(value)) return "_";
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch == '/') sb.Append('|');
            else if (ch is '\\' or '#' or '?' || char.IsControl(ch)) sb.Append('_');
            else sb.Append(ch);
        }
        var key = sb.ToString();
        return key.Length > 512 ? key[..512] : key;
    }
}
