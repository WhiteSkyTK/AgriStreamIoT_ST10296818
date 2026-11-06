using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Sql;
using AgriStreamIoT.Web.Interfaces;
using AgriStreamIoT.Web.Models;
using Dapper;

namespace AgriStreamIoT.Web.Services.Data;

/// <summary>Dapper access to dbo.Users (logins with Farmer / Agronomist / Admin roles).</summary>
public sealed class UserRepository : IUserRepository
{
    private const string SelectUser = """
        SELECT UserId, FullName, Email, PhoneNumber, PasswordHash, Role, IsActive, CreatedAtUtc, LastLoginUtc
        FROM dbo.Users
        """;

    private readonly SqlConnectionFactory _database;

    public UserRepository(SqlConnectionFactory database) => _database = database;

    public async Task<AppUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AppUser>(new CommandDefinition(
            SelectUser + " WHERE Email = @Email;", new { Email = email.Trim() }, cancellationToken: cancellationToken));
    }

    public async Task<AppUser?> GetAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AppUser>(new CommandDefinition(
            SelectUser + " WHERE UserId = @UserId;", new { UserId = userId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<AppUser>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<AppUser>(new CommandDefinition(
            SelectUser + " ORDER BY CASE Role WHEN N'Admin' THEN 0 WHEN N'Agronomist' THEN 1 ELSE 2 END, FullName;",
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<int> CreateAsync(AppUser user, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT INTO dbo.Users (FullName, Email, PhoneNumber, PasswordHash, Role, IsActive)
            OUTPUT INSERTED.UserId
            VALUES (@FullName, @Email, @PhoneNumber, @PasswordHash, @Role, @IsActive);
            """,
            new
            {
                FullName = user.FullName.Trim(),
                Email = user.Email.Trim(),
                PhoneNumber = string.IsNullOrWhiteSpace(user.PhoneNumber) ? null : user.PhoneNumber.Trim(),
                user.PasswordHash,
                Role = UserRoles.Normalise(user.Role) ?? UserRoles.Farmer,
                user.IsActive
            }, cancellationToken: cancellationToken));
    }

    public async Task UpdateProfileAsync(int userId, string fullName, string? phoneNumber, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.Users SET FullName = @FullName, PhoneNumber = @PhoneNumber WHERE UserId = @UserId;",
            new { UserId = userId, FullName = fullName.Trim(), PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim() },
            cancellationToken: cancellationToken));
    }

    public async Task UpdatePasswordAsync(int userId, string passwordHash, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.Users SET PasswordHash = @PasswordHash WHERE UserId = @UserId;",
            new { UserId = userId, PasswordHash = passwordHash }, cancellationToken: cancellationToken));
    }

    public async Task SetRoleAsync(int userId, string role, CancellationToken cancellationToken = default)
    {
        var canonical = UserRoles.Normalise(role) ?? throw new ArgumentException($"Unknown role '{role}'.", nameof(role));
        await using var connection = await _database.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.Users SET Role = @Role WHERE UserId = @UserId;",
            new { UserId = userId, Role = canonical }, cancellationToken: cancellationToken));
    }

    public async Task SetActiveAsync(int userId, bool isActive, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.Users SET IsActive = @IsActive WHERE UserId = @UserId;",
            new { UserId = userId, IsActive = isActive }, cancellationToken: cancellationToken));
    }

    public async Task RecordLoginAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.Users SET LastLoginUtc = SYSUTCDATETIME() WHERE UserId = @UserId;",
            new { UserId = userId }, cancellationToken: cancellationToken));
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.Users;", cancellationToken: cancellationToken));
    }

    public async Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.Users WHERE Role = N'Admin' AND IsActive = 1;", cancellationToken: cancellationToken));
    }
}
