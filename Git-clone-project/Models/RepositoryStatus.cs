namespace GitClone.Models;

public class RepositoryStatus
{
    public List<string> Modified { get; set; } = new();
    public List<string> Added { get; set; } = new();
    public List<string> Deleted { get; set; } = new();
    public List<string> Untracked { get; set; } = new();

    public void Display()
    {
        if (Modified.Any())
        {
            Console.WriteLine("\n\u001b[33mModified:\u001b[0m");
            foreach (var file in Modified)
                Console.WriteLine($"  \u001b[33m✎ {file}\u001b[0m");
        }

        if (Added.Any())
        {
            Console.WriteLine("\n\u001b[32mAdded:\u001b[0m");
            foreach (var file in Added)
                Console.WriteLine($"  \u001b[32m+ {file}\u001b[0m");
        }

        if (Deleted.Any())
        {
            Console.WriteLine("\n\u001b[31mDeleted:\u001b[0m");
            foreach (var file in Deleted)
                Console.WriteLine($"  \u001b[31m- {file}\u001b[0m");
        }

        if (Untracked.Any())
        {
            Console.WriteLine("\n\u001b[36mUntracked:\u001b[0m");
            foreach (var file in Untracked)
                Console.WriteLine($"  \u001b[36m? {file}\u001b[0m");
        }

        if (!Modified.Any() && !Added.Any() && !Deleted.Any() && !Untracked.Any())
        {
            Console.WriteLine("\n\u001b[32mWorking directory clean\u001b[0m");
        }
    }

    public bool HasChanges()
    {
        return Modified.Any() || Added.Any() || Deleted.Any() || Untracked.Any();
    }

    public int TotalChanges()
    {
        return Modified.Count + Added.Count + Deleted.Count + Untracked.Count;
    }

    public override string ToString()
    {
        return $"Modified: {Modified.Count}, Added: {Added.Count}, Deleted: {Deleted.Count}, Untracked: {Untracked.Count}";
    }
}