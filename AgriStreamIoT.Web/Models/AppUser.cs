using AgriStreamIoT.Shared.Constants;

namespace AgriStreamIoT.Web.Models;

/// <summary>A row of dbo.Users in Azure SQL.</summary>
public sealed class AppUser
{
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Farmer, Agronomist or Admin.</summary>
    public string Role { get; set; } = UserRoles.Farmer;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginUtc { get; set; }

    public string FirstName => FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? FullName;
}
