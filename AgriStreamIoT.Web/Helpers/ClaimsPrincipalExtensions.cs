using System.Security.Claims;

namespace AgriStreamIoT.Web.Helpers;

/// <summary>Reads the claims written at sign-in.</summary>
public static class ClaimsPrincipalExtensions
{
    public static int? UserId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static string Email(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Email) ?? user.Identity?.Name ?? "unknown";

    public static string DisplayName(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.GivenName) ?? user.Identity?.Name ?? "Guest";

    public static string Role(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

    public static string Initials(this ClaimsPrincipal user)
    {
        var name = user.FindFirstValue(ClaimTypes.GivenName) ?? user.Identity?.Name ?? "?";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant()
        };
    }
}
