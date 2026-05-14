using System.Text;

namespace GitClone.Models;

public class TreeEntry
{
    public string Mode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

public sealed class Tree : GitObject
{
    public List<TreeEntry> Entries { get; set; } = new();

    public Tree()
    {
        Type = "tree";
    }

    public void AddEntry(string mode, string name, string hash, string type)
    {
        Entries.Add(new TreeEntry
        {
            Mode = mode,
            Name = name,
            Hash = hash,
            Type = type
        });
        // Do NOT recompute Hash here - caller should call ComputeHash after all entries added
    }

    protected override byte[] GetContentBytes()
    {
        using var ms = new MemoryStream();
        foreach (var entry in Entries.OrderBy(e => e.Name))
        {
            // Format: "mode name\0hash" as bytes
            var header = $"{entry.Mode} {entry.Name}\0";
            var headerBytes = Encoding.UTF8.GetBytes(header);
            var hashBytes = HexStringToBytes(entry.Hash);

            ms.Write(headerBytes);
            ms.Write(hashBytes);
        }
        return ms.ToArray();
    }
}