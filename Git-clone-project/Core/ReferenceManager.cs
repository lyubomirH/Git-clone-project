namespace GitClone.Core;

public sealed class ReferenceManager
{
    private readonly string _gitDirectory;
    private readonly Dictionary<string, string> _references = new();
    private string? _head;

    public ReferenceManager(string gitDirectory)
    {
        _gitDirectory = gitDirectory;
        LoadReferences();

        // Ensure HEAD exists even if no references were loaded
        EnsureHeadExists();
    }

    private void EnsureHeadExists()
    {
        var headPath = Path.Combine(_gitDirectory, "HEAD");

        // If HEAD doesn't exist, create it pointing to main branch
        if (!File.Exists(headPath))
        {
            var defaultRef = "refs/heads/main";
            File.WriteAllText(headPath, defaultRef);
            _head = defaultRef;
        }

        // Also ensure the refs/heads directory exists
        var headsDir = Path.Combine(_gitDirectory, "refs", "heads");
        if (!Directory.Exists(headsDir))
        {
            Directory.CreateDirectory(headsDir);
        }
    }

    private void LoadReferences()
    {
        // Load HEAD
        var headPath = Path.Combine(_gitDirectory, "HEAD");
        if (File.Exists(headPath))
        {
            _head = File.ReadAllText(headPath).Trim();
        }

        // Load all references
        var refsPath = Path.Combine(_gitDirectory, "refs");
        if (Directory.Exists(refsPath))
        {
            LoadReferencesRecursive(refsPath, "refs");
        }
    }

    private void LoadReferencesRecursive(string path, string prefix)
    {
        foreach (var file in Directory.GetFiles(path))
        {
            var name = $"{prefix}/{Path.GetFileName(file)}";
            var commitHash = File.ReadAllText(file).Trim();
            _references[name] = commitHash;
        }

        foreach (var dir in Directory.GetDirectories(path))
        {
            var dirName = Path.GetFileName(dir);
            LoadReferencesRecursive(dir, $"{prefix}/{dirName}");
        }
    }

    public void UpdateReference(string name, string commitHash)
    {
        _references[name] = commitHash;

        // Save to disk
        var refPath = Path.Combine(_gitDirectory, name.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(refPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(refPath, commitHash);
    }

    public void UpdateCurrentBranch(string commitHash)
    {
        if (_head is not null && _head.StartsWith("refs/heads/"))
        {
            UpdateReference(_head, commitHash);
        }
        else
        {
            // If HEAD is detached or missing, create main branch
            if (string.IsNullOrEmpty(_head) || !_head.StartsWith("refs/heads/"))
            {
                _head = "refs/heads/main";
                var headPath = Path.Combine(_gitDirectory, "HEAD");
                File.WriteAllText(headPath, _head);
                UpdateReference(_head, commitHash);
            }
        }
    }

    public string? GetCurrentCommit()
    {
        if (_head is not null && _references.TryGetValue(_head, out var hash))
        {
            return hash;
        }

        return null;
    }

    public string? GetReference(string name)
    {
        return _references.GetValueOrDefault(name);
    }

    public void SetHead(string reference)
    {
        _head = reference;
        var headPath = Path.Combine(_gitDirectory, "HEAD");
        File.WriteAllText(headPath, reference);
    }

    public string? GetHead()
    {
        // Always read HEAD from disk to ensure it's current
        var headPath = Path.Combine(_gitDirectory, "HEAD");
        if (File.Exists(headPath))
        {
            _head = File.ReadAllText(headPath).Trim();
        }
        return _head;
    }

    public IEnumerable<KeyValuePair<string, string>> GetAllReferences() => _references;
}