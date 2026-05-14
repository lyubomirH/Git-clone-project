using GitClone.Models;
using GitClone.Services;

namespace GitClone.Core;

public sealed class GitRepository : IAsyncDisposable
{
    private readonly string _workingDirectory;
    private readonly string _gitDirectory;
    private readonly ObjectStore _objectStore;
    private readonly ReferenceManager _referenceManager;
    private readonly TreeBuilder _treeBuilder;
    private readonly DiffService _diffService;
    private readonly VerificationService _verificationService;
    private readonly AuthService _authService;
    private readonly SessionManager _sessionManager;
    private readonly DirectoryService _directoryService;
    private readonly string _repositoryName;
    private string? _activeToken;

    public GitRepository(string workingDirectory)
    {
        _workingDirectory = workingDirectory;
        _gitDirectory = Path.Combine(workingDirectory, ".gitclone");
        _repositoryName = Path.GetFileName(workingDirectory);

        _objectStore = new ObjectStore(_gitDirectory);
        _referenceManager = new ReferenceManager(_gitDirectory);
        _treeBuilder = new TreeBuilder(_objectStore);
        _diffService = new DiffService(_objectStore);
        _verificationService = new VerificationService(_objectStore);
        _authService = new AuthService(_gitDirectory);
        _sessionManager = new SessionManager();
        _directoryService = new DirectoryService(_workingDirectory, this);

        // Check if repository is already initialized
        bool isNewRepository = !Directory.Exists(_gitDirectory) ||
                               !File.Exists(Path.Combine(_gitDirectory, "refs", "heads", "main"));

        if (isNewRepository)
        {
            Console.WriteLine("Initializing new repository...");
            InitializeRepository();
            VerifyInitialization();
        }
    }

    private void InitializeRepository()
    {
        // Create directory structure
        if (!Directory.Exists(_gitDirectory))
            Directory.CreateDirectory(_gitDirectory);

        var objectsPath = Path.Combine(_gitDirectory, "objects");
        if (!Directory.Exists(objectsPath))
            Directory.CreateDirectory(objectsPath);

        var refsHeadsPath = Path.Combine(_gitDirectory, "refs", "heads");
        if (!Directory.Exists(refsHeadsPath))
            Directory.CreateDirectory(refsHeadsPath);

        // Create empty tree
        var emptyTree = new Tree();
        _objectStore.StoreObject(emptyTree);

        // Create initial commit
        var initialCommit = new Commit(emptyTree.Hash, null, "system", "Initial commit", DateTime.UtcNow);
        _objectStore.StoreObject(initialCommit);

        // Update main branch reference
        _referenceManager.UpdateReference("refs/heads/main", initialCommit.Hash);

        // Set HEAD to main branch
        _referenceManager.SetHead("refs/heads/main");

        // Force save everything
        _objectStore.Flush();
    }

    private void VerifyInitialization()
    {
        var currentCommit = _referenceManager.GetCurrentCommit();
        if (currentCommit != null)
        {
            Console.WriteLine($"✓ Repository initialized with commit: {currentCommit[..8]}...");
        }
    }

    // Authentication methods
    public bool Login(string username, string password)
    {
        var ok = _authService.Login(username, password);
        if (ok)
        {
            _activeToken = _sessionManager.CreateSession(username);
        }
        return ok;
    }

    public void Logout()
    {
        if (_activeToken != null)
        {
            _sessionManager.DestroySession(_activeToken);
            _activeToken = null;
        }
        _authService.Logout();
    }

    public User? GetCurrentUser() => _authService.GetCurrentUser();
    public bool IsAuthenticated() => _authService.IsAuthenticated();
    public string? GetActiveToken() => _activeToken;

    // Authorization checks
    private bool HasPermission(Permission required)
    {
        return _authService.HasPermission(required);
    }

    private bool HasRepositoryAccess(Permission required)
    {
        return _authService.HasRepositoryAccess(_repositoryName, required);
    }

    private void CheckAuthorization(Permission required)
    {
        if (_activeToken == null || !_sessionManager.ValidateSession(_activeToken))
            throw new UnauthorizedAccessException("Session invalid or expired");
        if (!HasRepositoryAccess(required))
            throw new UnauthorizedAccessException($"Insufficient permissions. Required: {required}");
    }

    // Restore tree helper for checkout - THIS IS THE KEY FIX
    private async Task RestoreTreeAsync(string treeHash, string dir)
    {
        var tree = await _objectStore.GetObjectAsync(treeHash) as Tree;
        if (tree == null) return;

        foreach (var entry in tree.Entries)
        {
            var fullPath = Path.Combine(dir, entry.Name);

            if (entry.Type == "blob")
            {
                var blob = await _objectStore.GetObjectAsync(entry.Hash) as Blob;
                if (blob != null)
                {
                    // Ensure directory exists
                    var fileDir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(fileDir) && !Directory.Exists(fileDir))
                        Directory.CreateDirectory(fileDir);

                    // Write the file content
                    await File.WriteAllBytesAsync(fullPath, blob.Data);
                    Console.WriteLine($"  Restored: {entry.Name}");
                }
            }
            else if (entry.Type == "tree")
            {
                // Create directory and recurse
                if (!Directory.Exists(fullPath))
                    Directory.CreateDirectory(fullPath);
                await RestoreTreeAsync(entry.Hash, fullPath);
            }
        }
    }

    // Clear working directory before checkout
    private void ClearWorkingDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.GetFiles(dir))
        {
            // Skip .gitclone directory
            if (file.Contains(".gitclone")) continue;

            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not delete {file}: {ex.Message}");
            }
        }

        foreach (var subDir in Directory.GetDirectories(dir))
        {
            // Skip .gitclone directory
            if (subDir.Contains(".gitclone")) continue;

            try
            {
                Directory.Delete(subDir, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not delete {subDir}: {ex.Message}");
            }
        }
    }

    // Commit methods
    public async Task<string> CommitAsync(string message, string author)
    {
        CheckAuthorization(Permission.Commit);

        var currentTree = await _treeBuilder.CreateTreeFromDirectoryAsync(_workingDirectory);
        await _objectStore.StoreObjectAsync(currentTree);

        var parentHash = _referenceManager.GetCurrentCommit();
        var commit = new Commit(currentTree.Hash, parentHash, author, message, DateTime.UtcNow);
        await _objectStore.StoreObjectAsync(commit);

        _referenceManager.UpdateCurrentBranch(commit.Hash);

        return commit.Hash;
    }

    public async Task<string> CommitFilesAsync(string message, string author, params string[] filePaths)
    {
        CheckAuthorization(Permission.Commit);

        var parentHash = _referenceManager.GetCurrentCommit();
        Tree? baseTree = null;

        if (parentHash != null)
        {
            var parentCommit = await _objectStore.GetObjectAsync(parentHash) as Commit;
            if (parentCommit != null)
            {
                baseTree = await _objectStore.GetObjectAsync(parentCommit.TreeHash) as Tree;
            }
        }

        var currentTree = await _treeBuilder.CreateTreeFromSpecificFilesAsync(_workingDirectory, baseTree, filePaths);
        await _objectStore.StoreObjectAsync(currentTree);

        var commit = new Commit(currentTree.Hash, parentHash, author, message, DateTime.UtcNow);
        await _objectStore.StoreObjectAsync(commit);

        _referenceManager.UpdateCurrentBranch(commit.Hash);

        return commit.Hash;
    }

    public string Commit(string message, string author)
    {
        return CommitAsync(message, author).GetAwaiter().GetResult();
    }

    public string CommitFiles(string message, string author, params string[] filePaths)
    {
        return CommitFilesAsync(message, author, filePaths).GetAwaiter().GetResult();
    }

    // Status methods
    public async Task<RepositoryStatus> GetStatusAsync()
    {
        if (!IsAuthenticated())
            return new RepositoryStatus { Untracked = new List<string> { "Authentication required" } };

        if (!HasRepositoryAccess(Permission.Read))
            return new RepositoryStatus { Untracked = new List<string> { "Insufficient permissions" } };

        var status = new RepositoryStatus();
        var currentCommitHash = _referenceManager.GetCurrentCommit();
        var trackedPaths = new HashSet<string>();

        if (currentCommitHash != null)
        {
            var currentCommit = await _objectStore.GetObjectAsync(currentCommitHash) as Commit;
            if (currentCommit != null)
            {
                var currentTree = await _objectStore.GetObjectAsync(currentCommit.TreeHash) as Tree;
                var workingTree = await _treeBuilder.CreateTreeFromDirectoryAsync(_workingDirectory);

                status = await _diffService.GetStatusAsync(currentTree, workingTree);

                // Collect all tracked paths from the committed tree
                await CollectTreePathsAsync(currentTree, "", trackedPaths);
            }
        }

        // Check for untracked files
        var allFiles = Directory.GetFiles(_workingDirectory, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains(".gitclone"))
            .Select(f => Path.GetRelativePath(_workingDirectory, f))
            .ToList();

        status.Untracked = allFiles
            .Where(f => !trackedPaths.Contains(f) && !status.Added.Contains(f))
            .ToList();

        return status;
    }

    private async Task CollectTreePathsAsync(Tree tree, string prefix, HashSet<string> paths)
    {
        foreach (var entry in tree.Entries)
        {
            if (entry.Type == "blob")
            {
                paths.Add(prefix + entry.Name);
            }
            else if (entry.Type == "tree")
            {
                var sub = await _objectStore.GetObjectAsync(entry.Hash) as Tree;
                if (sub != null)
                    await CollectTreePathsAsync(sub, prefix + entry.Name + "/", paths);
            }
        }
    }

    // Directory methods
    public async Task<DirectoryListing> ListDirectoryAsync(string? path = null, bool showAll = false, bool showGitStatus = true)
    {
        return await _directoryService.ListDirectoryAsync(path, showAll, showGitStatus);
    }

    public void DisplayDirectoryListing(DirectoryListing listing, bool showGitStatus = true, bool showDetails = false)
    {
        _directoryService.DisplayDirectoryListing(listing, showGitStatus, showDetails);
    }

    // History methods
    public List<Commit> GetCommitHistory()
    {
        if (!HasRepositoryAccess(Permission.Read))
            return new List<Commit>();

        var history = new List<Commit>();
        var currentHash = _referenceManager.GetCurrentCommit();

        while (currentHash is not null)
        {
            var commit = _objectStore.GetObject(currentHash) as Commit;
            if (commit is null)
                break;

            history.Add(commit);
            currentHash = commit.ParentHash;
        }

        return history;
    }

    public async Task<List<Commit>> GetCommitHistoryAsync()
    {
        var history = new List<Commit>();
        var currentHash = _referenceManager.GetCurrentCommit();

        while (currentHash is not null)
        {
            var commit = await _objectStore.GetObjectAsync(currentHash) as Commit;
            if (commit is null)
                break;

            history.Add(commit);
            currentHash = commit.ParentHash;
        }

        return history;
    }

    // Diff methods
    public void Diff(string commitHash1, string commitHash2) =>
        _diffService.DiffCommits(commitHash1, commitHash2);

    public async Task DiffAsync(string commitHash1, string commitHash2) =>
        await _diffService.DiffCommitsAsync(commitHash1, commitHash2);

    // Branch methods
    public List<string> GetAllBranches()
    {
        var branches = new List<string>();
        var headsDir = Path.Combine(_gitDirectory, "refs", "heads");

        if (Directory.Exists(headsDir))
        {
            foreach (var file in Directory.GetFiles(headsDir))
            {
                branches.Add(Path.GetFileName(file));
            }
        }

        return branches;
    }

    public void CreateBranch(string branchName)
    {
        CheckAuthorization(Permission.CreateBranch);

        var currentCommit = _referenceManager.GetCurrentCommit();
        if (currentCommit is not null)
        {
            _referenceManager.UpdateReference($"refs/heads/{branchName}", currentCommit);
            Console.WriteLine($"Branch '{branchName}' created pointing to {currentCommit[..8]}...");
        }
        else
        {
            Console.WriteLine("Cannot create branch: No current commit");
        }
    }

    // FIXED: Complete checkout implementation
    public async Task<bool> CheckoutAsync(string branchName)
    {
        CheckAuthorization(Permission.Read);

        var branchRef = $"refs/heads/{branchName}";
        var commitHash = _referenceManager.GetReference(branchRef);

        if (commitHash is null)
        {
            Console.WriteLine($"Branch '{branchName}' not found");
            return false;
        }

        Console.WriteLine($"Switching to branch '{branchName}'...");
        Console.WriteLine($"Commit: {commitHash[..8]}...");

        // Get the commit and its tree
        var commit = await _objectStore.GetObjectAsync(commitHash) as Commit;
        if (commit is null)
        {
            Console.WriteLine("Error: Commit not found");
            return false;
        }

        // Clear current working directory (optional - comment out if you want to keep files)
        Console.WriteLine("Clearing working directory...");
        ClearWorkingDirectory(_workingDirectory);

        // Restore files from the commit's tree
        Console.WriteLine("Restoring files from commit...");
        await RestoreTreeAsync(commit.TreeHash, _workingDirectory);

        // Update HEAD
        _referenceManager.SetHead(branchRef);

        Console.WriteLine($"\u001b[32m✓ Switched to branch '{branchName}'\u001b[0m");
        return true;
    }

    public bool Checkout(string branchName)
    {
        return CheckoutAsync(branchName).GetAwaiter().GetResult();
    }

    public bool DeleteBranch(string branchName)
    {
        CheckAuthorization(Permission.DeleteBranch);

        if (branchName == "main")
        {
            Console.WriteLine("Cannot delete main branch");
            return false;
        }

        if (branchName == GetCurrentBranch())
        {
            Console.WriteLine("Cannot delete the currently checked-out branch.");
            return false;
        }

        var branchRef = $"refs/heads/{branchName}";
        if (_referenceManager.GetReference(branchRef) != null)
        {
            var branchPath = Path.Combine(_gitDirectory, branchRef.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(branchPath))
            {
                File.Delete(branchPath);
                Console.WriteLine($"\u001b[32m✓ Branch '{branchName}' deleted\u001b[0m");
                return true;
            }
        }

        Console.WriteLine($"Branch '{branchName}' not found");
        return false;
    }

    public string? GetCurrentBranch()
    {
        var head = _referenceManager.GetHead();
        return head?.StartsWith("refs/heads/") == true ? head[11..] : null;
    }

    // Verification methods
    public bool VerifyIntegrity(string commitHash) =>
        _verificationService.VerifyCommit(commitHash);

    public async Task<bool> VerifyIntegrityAsync(string commitHash) =>
        await _verificationService.VerifyCommitAsync(commitHash);

    // User management methods (Admin only)
    public bool RegisterUser(string username, string password, string email, UserRole role = UserRole.User)
    {
        if (!HasPermission(Permission.ManageUsers))
            throw new UnauthorizedAccessException("Admin privileges required");

        return _authService.Register(username, password, email, role);
    }

    public bool DeleteUser(string username)
    {
        if (!HasPermission(Permission.ManageUsers))
            throw new UnauthorizedAccessException("Admin privileges required");

        return _authService.DeleteUser(username);
    }

    public bool UpdateUserRole(string username, UserRole newRole)
    {
        if (!HasPermission(Permission.ManagePermissions))
            throw new UnauthorizedAccessException("Admin privileges required");

        return _authService.UpdateUserRole(username, newRole);
    }

    public List<User> GetAllUsers()
    {
        if (!HasPermission(Permission.ManageUsers))
            throw new UnauthorizedAccessException("Admin privileges required");

        return _authService.GetAllUsers();
    }

    public void GrantRepositoryAccess(string username, string repositoryName)
    {
        if (!HasPermission(Permission.ManagePermissions))
            throw new UnauthorizedAccessException("Admin privileges required");

        _authService.GrantRepositoryAccess(username, repositoryName);
    }

    // Utility methods
    public GitObject? GetObject(string hash) => _objectStore.GetObject(hash);

    public async Task<GitObject?> GetObjectAsync(string hash) => await _objectStore.GetObjectAsync(hash);

    public string GetCurrentUserInfo()
    {
        var user = GetCurrentUser();
        if (user == null) return "Not logged in";

        return $"User: {user.Username} | Role: {user.Role} | Email: {user.Email}";
    }

    public void Log()
    {
        var history = GetCommitHistory();
        if (history.Count == 0)
        {
            Console.WriteLine("\n\u001b[33mNo commits yet. Use 'commit' to create your first commit.\u001b[0m");
            return;
        }

        Console.WriteLine($"\n\u001b[36mCommit History ({history.Count} commits):\u001b[0m");
        Console.WriteLine(new string('-', 80));

        foreach (var commit in history)
        {
            Console.WriteLine($"\u001b[33mcommit {commit.Hash}\u001b[0m");
            Console.WriteLine($"Author: {commit.Author}");
            Console.WriteLine($"Date:   {commit.Timestamp:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"\n    {commit.Message}\n");
        }
    }

    public async Task LogAsync()
    {
        var history = await GetCommitHistoryAsync();
        if (history.Count == 0)
        {
            Console.WriteLine("\n\u001b[33mNo commits yet. Use 'commit' to create your first commit.\u001b[0m");
            return;
        }

        Console.WriteLine($"\n\u001b[36mCommit History ({history.Count} commits):\u001b[0m");
        Console.WriteLine(new string('-', 80));

        foreach (var commit in history)
        {
            Console.WriteLine($"\u001b[33mcommit {commit.Hash}\u001b[0m");
            Console.WriteLine($"Author: {commit.Author}");
            Console.WriteLine($"Date:   {commit.Timestamp:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"\n    {commit.Message}\n");
        }
    }

    public void ShowRepositoryInfo()
    {
        Console.WriteLine("\n\u001b[36mRepository Information:\u001b[0m");
        Console.WriteLine(new string('-', 50));
        Console.WriteLine($"Git directory: {_gitDirectory}");
        Console.WriteLine($"Working directory: {_workingDirectory}");

        // Show HEAD
        var headPath = Path.Combine(_gitDirectory, "HEAD");
        if (File.Exists(headPath))
        {
            var headContent = File.ReadAllText(headPath).Trim();
            Console.WriteLine($"HEAD: {headContent}");
        }
        else
        {
            Console.WriteLine($"HEAD: \u001b[31mNOT FOUND\u001b[0m");
        }

        // Show branches
        var branches = GetAllBranches();
        if (branches.Any())
        {
            Console.WriteLine($"Branches: {string.Join(", ", branches)}");
        }
        else
        {
            Console.WriteLine($"Branches: \u001b[33mNone\u001b[0m");
        }
        Console.WriteLine($"Current branch: {GetCurrentBranch() ?? "detached"}");

        // Show current commit
        var currentCommit = _referenceManager.GetCurrentCommit();
        if (currentCommit != null)
        {
            Console.WriteLine($"Current commit: {currentCommit[..8]}...");
            var commit = _objectStore.GetObject(currentCommit) as Commit;
            if (commit != null)
            {
                Console.WriteLine($"  Message: {commit.Message}");
                Console.WriteLine($"  Tree: {commit.TreeHash[..8]}...");
                Console.WriteLine($"  Date: {commit.Timestamp:yyyy-MM-dd HH:mm:ss}");
            }
        }
        else
        {
            Console.WriteLine($"Current commit: \u001b[33mNone\u001b[0m");
        }

        // Show object count
        var objectsPath = Path.Combine(_gitDirectory, "objects");
        if (Directory.Exists(objectsPath))
        {
            var objectCount = Directory.GetFiles(objectsPath, "*", SearchOption.AllDirectories).Length;
            Console.WriteLine($"Objects in store: {objectCount}");
        }
    }

    public async ValueTask DisposeAsync() => await _objectStore.DisposeAsync();
}