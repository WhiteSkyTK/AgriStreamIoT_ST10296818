using Microsoft.Data.SqlClient;

namespace AgriStreamIoT.Shared.Sql;

/// <summary>
/// Opens connections to the Azure SQL database with built-in retry. The retry matters for the
/// serverless / free-offer database: after auto-pause the first login fails with error 40613
/// while the database resumes (up to about a minute).
/// </summary>
public sealed class SqlConnectionFactory
{
    private static readonly SqlRetryLogicBaseProvider RetryProvider =
        SqlConfigurableRetryFactory.CreateExponentialRetryProvider(new SqlRetryLogicOption
        {
            NumberOfTries = 6,
            DeltaTime = TimeSpan.FromSeconds(2),
            MaxTimeInterval = TimeSpan.FromSeconds(30)
        });

    private readonly string _connectionString;

    public SqlConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new FarmConfigurationException("Azure SQL Database", "ConnectionStrings:FarmSql");

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            if (builder.ConnectTimeout < 30) builder.ConnectTimeout = 30;
            DataSource = builder.DataSource;
            Database = builder.InitialCatalog;
            _connectionString = builder.ConnectionString;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException)
        {
            throw new FarmConfigurationException("Azure SQL Database", "ConnectionStrings:FarmSql",
                "the value is not a valid ADO.NET connection string.");
        }
    }

    /// <summary>Server name, e.g. tcp:agristream-sql.database.windows.net,1433.</summary>
    public string DataSource { get; }

    public string Database { get; }

    public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(_connectionString) { RetryLogicProvider = RetryProvider };
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
