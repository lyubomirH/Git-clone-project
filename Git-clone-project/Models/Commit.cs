using System.Text;

namespace GitClone.Models;

public sealed class Commit : GitObject
{
    public string TreeHash { get; set; } = string.Empty;
    public string? ParentHash { get; set; }
    public string Author { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }

    public Commit()
    {
        Type = "commit";
        Timestamp = DateTime.UtcNow;
        Hash = ComputeHash();
    }

    public Commit(string treeHash, string? parentHash, string author, string message, DateTime? timestamp = null) : this()
    {
        TreeHash = treeHash;
        ParentHash = parentHash;
        Author = author;
        Message = message;
        Timestamp = timestamp ?? DateTime.UtcNow;
        Hash = ComputeHash();
    }

    protected override byte[] GetContentBytes()
    {
        var content = new StringBuilder();
        content.AppendLine($"tree {TreeHash}");
        if (!string.IsNullOrEmpty(ParentHash))
            content.AppendLine($"parent {ParentHash}");
        content.AppendLine($"author {Author}");
        content.AppendLine($"timestamp {Timestamp:yyyy-MM-dd HH:mm:ss K}");
        content.AppendLine();
        content.Append(Message);

        return Encoding.UTF8.GetBytes(content.ToString());
    }
}