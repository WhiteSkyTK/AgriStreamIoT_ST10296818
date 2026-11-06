using AgriStreamIoT.Web.Models;

namespace AgriStreamIoT.Web.Interfaces;

/// <summary>Logins and roles stored in Azure SQL (dbo.Users).</summary>
public interface IUserRepository
{
    Task<AppUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<AppUser?> GetAsync(int userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AppUser>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<int> CreateAsync(AppUser user, CancellationToken cancellationToken = default);
    Task UpdateProfileAsync(int userId, string fullName, string? phoneNumber, CancellationToken cancellationToken = default);
    Task UpdatePasswordAsync(int userId, string passwordHash, CancellationToken cancellationToken = default);
    Task SetRoleAsync(int userId, string role, CancellationToken cancellationToken = default);
    Task SetActiveAsync(int userId, bool isActive, CancellationToken cancellationToken = default);
    Task RecordLoginAsync(int userId, CancellationToken cancellationToken = default);
    Task<int> CountAsync(CancellationToken cancellationToken = default);
    Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken = default);
}
