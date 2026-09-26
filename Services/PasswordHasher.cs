using System.Security.Cryptography;

namespace EduHelpdesk.Services;

// PBKDF2 password hashing with no external dependency beyond .NET itself.
public static class PasswordHasher
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private static readonly byte[] DummySalt = RandomNumberGenerator.GetBytes(SaltSize);

    // Returns "iterations.base64(salt).base64(key)".
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    // Pass null for an account that doesn't exist or has no password: the same hashing work is still done, so a wrong
    // email takes as long to refuse as a wrong password and the timing gives away nothing about who has an account.
    public static bool Verify(string? hash, string password)
    {
        if (string.IsNullOrEmpty(hash))
        {
            Rfc2898DeriveBytes.Pbkdf2(password, DummySalt, Iterations, HashAlgorithmName.SHA256, KeySize);
            return false;
        }
        var parts = hash.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations)) return false;
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expected = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException) { return false; }
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
