using System.Security.Cryptography;
using System.Text;

namespace GitClone.Models;

public abstract class GitObject
{
    private string? _hash;

    public string Type { get; set; } = string.Empty;

    public string Hash
    {
        get
        {
            if (string.IsNullOrEmpty(_hash))
            {
                _hash = ComputeHash();
            }
            return _hash ?? string.Empty;
        }
        set => _hash = value;
    }

    protected abstract byte[] GetContentBytes();

    public string ComputeHash()
    {
        try
        {
            var contentBytes = GetContentBytes();
            if (contentBytes == null || contentBytes.Length == 0)
            {
                // Return a placeholder for empty content
                return "empty";
            }
            var hashBytes = SHA256.HashData(contentBytes);
            return Convert.ToHexString(hashBytes).ToLower();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error computing hash: {ex.Message}");
            return "error";
        }
    }

    public static byte[] HexStringToBytes(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex == "empty" || hex == "error")
        {
            return new byte[32]; // Return empty hash for invalid hex
        }
        try
        {
            return Convert.FromHexString(hex);
        }
        catch
        {
            return new byte[32];
        }
    }

    public static string BytesToHexString(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return string.Empty;
        return Convert.ToHexString(bytes).ToLower();
    }
}