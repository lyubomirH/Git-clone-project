using GitClone.Core;
using GitClone.Models;

namespace GitClone.Services;

public sealed class DiffService
{
    private readonly ObjectStore _objectStore;

    public DiffService(ObjectStore objectStore)
    {
        _objectStore = objectStore;
    }

    public void DiffCommits(string commitHash1, string commitHash2)
    {
        if (string.IsNullOrEmpty(commitHash1) || string.IsNullOrEmpty(commitHash2))
        {
            Console.WriteLine("Invalid commit hashes provided");
            return;
        }

        var commit1 = _objectStore.GetObject(commitHash1) as Commit;
        var commit2 = _objectStore.GetObject(commitHash2) as Commit;

        if (commit1 is null)
        {
            Console.WriteLine($"Commit not found: {commitHash1}");
            return;
        }

        if (commit2 is null)
        {
            Console.WriteLine($"Commit not found: {commitHash2}");
            return;
        }

        if (string.IsNullOrEmpty(commit1.TreeHash) || string.IsNullOrEmpty(commit2.TreeHash))
        {
            Console.WriteLine("Invalid commit trees");
            return;
        }

        var tree1 = _objectStore.GetObject(commit1.TreeHash) as Tree;
        var tree2 = _objectStore.GetObject(commit2.TreeHash) as Tree;

        if (tree1 is null)
        {
            Console.WriteLine($"Tree not found for commit: {commitHash1}");
            return;
        }

        if (tree2 is null)
        {
            Console.WriteLine($"Tree not found for commit: {commitHash2}");
            return;
        }

        Console.WriteLine($"\n\u001b[36mDiff between {commitHash1[..Math.Min(8, commitHash1.Length)]} and {commitHash2[..Math.Min(8, commitHash2.Length)]}:\u001b[0m");
        DiffTrees(tree1, tree2, "");
    }

    public async Task DiffCommitsAsync(string commitHash1, string commitHash2)
    {
        if (string.IsNullOrEmpty(commitHash1) || string.IsNullOrEmpty(commitHash2))
        {
            Console.WriteLine("Invalid commit hashes provided");
            return;
        }

        var commit1 = await _objectStore.GetObjectAsync(commitHash1) as Commit;
        var commit2 = await _objectStore.GetObjectAsync(commitHash2) as Commit;

        if (commit1 is null)
        {
            Console.WriteLine($"Commit not found: {commitHash1}");
            return;
        }

        if (commit2 is null)
        {
            Console.WriteLine($"Commit not found: {commitHash2}");
            return;
        }

        if (string.IsNullOrEmpty(commit1.TreeHash) || string.IsNullOrEmpty(commit2.TreeHash))
        {
            Console.WriteLine("Invalid commit trees");
            return;
        }

        var tree1 = await _objectStore.GetObjectAsync(commit1.TreeHash) as Tree;
        var tree2 = await _objectStore.GetObjectAsync(commit2.TreeHash) as Tree;

        if (tree1 is null)
        {
            Console.WriteLine($"Tree not found for commit: {commitHash1}");
            return;
        }

        if (tree2 is null)
        {
            Console.WriteLine($"Tree not found for commit: {commitHash2}");
            return;
        }

        Console.WriteLine($"\n\u001b[36mDiff between {commitHash1[..Math.Min(8, commitHash1.Length)]} and {commitHash2[..Math.Min(8, commitHash2.Length)]}:\u001b[0m");
        await DiffTreesAsync(tree1, tree2, "");
    }

    public async Task<RepositoryStatus> GetStatusAsync(Tree? currentTree, Tree workingTree)
    {
        var status = new RepositoryStatus();

        if (currentTree == null || currentTree.Entries == null)
        {
            // All files are new
            GetAllFileNames(workingTree, "", status.Added);
            return status;
        }

        if (workingTree == null || workingTree.Entries == null)
        {
            return status;
        }

        var currentEntries = currentTree.Entries.ToDictionary(e => e.Name);
        var workingEntries = workingTree.Entries.ToDictionary(e => e.Name);

        // Check for modified and added files
        foreach (var entry in workingTree.Entries)
        {
            if (entry == null) continue;

            if (!currentEntries.ContainsKey(entry.Name))
            {
                if (entry.Type == "blob")
                    status.Added.Add(entry.Name);
                else if (entry.Type == "tree")
                {
                    // Recursively get all files in the new directory
                    var subTree = await _objectStore.GetObjectAsync(entry.Hash) as Tree;
                    if (subTree != null)
                    {
                        GetAllFileNames(subTree, entry.Name + "/", status.Added);
                    }
                }
            }
            else if (currentEntries[entry.Name].Hash != entry.Hash)
            {
                if (entry.Type == "blob")
                {
                    status.Modified.Add(entry.Name);
                }
                else if (entry.Type == "tree")
                {
                    var subCurrent = await _objectStore.GetObjectAsync(currentEntries[entry.Name].Hash) as Tree;
                    var subWorking = await _objectStore.GetObjectAsync(entry.Hash) as Tree;
                    if (subCurrent != null && subWorking != null)
                    {
                        var subStatus = await GetStatusAsync(subCurrent, subWorking);
                        foreach (var file in subStatus.Modified)
                            status.Modified.Add(Path.Combine(entry.Name, file));
                        foreach (var file in subStatus.Added)
                            status.Added.Add(Path.Combine(entry.Name, file));
                        foreach (var file in subStatus.Deleted)
                            status.Deleted.Add(Path.Combine(entry.Name, file));
                    }
                }
            }
        }

        // Check for deleted files
        foreach (var entry in currentTree.Entries)
        {
            if (entry == null) continue;

            if (!workingEntries.ContainsKey(entry.Name))
            {
                if (entry.Type == "blob")
                {
                    status.Deleted.Add(entry.Name);
                }
                else if (entry.Type == "tree")
                {
                    // Recursively get all files in the deleted directory
                    var subTree = await _objectStore.GetObjectAsync(entry.Hash) as Tree;
                    if (subTree != null)
                    {
                        GetAllFileNames(subTree, entry.Name + "/", status.Deleted);
                    }
                }
            }
        }

        return status;
    }

    private void GetAllFileNames(Tree tree, string prefix, List<string> fileList)
    {
        if (tree == null || tree.Entries == null) return;

        foreach (var entry in tree.Entries)
        {
            if (entry == null) continue;

            if (entry.Type == "blob")
            {
                fileList.Add(prefix + entry.Name);
            }
            else if (entry.Type == "tree")
            {
                var subTree = _objectStore.GetObject(entry.Hash) as Tree;
                if (subTree != null)
                {
                    GetAllFileNames(subTree, prefix + entry.Name + "/", fileList);
                }
            }
        }
    }

    private void DiffTrees(Tree tree1, Tree tree2, string path)
    {
        var dict1 = tree1.Entries.ToDictionary(e => e.Name);
        var dict2 = tree2.Entries.ToDictionary(e => e.Name);

        foreach (var entry2 in tree2.Entries)
        {
            if (!dict1.ContainsKey(entry2.Name))
            {
                Console.WriteLine($"Added: {path}{entry2.Name}");
            }
            else if (dict1[entry2.Name].Hash != entry2.Hash)
            {
                if (entry2.Type == "blob")
                {
                    Console.WriteLine($"Modified: {path}{entry2.Name}");
                }
                else if (entry2.Type == "tree")
                {
                    var subTree1 = _objectStore.GetObject(dict1[entry2.Name].Hash) as Tree;
                    var subTree2 = _objectStore.GetObject(entry2.Hash) as Tree;
                    if (subTree1 is not null && subTree2 is not null)
                    {
                        DiffTrees(subTree1, subTree2, $"{path}{entry2.Name}/");
                    }
                }
            }
        }

        foreach (var entry1 in tree1.Entries)
        {
            if (!dict2.ContainsKey(entry1.Name))
            {
                Console.WriteLine($"Deleted: {path}{entry1.Name}");
            }
        }
    }

    private async Task DiffTreesAsync(Tree tree1, Tree tree2, string path)
    {
        var dict1 = tree1.Entries.ToDictionary(e => e.Name);
        var dict2 = tree2.Entries.ToDictionary(e => e.Name);

        foreach (var entry2 in tree2.Entries)
        {
            if (!dict1.ContainsKey(entry2.Name))
            {
                Console.WriteLine($"Added: {path}{entry2.Name}");
            }
            else if (dict1[entry2.Name].Hash != entry2.Hash)
            {
                if (entry2.Type == "blob")
                {
                    Console.WriteLine($"Modified: {path}{entry2.Name}");
                }
                else if (entry2.Type == "tree")
                {
                    var subTree1 = await _objectStore.GetObjectAsync(dict1[entry2.Name].Hash) as Tree;
                    var subTree2 = await _objectStore.GetObjectAsync(entry2.Hash) as Tree;
                    if (subTree1 is not null && subTree2 is not null)
                    {
                        await DiffTreesAsync(subTree1, subTree2, $"{path}{entry2.Name}/");
                    }
                }
            }
        }

        foreach (var entry1 in tree1.Entries)
        {
            if (!dict2.ContainsKey(entry1.Name))
            {
                Console.WriteLine($"Deleted: {path}{entry1.Name}");
            }
        }
    }
}