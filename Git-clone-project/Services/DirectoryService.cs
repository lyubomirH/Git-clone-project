using GitClone.Models;
using GitClone.Core;

namespace GitClone.Services;

public class DirectoryService
{
    private readonly string _workingDirectory;
    private readonly GitRepository _repository;

    public DirectoryService(string workingDirectory, GitRepository repository)
    {
        _workingDirectory = workingDirectory;
        _repository = repository;
    }

    public async Task<DirectoryListing> ListDirectoryAsync(string? path = null, bool showAll = false, bool showGitStatus = true)
    {
        var targetPath = string.IsNullOrEmpty(path)
            ? _workingDirectory
            : Path.IsPathRooted(path) ? path : Path.Combine(_workingDirectory, path);

        if (!Directory.Exists(targetPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {targetPath}");
        }

        var listing = new DirectoryListing
        {
            CurrentPath = targetPath,
            RelativePath = Path.GetRelativePath(_workingDirectory, targetPath),
            ParentPath = Directory.GetParent(targetPath)?.FullName
        };

        // Get Git status for files
        RepositoryStatus? gitStatus = null;
        if (showGitStatus && _repository.IsAuthenticated())
        {
            try
            {
                gitStatus = await _repository.GetStatusAsync();
            }
            catch
            {
                // Ignore status errors
            }
        }

        // Get directories
        foreach (var dir in Directory.GetDirectories(targetPath))
        {
            var dirInfo = new DirectoryItem
            {
                Name = Path.GetFileName(dir),
                FullPath = dir,
                Type = ItemType.Directory,
                Size = 0,
                LastModified = Directory.GetLastWriteTime(dir),
                IsHidden = Path.GetFileName(dir).StartsWith(".") || dir.Contains(".gitclone")
            };

            if (!showAll && dirInfo.IsHidden)
                continue;

            listing.Items.Add(dirInfo);
        }

        // Get files
        foreach (var file in Directory.GetFiles(targetPath))
        {
            var fileInfo = new FileInfo(file);
            var fileName = fileInfo.Name;

            var item = new DirectoryItem
            {
                Name = fileName,
                FullPath = file,
                Type = ItemType.File,
                Size = fileInfo.Length,
                LastModified = fileInfo.LastWriteTime,
                IsHidden = fileName.StartsWith(".") || file.Contains(".gitclone")
            };

            if (!showAll && item.IsHidden)
                continue;

            // Get Git status if available
            if (gitStatus != null)
            {
                var relativePath = Path.GetRelativePath(_workingDirectory, file);
                if (gitStatus.Modified.Contains(relativePath))
                    item.GitStatus = GitFileStatus.Modified;
                else if (gitStatus.Added.Contains(relativePath))
                    item.GitStatus = GitFileStatus.Added;
                else if (gitStatus.Deleted.Contains(relativePath))
                    item.GitStatus = GitFileStatus.Deleted;
                else if (gitStatus.Untracked.Contains(relativePath))
                    item.GitStatus = GitFileStatus.Untracked;
                else
                    item.GitStatus = GitFileStatus.Tracked;
            }

            listing.Items.Add(item);
        }

        // Sort: directories first, then files, then alphabetically
        listing.Items = listing.Items
            .OrderByDescending(i => i.Type)
            .ThenBy(i => i.Name)
            .ToList();

        return listing;
    }

    public void DisplayDirectoryListing(DirectoryListing listing, bool showGitStatus = true, bool showDetails = false)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        if (listing.RelativePath == "")
        {
            Console.WriteLine($"Directory: /");
        }
        else
        {
            Console.WriteLine($"Directory: {listing.RelativePath}");
        }
        Console.ResetColor();
        Console.WriteLine(new string('-', 80));

        // Show parent directory
        if (listing.ParentPath != null && listing.ParentPath != _workingDirectory)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  [..]  ../");
            Console.ResetColor();
        }

        foreach (var item in listing.Items)
        {
            // Display based on type and Git status
            ConsoleColor color = ConsoleColor.White;
            string prefix = "";
            string suffix = "";

            switch (item.Type)
            {
                case ItemType.Directory:
                    color = ConsoleColor.Blue;
                    prefix = "  ";
                    suffix = "/";
                    break;
                case ItemType.File:
                    if (showGitStatus && item.GitStatus != GitFileStatus.Tracked)
                    {
                        (color, prefix) = item.GitStatus switch
                        {
                            GitFileStatus.Modified => (ConsoleColor.Yellow, "  \u001b[33m✎ "),
                            GitFileStatus.Added => (ConsoleColor.Green, "  \u001b[32m+ "),
                            GitFileStatus.Deleted => (ConsoleColor.Red, "  \u001b[31m- "),
                            GitFileStatus.Untracked => (ConsoleColor.Cyan, "  \u001b[36m? "),
                            _ => (ConsoleColor.White, "    ")
                        };
                    }
                    else
                    {
                        prefix = "    ";
                    }
                    suffix = "";
                    break;
            }

            if (showDetails && item.Type == ItemType.File)
            {
                var size = FormatFileSize(item.Size);
                var modified = item.LastModified.ToString("yyyy-MM-dd HH:mm:ss");
                Console.ForegroundColor = color;
                Console.WriteLine($"{prefix} {item.Name,-35} {size,10} {modified,20}{suffix}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = color;
                Console.WriteLine($"{prefix}{item.Name}{suffix}");
                Console.ResetColor();
            }
        }

        Console.WriteLine(new string('-', 80));
        Console.WriteLine($"Total: {listing.Items.Count} items");

        // Show Git summary
        if (showGitStatus && listing.GitSummary.HasChanges)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("\nGit: ");
            Console.ResetColor();

            if (listing.GitSummary.Modified > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"✎{listing.GitSummary.Modified} ");
                Console.ResetColor();
            }
            if (listing.GitSummary.Added > 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"+{listing.GitSummary.Added} ");
                Console.ResetColor();
            }
            if (listing.GitSummary.Deleted > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"-{listing.GitSummary.Deleted} ");
                Console.ResetColor();
            }
            if (listing.GitSummary.Untracked > 0)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"?{listing.GitSummary.Untracked} ");
                Console.ResetColor();
            }
            Console.WriteLine();
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

public class DirectoryListing
{
    public string CurrentPath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string? ParentPath { get; set; }
    public List<DirectoryItem> Items { get; set; } = new();

    public GitSummary GitSummary
    {
        get
        {
            var summary = new GitSummary();
            foreach (var item in Items)
            {
                switch (item.GitStatus)
                {
                    case GitFileStatus.Modified:
                        summary.Modified++;
                        break;
                    case GitFileStatus.Added:
                        summary.Added++;
                        break;
                    case GitFileStatus.Deleted:
                        summary.Deleted++;
                        break;
                    case GitFileStatus.Untracked:
                        summary.Untracked++;
                        break;
                }
            }
            return summary;
        }
    }
}

public class DirectoryItem
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public ItemType Type { get; set; }
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public bool IsHidden { get; set; }
    public GitFileStatus GitStatus { get; set; } = GitFileStatus.Tracked;
}

public enum ItemType
{
    File,
    Directory
}

public enum GitFileStatus
{
    Tracked,
    Modified,
    Added,
    Deleted,
    Untracked
}

public class GitSummary
{
    public int Modified { get; set; }
    public int Added { get; set; }
    public int Deleted { get; set; }
    public int Untracked { get; set; }

    public bool HasChanges => Modified > 0 || Added > 0 || Deleted > 0 || Untracked > 0;
}