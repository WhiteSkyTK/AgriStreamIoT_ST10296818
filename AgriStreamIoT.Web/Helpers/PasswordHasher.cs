using System.Security.Cryptography;
using System.Text;

namespace AgriStreamIoT.Web.Helpers;

/// <summary>
/// PBKDF2-SHA256 password hashing (100 000 iterations, 16-byte salt). Stored as "salt:hash" in
/// dbo.Users.PasswordHash; plain passwords are never stored.
/// </summary>
public static class PasswordHasher
{
    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        var parts = stored.Split(':');
        if (parts.Length != 2) return false;
        try
        {
            byte[] salt = Convert.FromBase64String(parts[0]);
            byte[] computed = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(Convert.ToBase64String(computed)),
                Encoding.UTF8.GetBytes(parts[1]));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
