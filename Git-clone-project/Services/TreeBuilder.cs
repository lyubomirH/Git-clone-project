using GitClone.Models;
using GitClone.Core;

namespace GitClone.Services;

public sealed class TreeBuilder
{
    private readonly ObjectStore _objectStore;

    public TreeBuilder(ObjectStore objectStore)
    {
        _objectStore = objectStore;
    }

    public Tree CreateTreeFromDirectory(string directoryPath) =>
        CreateTreeFromDirectoryAsync(directoryPath).GetAwaiter().GetResult();

    public async Task<Tree> CreateTreeFromDirectoryAsync(string directoryPath)
    {
        var tree = new Tree();
        var entries = new List<TreeEntry>();

        // Get all files in current directory (not recursively)
        foreach (var filePath in Directory.GetFiles(directoryPath))
        {
            // Skip .gitclone directory
            if (filePath.Contains(".gitclone"))
                continue;

            var fileName = Path.GetFileName(filePath);
            var content = await File.ReadAllBytesAsync(filePath);
            var blob = new Blob(content);
            await _objectStore.StoreObjectAsync(blob);

            entries.Add(new TreeEntry
            {
                Mode = "100644",
                Name = fileName,
                Hash = blob.Hash,
                Type = "blob"
            });
        }

        // Get all subdirectories
        foreach (var dirPath in Directory.GetDirectories(directoryPath))
        {
            // Skip .gitclone directory
            if (dirPath.Contains(".gitclone"))
                continue;

            var dirName = Path.GetFileName(dirPath);
            var subTree = await CreateTreeFromDirectoryAsync(dirPath);
            await _objectStore.StoreObjectAsync(subTree);

            entries.Add(new TreeEntry
            {
                Mode = "040000",
                Name = dirName,
                Hash = subTree.Hash,
                Type = "tree"
            });
        }

        // Sort entries by name for consistent hashing
        foreach (var entry in entries.OrderBy(e => e.Name))
        {
            tree.Entries.Add(entry);
        }

        tree.Hash = tree.ComputeHash();
        return tree;
    }

    public async Task<Tree> CreateTreeFromSpecificFilesAsync(string workingDirectory, Tree? baseTree, params string[] filePaths)
    {
        // Start with entries from the base tree (previously tracked files)
        var entries = baseTree?.Entries.ToList() ?? new List<TreeEntry>();

        foreach (var filePath in filePaths)
        {
            var fullPath = Path.IsPathRooted(filePath) ? filePath : Path.Combine(workingDirectory, filePath);

            if (!File.Exists(fullPath))
            {
                Console.WriteLine($"Warning: File not found - {filePath}");
                continue;
            }

            var content = await File.ReadAllBytesAsync(fullPath);
            var blob = new Blob(content);
            await _objectStore.StoreObjectAsync(blob);

            // Replace or add the entry for this file
            entries.RemoveAll(e => e.Name == Path.GetFileName(fullPath));
            entries.Add(new TreeEntry
            {
                Mode = "100644",
                Name = Path.GetFileName(fullPath),
                Hash = blob.Hash,
                Type = "blob"
            });
        }

        var tree = new Tree();
        foreach (var entry in entries.OrderBy(e => e.Name))
        {
            tree.Entries.Add(entry);
        }
        tree.Hash = tree.ComputeHash();
        return tree;
    }
}