using System.Security.Cryptography;

namespace GitClone.Models;

public class User
{
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.User;
    public DateTime CreatedAt { get; set; }
    public DateTime LastLogin { get; set; }
    public bool IsActive { get; set; } = true;
    public List<string> AllowedRepositories { get; set; } = new();

    public void SetPassword(string password)
    {
        Salt = GenerateSalt();
        PasswordHash = HashPassword(password, Salt);
    }

    public bool VerifyPassword(string password)
    {
        return HashPassword(password, Salt) == PasswordHash;
    }

    private static string GenerateSalt()
    {
        var saltBytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(saltBytes);
    }

    private static string HashPassword(string password, string salt)
    {
        // Use PBKDF2 with 310,000 iterations (OWASP recommended for 2024)
        using var pbkdf2 = new Rfc2898DeriveBytes(
            password,
            Convert.FromBase64String(salt),
            310_000,
            HashAlgorithmName.SHA256
        );
        var hashBytes = pbkdf2.GetBytes(32);
        return Convert.ToBase64String(hashBytes);
    }
}

public enum UserRole
{
    Guest = 0,
    User = 1,
    Developer = 2,
    Maintainer = 3,
    Admin = 4
}