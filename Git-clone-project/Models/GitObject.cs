using System.Security.Cryptography;
using System.Text;

namespace GitClone.Models;

public abstract class GitObject
{
    public string Type { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;

    protected abstract byte[] GetContentBytes();

    public string ComputeHash()
    {
        var hashBytes = SHA256.HashData(GetContentBytes());
        return Convert.ToHexString(hashBytes).ToLower();
    }

    public static byte[] HexStringToBytes(string hex)
    {
        return Convert.FromHexString(hex);
    }

    public static string BytesToHexString(byte[] bytes)
    {
        return Convert.ToHexString(bytes).ToLower();
    }
}