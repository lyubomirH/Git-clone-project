using System.Security.Cryptography;
using System.Text;

namespace GitClone.Core;

public static class HashUtility
{
    public static string ComputeSha256Hash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLower();
    }

    public static string ComputeSha256Hash(ReadOnlySpan<byte> input)
    {
        Span<byte> hashBytes = stackalloc byte[32];
        SHA256.HashData(input, hashBytes);
        return Convert.ToHexString(hashBytes).ToLower();
    }

    public static async Task<string> ComputeFileHashAsync(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        var hashBytes = await SHA256.HashDataAsync(stream);
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

    public static bool VerifyHash(string data, string expectedHash)
    {
        var computedHash = ComputeSha256Hash(data);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedHash),
            Encoding.UTF8.GetBytes(expectedHash)
        );
    }
}