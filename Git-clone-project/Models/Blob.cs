using System.Text;

namespace GitClone.Models;

public sealed class Blob : GitObject
{
    public byte[] Data { get; set; } = Array.Empty<byte>();

    public Blob()
    {
        Type = "blob";
        Hash = ComputeHash();
    }

    public Blob(byte[] data) : this()
    {
        Data = data;
        Hash = ComputeHash();
    }

    protected override byte[] GetContentBytes() => Data;
}