using AgriStreamIoT.Shared.Sql;
using AgriStreamIoT.Web.Helpers;
using AgriStreamIoT.Web.Interfaces;
using AgriStreamIoT.Web.Models;
using Microsoft.Data.SqlClient;

namespace AgriStreamIoT.Web.Services.Seeding;

/// <summary>Creates the Azure SQL schema and, on an empty database, the demo farm and logins.</summary>
public sealed class DatabaseSeeder
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(IServiceProvider services, ILogger<DatabaseSeeder> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        var database = _services.GetRequiredService<SqlConnectionFactory>();
        await FarmDatabaseSchema.EnsureCreatedAsync(database, cancellationToken);
        _logger.LogInformation("Azure SQL schema ready on {Server}/{Database}.", database.DataSource, database.Database);
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var fields = _services.GetRequiredService<IFieldRepository>();
        var devices = _services.GetRequiredService<IDeviceRegisterRepository>();
        var users = _services.GetRequiredService<IUserRepository>();

        if (await fields.CountAsync(cancellationToken) == 0)
        {
            foreach (var field in FarmSeedData.Fields)
                await IgnoreDuplicate(() => fields.CreateAsync(field, cancellationToken));
            _logger.LogInformation("Seeded {Count} fields.", FarmSeedData.Fields.Count);
        }

        if ((await devices.GetAllAsync(cancellationToken)).Count == 0)
        {
            foreach (var device in FarmSeedData.Devices)
                await IgnoreDuplicate(() => devices.CreateAsync(device, "seed", cancellationToken));
            _logger.LogInformation("Seeded {Count} devices in the master device register.", FarmSeedData.Devices.Count);
        }

        if (await users.CountAsync(cancellationToken) == 0)
        {
            foreach (var demo in FarmSeedData.Users)
            {
                await IgnoreDuplicate(() => users.CreateAsync(new AppUser
                {
                    FullName = demo.FullName,
                    Email = demo.Email,
                    PhoneNumber = demo.Phone,
                    PasswordHash = PasswordHasher.Hash(demo.Password),
                    Role = demo.Role,
                    IsActive = true
                }, cancellationToken));
            }
            _logger.LogInformation("Seeded {Count} demo logins (one per role).", FarmSeedData.Users.Count);
        }
    }

    /// <summary>Two instances starting together may both try to seed; the loser just skips.</summary>
    private static async Task IgnoreDuplicate(Func<Task> insert)
    {
        try
        {
            await insert();
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            // Primary key / unique index violation: already there.
        }
    }
}
