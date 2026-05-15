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

        // Debug output
        Console.WriteLine($"Repository Name: '{_repositoryName}'");
        Console.WriteLine($"Working Directory: '{_workingDirectory}'");

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
        try
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
            Console.WriteLine("  Creating empty tree...");
            var emptyTree = new Tree();
            _objectStore.StoreObject(emptyTree);

            if (!string.IsNullOrEmpty(emptyTree.Hash) && emptyTree.Hash != "empty")
            {
                var shortHash = emptyTree.Hash.Length >= 8 ? emptyTree.Hash[..8] : emptyTree.Hash;
                Console.WriteLine($"  ✓ Empty tree created: {shortHash}");
            }

            // Create initial commit
            Console.WriteLine("  Creating initial commit...");
            var initialCommit = new Commit(emptyTree.Hash, null, "system", "Initial commit", DateTime.UtcNow);
            _objectStore.StoreObject(initialCommit);

            if (!string.IsNullOrEmpty(initialCommit.Hash) && initialCommit.Hash != "empty")
            {
                var shortHash = initialCommit.Hash.Length >= 8 ? initialCommit.Hash[..8] : initialCommit.Hash;
                Console.WriteLine($"  ✓ Initial commit created: {shortHash}");
            }

            // Update main branch reference
            Console.WriteLine("  Creating main branch...");
            _referenceManager.UpdateReference("refs/heads/main", initialCommit.Hash);

            // Set HEAD to main branch
            Console.WriteLine("  Setting HEAD...");
            _referenceManager.SetHead("refs/heads/main");

            // Force save everything
            _objectStore.Flush();

            Console.WriteLine("  ✓ Repository initialization complete!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Error during initialization: {ex.Message}");
            throw;
        }
    }

    private void VerifyInitialization()
    {
        try
        {
            var currentCommit = _referenceManager.GetCurrentCommit();
            if (!string.IsNullOrEmpty(currentCommit))
            {
                var shortHash = currentCommit.Length >= 8 ? currentCommit[..8] : currentCommit;
                Console.WriteLine($"✓ Repository verified with commit: {shortHash}");
            }
            else
            {
                Console.WriteLine("⚠ Repository initialized but no commit found");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ Verification warning: {ex.Message}");
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
        if (!IsAuthenticated())
            return false;

        var user = GetCurrentUser();
        if (user == null) return false;

        var userPermissions = PermissionHelper.GetPermissionsForRole(user.Role);
        return PermissionHelper.HasPermission(userPermissions, required);
    }

    private bool HasRepositoryAccess(Permission required)
    {
        if (!IsAuthenticated())
            return false;

        var user = GetCurrentUser();
        if (user == null) return false;

        // Debug output
        Console.WriteLine($"Checking repository access: User={user.Username}, Role={user.Role}, Repo={_repositoryName}, Required={required}");

        // Get user's role-based permissions
        var userPermissions = PermissionHelper.GetPermissionsForRole(user.Role);

        // Admin has access to everything
        if (user.Role == UserRole.Admin)
        {
            Console.WriteLine($"Admin access granted for {user.Username}");
            return PermissionHelper.HasPermission(userPermissions, required);
        }

        // If user has no repository restrictions, allow access
        if (user.AllowedRepositories == null || user.AllowedRepositories.Count == 0)
        {
            Console.WriteLine($"No repository restrictions for {user.Username}, allowing access");
            return PermissionHelper.HasPermission(userPermissions, required);
        }

        // Check if user has access to this specific repository
        bool hasRepoAccess = user.AllowedRepositories.Contains(_repositoryName) ||
                             user.AllowedRepositories.Contains("*");

        if (!hasRepoAccess)
        {
            Console.WriteLine($"User {user.Username} does not have access to repository '{_repositoryName}'");
            Console.WriteLine($"Allowed repositories: {string.Join(", ", user.AllowedRepositories)}");
            return false;
        }

        // Check if the user's role has the required permission
        var result = PermissionHelper.HasPermission(userPermissions, required);
        Console.WriteLine($"Permission check result: {result}");
        return result;
    }


    private void CheckAuthorization(Permission required)
    {
        if (_activeToken == null || !_sessionManager.ValidateSession(_activeToken))
            throw new UnauthorizedAccessException("Session invalid or expired");

        if (!HasRepositoryAccess(required))
        {
            var user = GetCurrentUser();
            var role = user?.Role ?? UserRole.Guest;
            var userPermissions = PermissionHelper.GetPermissionsForRole(role);

            throw new UnauthorizedAccessException(
                $"Insufficient permissions. Required: {required}. " +
                $"Your role ({role}) has: {PermissionHelper.GetPermissionString(userPermissions)}");
        }
    }

    // Restore tree helper for checkout
    private async Task RestoreTreeAsync(string treeHash, string dir)
    {
        if (string.IsNullOrEmpty(treeHash))
            return;

        var tree = await _objectStore.GetObjectAsync(treeHash) as Tree;
        if (tree == null || tree.Entries == null) return;

        foreach (var entry in tree.Entries)
        {
            if (entry == null) continue;

            var fullPath = Path.Combine(dir, entry.Name);

            if (entry.Type == "blob")
            {
                var blob = await _objectStore.GetObjectAsync(entry.Hash) as Blob;
                if (blob != null && blob.Data != null)
                {
                    var fileDir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(fileDir) && !Directory.Exists(fileDir))
                        Directory.CreateDirectory(fileDir);

                    await File.WriteAllBytesAsync(fullPath, blob.Data);
                }
            }
            else if (entry.Type == "tree")
            {
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
        if (!HasPermission(Permission.Commit))
        {
            var user = GetCurrentUser();
            throw new UnauthorizedAccessException(
                $"Your role ({user?.Role}) does not have permission to commit. " +
                $"Required: {Permission.Commit}");
        }

        if (!HasRepositoryAccess(Permission.Commit))
        {
            var user = GetCurrentUser();
            throw new UnauthorizedAccessException(
                $"You don't have commit access to repository '{_repositoryName}'. " +
                $"Your role: {user?.Role}");
        }

        Console.WriteLine("Creating tree from working directory...");
        var currentTree = await _treeBuilder.CreateTreeFromDirectoryAsync(_workingDirectory);
        await _objectStore.StoreObjectAsync(currentTree);

        var parentHash = _referenceManager.GetCurrentCommit();
        var commit = new Commit(currentTree.Hash, parentHash, author, message, DateTime.UtcNow);
        await _objectStore.StoreObjectAsync(commit);

        _referenceManager.UpdateCurrentBranch(commit.Hash);

        var shortHash = commit.Hash.Length >= 8 ? commit.Hash[..8] : commit.Hash;
        Console.WriteLine($"✓ Commit created: {shortHash}...");

        return commit.Hash;
    }

    public async Task<string> CommitFilesAsync(string message, string author, params string[] filePaths)
    {
        if (!HasPermission(Permission.Commit))
        {
            var user = GetCurrentUser();
            throw new UnauthorizedAccessException(
                $"Your role ({user?.Role}) does not have permission to commit. " +
                $"Required: {Permission.Commit}");
        }

        if (!HasRepositoryAccess(Permission.Commit))
        {
            var user = GetCurrentUser();
            throw new UnauthorizedAccessException(
                $"You don't have commit access to repository '{_repositoryName}'. " +
                $"Your role: {user?.Role}");
        }

        var parentHash = _referenceManager.GetCurrentCommit();
        Tree? baseTree = null;

        if (!string.IsNullOrEmpty(parentHash))
        {
            var parentCommit = await _objectStore.GetObjectAsync(parentHash) as Commit;
            if (parentCommit != null && !string.IsNullOrEmpty(parentCommit.TreeHash))
            {
                baseTree = await _objectStore.GetObjectAsync(parentCommit.TreeHash) as Tree;
            }
        }

        var currentTree = await _treeBuilder.CreateTreeFromSpecificFilesAsync(_workingDirectory, baseTree, filePaths);
        await _objectStore.StoreObjectAsync(currentTree);

        var commit = new Commit(currentTree.Hash, parentHash, author, message, DateTime.UtcNow);
        await _objectStore.StoreObjectAsync(commit);

        _referenceManager.UpdateCurrentBranch(commit.Hash);

        var shortHash = commit.Hash.Length >= 8 ? commit.Hash[..8] : commit.Hash;
        Console.WriteLine($"✓ Commit created: {shortHash}...");

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

    // Revert methods
    private string? GetFullHash(string shortHash)
    {
        if (string.IsNullOrEmpty(shortHash))
            return null;

        if (shortHash.Length == 64)
            return shortHash;

        var history = GetCommitHistory();
        foreach (var commit in history)
        {
            if (!string.IsNullOrEmpty(commit.Hash))
            {
                if (commit.Hash.Equals(shortHash, StringComparison.OrdinalIgnoreCase) ||
                    commit.Hash.StartsWith(shortHash, StringComparison.OrdinalIgnoreCase))
                {
                    return commit.Hash;
                }
            }
        }

        return null;
    }

    public async Task<string> RevertCommitAsync(string commitHash, string author, string? customMessage = null)
    {
        if (!HasPermission(Permission.Commit))
        {
            var user = GetCurrentUser();
            throw new UnauthorizedAccessException(
                $"Your role ({user?.Role}) does not have permission to revert commits. " +
                $"Required: {Permission.Commit}");
        }

        if (string.IsNullOrEmpty(commitHash))
        {
            throw new ArgumentException("Commit hash cannot be empty");
        }

        var fullHash = GetFullHash(commitHash);
        if (string.IsNullOrEmpty(fullHash))
        {
            throw new ArgumentException($"Commit not found: {commitHash}");
        }

        var commitToRevert = await _objectStore.GetObjectAsync(fullHash) as Commit;
        if (commitToRevert == null)
        {
            throw new ArgumentException($"Commit not found: {fullHash}");
        }

        var currentCommitHash = _referenceManager.GetCurrentCommit();
        Tree? currentTree = null;

        if (!string.IsNullOrEmpty(currentCommitHash))
        {
            var currentCommit = await _objectStore.GetObjectAsync(currentCommitHash) as Commit;
            if (currentCommit != null && !string.IsNullOrEmpty(currentCommit.TreeHash))
            {
                currentTree = await _objectStore.GetObjectAsync(currentCommit.TreeHash) as Tree;
            }
        }

        Tree? parentTree = null;
        if (!string.IsNullOrEmpty(commitToRevert.ParentHash))
        {
            var parentCommit = await _objectStore.GetObjectAsync(commitToRevert.ParentHash) as Commit;
            if (parentCommit != null && !string.IsNullOrEmpty(parentCommit.TreeHash))
            {
                parentTree = await _objectStore.GetObjectAsync(parentCommit.TreeHash) as Tree;
            }
        }

        var revertTargetTree = await _objectStore.GetObjectAsync(commitToRevert.TreeHash) as Tree;
        if (revertTargetTree == null)
        {
            throw new InvalidOperationException("Cannot find tree for the commit to revert");
        }

        Tree revertTree;
        if (parentTree != null)
        {
            revertTree = await ApplyReverseChangesAsync(currentTree, parentTree, revertTargetTree);
        }
        else
        {
            revertTree = await RemoveAddedFilesAsync(currentTree, revertTargetTree);
        }

        await _objectStore.StoreObjectAsync(revertTree);

        var shortHash = fullHash.Length >= 8 ? fullHash[..8] : fullHash;
        var message = customMessage ?? $"Revert commit: {shortHash} - {commitToRevert.Message}";
        var revertCommit = new Commit(revertTree.Hash, currentCommitHash, author, message, DateTime.UtcNow);
        await _objectStore.StoreObjectAsync(revertCommit);

        _referenceManager.UpdateCurrentBranch(revertCommit.Hash);

        Console.WriteLine("Updating working directory with reverted changes...");
        await RestoreTreeAsync(revertTree.Hash, _workingDirectory);

        return revertCommit.Hash;
    }

    private async Task<Tree> ApplyReverseChangesAsync(Tree? currentTree, Tree parentTree, Tree targetTree)
    {
        var entries = new List<TreeEntry>();

        if (currentTree != null && currentTree.Entries != null)
        {
            foreach (var entry in currentTree.Entries)
            {
                if (entry != null)
                {
                    entries.Add(new TreeEntry
                    {
                        Mode = entry.Mode,
                        Name = entry.Name,
                        Hash = entry.Hash,
                        Type = entry.Type
                    });
                }
            }
        }

        var parentEntries = parentTree.Entries.ToDictionary(e => e.Name);
        var targetEntries = targetTree.Entries.ToDictionary(e => e.Name);

        foreach (var targetEntry in targetTree.Entries)
        {
            if (targetEntry == null) continue;

            bool existedInParent = parentEntries.ContainsKey(targetEntry.Name);

            if (!existedInParent)
            {
                entries.RemoveAll(e => e.Name == targetEntry.Name);
            }
            else
            {
                var parentEntry = parentEntries[targetEntry.Name];
                if (parentEntry.Hash != targetEntry.Hash)
                {
                    if (parentEntry.Type == "blob")
                    {
                        entries.RemoveAll(e => e.Name == parentEntry.Name);
                        entries.Add(new TreeEntry
                        {
                            Mode = parentEntry.Mode,
                            Name = parentEntry.Name,
                            Hash = parentEntry.Hash,
                            Type = parentEntry.Type
                        });
                    }
                    else if (parentEntry.Type == "tree")
                    {
                        var parentSubTree = await _objectStore.GetObjectAsync(parentEntry.Hash) as Tree;
                        var targetSubTree = await _objectStore.GetObjectAsync(targetEntry.Hash) as Tree;

                        if (parentSubTree != null && targetSubTree != null)
                        {
                            var revertedSubTree = await ApplyReverseChangesAsync(
                                await _objectStore.GetObjectAsync(parentEntry.Hash) as Tree,
                                parentSubTree,
                                targetSubTree
                            );
                            await _objectStore.StoreObjectAsync(revertedSubTree);

                            entries.RemoveAll(e => e.Name == parentEntry.Name);
                            entries.Add(new TreeEntry
                            {
                                Mode = parentEntry.Mode,
                                Name = parentEntry.Name,
                                Hash = revertedSubTree.Hash,
                                Type = "tree"
                            });
                        }
                    }
                }
            }
        }

        foreach (var parentEntry in parentTree.Entries)
        {
            if (parentEntry == null) continue;

            if (!targetEntries.ContainsKey(parentEntry.Name))
            {
                if (!entries.Any(e => e.Name == parentEntry.Name))
                {
                    entries.Add(new TreeEntry
                    {
                        Mode = parentEntry.Mode,
                        Name = parentEntry.Name,
                        Hash = parentEntry.Hash,
                        Type = parentEntry.Type
                    });
                }
            }
        }

        var newTree = new Tree();
        foreach (var entry in entries.OrderBy(e => e.Name))
        {
            newTree.Entries.Add(entry);
        }
        newTree.Hash = newTree.ComputeHash();

        return newTree;
    }

    private async Task<Tree> RemoveAddedFilesAsync(Tree? currentTree, Tree targetTree)
    {
        var entries = new List<TreeEntry>();

        if (currentTree != null && currentTree.Entries != null)
        {
            foreach (var entry in currentTree.Entries)
            {
                if (entry != null && !targetTree.Entries.Any(e => e.Name == entry.Name))
                {
                    entries.Add(new TreeEntry
                    {
                        Mode = entry.Mode,
                        Name = entry.Name,
                        Hash = entry.Hash,
                        Type = entry.Type
                    });
                }
            }
        }

        var newTree = new Tree();
        foreach (var entry in entries.OrderBy(e => e.Name))
        {
            newTree.Entries.Add(entry);
        }
        newTree.Hash = newTree.ComputeHash();

        return newTree;
    }

    public async Task ShowRevertChangesAsync(string commitHash)
    {
        var fullHash = GetFullHash(commitHash);
        if (string.IsNullOrEmpty(fullHash))
        {
            Console.WriteLine($"Commit not found: {commitHash}");
            return;
        }

        var commitToRevert = await _objectStore.GetObjectAsync(fullHash) as Commit;
        if (commitToRevert == null)
        {
            Console.WriteLine($"Commit not found: {fullHash}");
            return;
        }

        Tree? parentTree = null;
        if (!string.IsNullOrEmpty(commitToRevert.ParentHash))
        {
            var parentCommit = await _objectStore.GetObjectAsync(commitToRevert.ParentHash) as Commit;
            if (parentCommit != null)
            {
                parentTree = await _objectStore.GetObjectAsync(parentCommit.TreeHash) as Tree;
            }
        }

        var targetTree = await _objectStore.GetObjectAsync(commitToRevert.TreeHash) as Tree;

        Console.WriteLine($"\n\u001b[36mChanges that will be reverted:\u001b[0m");

        if (parentTree != null && targetTree != null)
        {
            var parentEntries = parentTree.Entries.ToDictionary(e => e.Name);
            var targetEntries = targetTree.Entries.ToDictionary(e => e.Name);

            foreach (var targetEntry in targetTree.Entries)
            {
                if (targetEntry == null) continue;

                bool existedInParent = parentEntries.ContainsKey(targetEntry.Name);

                if (!existedInParent)
                {
                    Console.WriteLine($"  \u001b[31m- File will be REMOVED: {targetEntry.Name}\u001b[0m");
                }
                else
                {
                    var parentEntry = parentEntries[targetEntry.Name];
                    if (parentEntry.Hash != targetEntry.Hash)
                    {
                        Console.WriteLine($"  \u001b[33m✎ File will be REVERTED: {targetEntry.Name}\u001b[0m");
                    }
                }
            }

            foreach (var parentEntry in parentTree.Entries)
            {
                if (parentEntry == null) continue;

                if (!targetEntries.ContainsKey(parentEntry.Name))
                {
                    Console.WriteLine($"  \u001b[32m+ File will be RESTORED: {parentEntry.Name}\u001b[0m");
                }
            }
        }
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

        if (!string.IsNullOrEmpty(currentCommitHash))
        {
            var currentCommit = await _objectStore.GetObjectAsync(currentCommitHash) as Commit;
            if (currentCommit != null && !string.IsNullOrEmpty(currentCommit.TreeHash))
            {
                var currentTree = await _objectStore.GetObjectAsync(currentCommit.TreeHash) as Tree;
                var workingTree = await _treeBuilder.CreateTreeFromDirectoryAsync(_workingDirectory);

                status = await _diffService.GetStatusAsync(currentTree, workingTree);
                await CollectTreePathsAsync(currentTree, "", trackedPaths);
            }
        }

        var allFiles = new List<string>();
        if (Directory.Exists(_workingDirectory))
        {
            allFiles = Directory.GetFiles(_workingDirectory, "*", SearchOption.AllDirectories)
                .Where(f => !f.Contains(".gitclone"))
                .Select(f => Path.GetRelativePath(_workingDirectory, f))
                .ToList();
        }

        status.Untracked = allFiles
            .Where(f => !trackedPaths.Contains(f) && !status.Added.Contains(f))
            .ToList();

        return status;
    }

    private async Task CollectTreePathsAsync(Tree? tree, string prefix, HashSet<string> paths)
    {
        if (tree == null || tree.Entries == null)
            return;

        foreach (var entry in tree.Entries)
        {
            if (entry == null) continue;

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

        while (!string.IsNullOrEmpty(currentHash))
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

        while (!string.IsNullOrEmpty(currentHash))
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
    public void Diff(string commitHash1, string commitHash2)
    {
        if (!HasRepositoryAccess(Permission.Read))
        {
            Console.WriteLine("Insufficient permissions to read commits");
            return;
        }

        if (string.IsNullOrEmpty(commitHash1) || string.IsNullOrEmpty(commitHash2))
        {
            Console.WriteLine("Invalid commit hashes provided");
            return;
        }

        var fullHash1 = GetFullHash(commitHash1);
        var fullHash2 = GetFullHash(commitHash2);

        if (string.IsNullOrEmpty(fullHash1))
        {
            Console.WriteLine($"Commit not found: {commitHash1}");
            return;
        }

        if (string.IsNullOrEmpty(fullHash2))
        {
            Console.WriteLine($"Commit not found: {commitHash2}");
            return;
        }

        _diffService.DiffCommits(fullHash1, fullHash2);
    }

    public async Task DiffAsync(string commitHash1, string commitHash2)
    {
        if (!HasRepositoryAccess(Permission.Read))
        {
            Console.WriteLine("Insufficient permissions to read commits");
            return;
        }

        if (string.IsNullOrEmpty(commitHash1) || string.IsNullOrEmpty(commitHash2))
        {
            Console.WriteLine("Invalid commit hashes provided");
            return;
        }

        var fullHash1 = GetFullHash(commitHash1);
        var fullHash2 = GetFullHash(commitHash2);

        if (string.IsNullOrEmpty(fullHash1))
        {
            Console.WriteLine($"Commit not found: {commitHash1}");
            return;
        }

        if (string.IsNullOrEmpty(fullHash2))
        {
            Console.WriteLine($"Commit not found: {commitHash2}");
            return;
        }

        await _diffService.DiffCommitsAsync(fullHash1, fullHash2);
    }

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
        if (!HasPermission(Permission.CreateBranch))
        {
            var user = GetCurrentUser();
            throw new UnauthorizedAccessException(
                $"Your role ({user?.Role}) does not have permission to create branches. " +
                $"Required: {Permission.CreateBranch}");
        }

        var currentCommit = _referenceManager.GetCurrentCommit();
        if (!string.IsNullOrEmpty(currentCommit))
        {
            _referenceManager.UpdateReference($"refs/heads/{branchName}", currentCommit);
            var shortHash = currentCommit.Length >= 8 ? currentCommit[..8] : currentCommit;
            Console.WriteLine($"✓ Branch '{branchName}' created pointing to {shortHash}...");
        }
        else
        {
            Console.WriteLine("Cannot create branch: No commits yet. Make your first commit first.");
        }
    }

    public async Task<bool> CheckoutAsync(string branchName)
    {
        if (!HasPermission(Permission.Read))
        {
            var user = GetCurrentUser();
            throw new UnauthorizedAccessException(
                $"Your role ({user?.Role}) does not have permission to checkout branches. " +
                $"Required: {Permission.Read}");
        }

        var branchRef = $"refs/heads/{branchName}";
        var commitHash = _referenceManager.GetReference(branchRef);

        if (string.IsNullOrEmpty(commitHash))
        {
            Console.WriteLine($"Branch '{branchName}' not found");
            return false;
        }

        var shortHash = commitHash.Length >= 8 ? commitHash[..8] : commitHash;
        Console.WriteLine($"Switching to branch '{branchName}'...");
        Console.WriteLine($"Commit: {shortHash}...");

        var commit = await _objectStore.GetObjectAsync(commitHash) as Commit;
        if (commit is null)
        {
            Console.WriteLine("Error: Commit not found");
            return false;
        }

        Console.WriteLine("Clearing working directory...");
        ClearWorkingDirectory(_workingDirectory);

        Console.WriteLine("Restoring files from commit...");
        await RestoreTreeAsync(commit.TreeHash, _workingDirectory);

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
        if (!HasPermission(Permission.DeleteBranch))
        {
            var user = GetCurrentUser();
            throw new UnauthorizedAccessException(
                $"Your role ({user?.Role}) does not have permission to delete branches. " +
                $"Required: {Permission.DeleteBranch}");
        }

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

    public bool RegisterUserWithRepository(string username, string password, string email, string repositoryName, UserRole role = UserRole.User)
    {
        if (!HasPermission(Permission.ManageUsers))
            throw new UnauthorizedAccessException("Admin privileges required");

        return _authService.RegisterUserWithRepository(username, password, email, repositoryName, role);
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
        Console.WriteLine($"\u001b[32m✓ User '{username}' granted access to repository '{repositoryName}'\u001b[0m");
    }

    public void GrantUserRepositoryAccess(string username, string repositoryName)
    {
        GrantRepositoryAccess(username, repositoryName);
    }

    // Utility methods
    public GitObject? GetObject(string hash) => _objectStore.GetObject(hash);

    public async Task<GitObject?> GetObjectAsync(string hash) => await _objectStore.GetObjectAsync(hash);

    public string GetCurrentUserInfo()
    {
        var user = GetCurrentUser();
        if (user == null) return "Not logged in";

        var permissions = PermissionHelper.GetPermissionsForRole(user.Role);
        return $"User: {user.Username} | Role: {user.Role} | Email: {user.Email} | Permissions: {PermissionHelper.GetPermissionString(permissions)}";
    }

    public void ShowMyPermissions()
    {
        if (!IsAuthenticated())
        {
            Console.WriteLine("Not logged in");
            return;
        }

        var user = GetCurrentUser();
        if (user == null) return;

        var permissions = PermissionHelper.GetPermissionsForRole(user.Role);
        Console.WriteLine($"\u001b[36mYour permissions ({user.Role}):\u001b[0m");
        Console.WriteLine($"  {PermissionHelper.GetPermissionString(permissions)}");
        Console.WriteLine($"\u001b[36mRepository access:\u001b[0m");
        if (user.AllowedRepositories.Contains("*"))
            Console.WriteLine($"  All repositories");
        else if (user.AllowedRepositories.Any())
            Console.WriteLine($"  {string.Join(", ", user.AllowedRepositories)}");
        else
            Console.WriteLine($"  No specific repository access (contact admin)");
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
            var shortHash = !string.IsNullOrEmpty(commit.Hash) && commit.Hash.Length >= 8 ? commit.Hash[..8] : commit.Hash;
            Console.WriteLine($"\u001b[33mcommit {shortHash}\u001b[0m");
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
            var shortHash = !string.IsNullOrEmpty(commit.Hash) && commit.Hash.Length >= 8 ? commit.Hash[..8] : commit.Hash;
            Console.WriteLine($"\u001b[33mcommit {shortHash}\u001b[0m");
            Console.WriteLine($"Author: {commit.Author}");
            Console.WriteLine($"Date:   {commit.Timestamp:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"\n    {commit.Message}\n");
        }
    }

    public void ShowRepositoryInfo()
    {
        Console.WriteLine("\n\u001b[36mRepository Information:\u001b[0m");
        Console.WriteLine(new string('-', 50));
        Console.WriteLine($"Repository name: {_repositoryName}");
        Console.WriteLine($"Git directory: {_gitDirectory}");
        Console.WriteLine($"Working directory: {_workingDirectory}");

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

        var currentCommit = _referenceManager.GetCurrentCommit();
        if (!string.IsNullOrEmpty(currentCommit))
        {
            var shortHash = currentCommit.Length >= 8 ? currentCommit[..8] : currentCommit;
            Console.WriteLine($"Current commit: {shortHash}...");
            var commit = _objectStore.GetObject(currentCommit) as Commit;
            if (commit != null)
            {
                Console.WriteLine($"  Message: {commit.Message}");
                var shortTreeHash = commit.TreeHash.Length >= 8 ? commit.TreeHash[..8] : commit.TreeHash;
                Console.WriteLine($"  Tree: {shortTreeHash}...");
                Console.WriteLine($"  Date: {commit.Timestamp:yyyy-MM-dd HH:mm:ss}");
            }
        }
        else
        {
            Console.WriteLine($"Current commit: \u001b[33mNone\u001b[0m");
        }

        var objectsPath = Path.Combine(_gitDirectory, "objects");
        if (Directory.Exists(objectsPath))
        {
            var objectCount = Directory.GetFiles(objectsPath, "*", SearchOption.AllDirectories).Length;
            Console.WriteLine($"Objects in store: {objectCount}");
        }
    }

    public async ValueTask DisposeAsync() => await _objectStore.DisposeAsync();
}