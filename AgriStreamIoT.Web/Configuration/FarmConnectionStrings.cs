using AgriStreamIoT.Shared;

namespace AgriStreamIoT.Web.Configuration;

/// <summary>
/// Reads the two connection strings every time they are needed, so pasting them into
/// appsettings.Local.json takes effect without restarting the app.
///   ConnectionStrings:FarmSql      Azure SQL Database (logins, fields, master device register)
///   ConnectionStrings:FarmStorage  Azure Storage account (Blob, Table and Queue storage)
/// </summary>
public sealed class FarmConnectionStrings
{
    public const string SqlName = "FarmSql";
    public const string StorageName = "FarmStorage";

    private readonly IConfiguration _configuration;

    public FarmConnectionStrings(IConfiguration configuration) => _configuration = configuration;

    public string? Sql => Clean(_configuration.GetConnectionString(SqlName));

    public string? Storage => Clean(_configuration.GetConnectionString(StorageName));

    public bool SqlConfigured => Sql is not null;

    public bool StorageConfigured => Storage is not null;

    public string RequireSql() =>
        Sql ?? throw new FarmConfigurationException("Azure SQL Database", $"ConnectionStrings:{SqlName}");

    public string RequireStorage() =>
        Storage ?? throw new FarmConfigurationException("Azure Storage", $"ConnectionStrings:{StorageName}");

    /// <summary>Treats empty values and the "PASTE..." placeholders as not configured.</summary>
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Contains("PASTE", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();
}
