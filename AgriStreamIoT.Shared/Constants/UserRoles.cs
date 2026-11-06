namespace AgriStreamIoT.Shared.Constants;

/// <summary>The three roles stored in the Azure SQL Users table.</summary>
public static class UserRoles
{
    public const string Farmer = "Farmer";
    public const string Agronomist = "Agronomist";
    public const string Admin = "Admin";

    public static readonly IReadOnlyList<string> All = new[] { Farmer, Agronomist, Admin };

    /// <summary>Roles a visitor may pick when registering. Admins are promoted by another admin.</summary>
    public static readonly IReadOnlyList<string> SelfRegister = new[] { Farmer, Agronomist };

    // Role groups for [Authorize(Roles = ...)] (comma = any of these roles).
    public const string CanIrrigate = Farmer + "," + Admin;
    public const string CanManageFields = Agronomist + "," + Admin;

    public static bool IsValid(string? role) =>
        role is not null && All.Contains(role, StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the canonical spelling of a role name, or null when unknown.</summary>
    public static string? Normalise(string? role) =>
        All.FirstOrDefault(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
}
