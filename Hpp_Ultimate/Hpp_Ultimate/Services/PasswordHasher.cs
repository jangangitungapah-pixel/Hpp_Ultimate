using System.Security.Cryptography;

namespace Hpp_Ultimate.Services;

public enum PasswordVerificationStatus
{
    Failed,
    Success,
    SuccessRehashNeeded
}

public static class PasswordHasher
{
    private const string Prefix = "$hpp$pbkdf2-sha256$";
    private const int DefaultIterations = 100_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public static string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, DefaultIterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Prefix}{DefaultIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static PasswordVerificationStatus Verify(string password, string? storedPassword)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(storedPassword))
        {
            return PasswordVerificationStatus.Failed;
        }

        if (!storedPassword.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return storedPassword.Equals(password, StringComparison.Ordinal)
                ? PasswordVerificationStatus.SuccessRehashNeeded
                : PasswordVerificationStatus.Failed;
        }

        var parts = storedPassword.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5 || !int.TryParse(parts[2], out var iterations) || iterations <= 0)
        {
            return PasswordVerificationStatus.Failed;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[3]);
            var expected = Convert.FromBase64String(parts[4]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            var success = CryptographicOperations.FixedTimeEquals(actual, expected);
            if (!success)
            {
                return PasswordVerificationStatus.Failed;
            }

            return iterations < DefaultIterations
                ? PasswordVerificationStatus.SuccessRehashNeeded
                : PasswordVerificationStatus.Success;
        }
        catch (FormatException)
        {
            return PasswordVerificationStatus.Failed;
        }
    }
}
