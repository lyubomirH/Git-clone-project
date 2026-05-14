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
                Console.WriteLine($"  ✓ Empty tree created: {emptyTree.Hash[..Math.Min(8, emptyTree.Hash.Length)]}");
            }
            else
            {
                Console.WriteLine($"  ⚠ Empty tree created with hash: {emptyTree.Hash}");
            }

            // Create initial commit
            Console.WriteLine("  Creating initial commit...");
            var initialCommit = new Commit(emptyTree.Hash, null, "system", "Initial commit", DateTime.UtcNow);
            _objectStore.StoreObject(initialCommit);

            if (!string.IsNullOrEmpty(initialCommit.Hash) && initialCommit.Hash != "empty")
            {
                Console.WriteLine($"  ✓ Initial commit created: {initialCommit.Hash[..Math.Min(8, initialCommit.Hash.Length)]}");
            }
            else
            {
                Console.WriteLine($"  ⚠ Initial commit created with hash: {initialCommit.Hash}");
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
            Console.WriteLine($"  Stack trace: {ex.StackTrace}");
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
                Console.WriteLine($"✓ Repository verified with commit: {shortHash}...");
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

    // Revert methods - FIXED VERSION
    public async Task<string> RevertCommitAsync(string commitHash, string author, string? customMessage = null)
    {
        CheckAuthorization(Permission.Commit);

        // Get the commit to revert
        var commitToRevert = await _objectStore.GetObjectAsync(commitHash) as Commit;
        if (commitToRevert == null)
        {
            throw new ArgumentException($"Commit not found: {commitHash}");
        }

        // Get the parent of the commit to revert (the state before that commit)
        Tree? parentTree = null;
        if (!string.IsNullOrEmpty(commitToRevert.ParentHash))
        {
            var parentCommit = await _objectStore.GetObjectAsync(commitToRevert.ParentHash) as Commit;
            if (parentCommit != null)
            {
                parentTree = await _objectStore.GetObjectAsync(parentCommit.TreeHash) as Tree;
            }
        }

        // Get the current tree (HEAD)
        var currentCommitHash = _referenceManager.GetCurrentCommit();
        Tree? currentTree = null;
        if (currentCommitHash != null)
        {
            var currentCommit = await _objectStore.GetObjectAsync(currentCommitHash) as Commit;
            if (currentCommit != null)
            {
                currentTree = await _objectStore.GetObjectAsync(currentCommit.TreeHash) as Tree;
            }
        }

        // Get the tree from the commit to revert (the changes we want to undo)
        var revertTargetTree = await _objectStore.GetObjectAsync(commitToRevert.TreeHash) as Tree;
        if (revertTargetTree == null)
        {
            throw new InvalidOperationException("Cannot find tree for the commit to revert");
        }

        // Create a new tree that applies the reverse changes to the current tree
        Tree revertTree;
        if (parentTree != null)
        {
            // Calculate the diff between parent and target, then apply reverse to current
            revertTree = await ApplyReverseChangesAsync(currentTree, parentTree, revertTargetTree);
        }
        else
        {
            // Initial commit - revert means remove all files added in that commit
            revertTree = await RemoveAddedFilesAsync(currentTree, revertTargetTree);
        }

        // Store the new tree
        await _objectStore.StoreObjectAsync(revertTree);

        // Create the revert commit
        var message = customMessage ?? $"Revert commit: {commitHash[..8]} - {commitToRevert.Message}";
        var revertCommit = new Commit(revertTree.Hash, currentCommitHash, author, message, DateTime.UtcNow);
        await _objectStore.StoreObjectAsync(revertCommit);

        // Update current branch
        _referenceManager.UpdateCurrentBranch(revertCommit.Hash);

        // Update working directory with reverted files
        Console.WriteLine("Updating working directory with reverted changes...");
        await RestoreTreeAsync(revertTree.Hash, _workingDirectory);

        return revertCommit.Hash;
    }

    public string RevertCommit(string commitHash, string author, string? customMessage = null)
    {
        return RevertCommitAsync(commitHash, author, customMessage).GetAwaiter().GetResult();
    }

    // Helper method to apply reverse changes
    private async Task<Tree> ApplyReverseChangesAsync(Tree? currentTree, Tree parentTree, Tree targetTree)
    {
        var entries = new List<TreeEntry>();

        // Start with current tree entries if it exists
        if (currentTree != null)
        {
            foreach (var entry in currentTree.Entries)
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

        // Get the diff between parent and target (changes made in the commit to revert)
        var parentEntries = parentTree.Entries.ToDictionary(e => e.Name);
        var targetEntries = targetTree.Entries.ToDictionary(e => e.Name);

        // For each file that changed in the commit to revert, reverse the change
        foreach (var targetEntry in targetTree.Entries)
        {
            bool existedInParent = parentEntries.ContainsKey(targetEntry.Name);

            if (!existedInParent)
            {
                // File was ADDED in the commit to revert - remove it from current
                entries.RemoveAll(e => e.Name == targetEntry.Name);
                Console.WriteLine($"  Will remove: {targetEntry.Name}");
            }
            else
            {
                var parentEntry = parentEntries[targetEntry.Name];
                if (parentEntry.Hash != targetEntry.Hash)
                {
                    // File was MODIFIED in the commit to revert - revert to parent version
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
                        Console.WriteLine($"  Will revert: {parentEntry.Name} to previous version");
                    }
                    else if (parentEntry.Type == "tree")
                    {
                        // Handle directory changes recursively
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

        // Check for files DELETED in the commit to revert - restore them
        foreach (var parentEntry in parentTree.Entries)
        {
            if (!targetEntries.ContainsKey(parentEntry.Name))
            {
                // File was DELETED in the commit to revert - restore it
                if (!entries.Any(e => e.Name == parentEntry.Name))
                {
                    entries.Add(new TreeEntry
                    {
                        Mode = parentEntry.Mode,
                        Name = parentEntry.Name,
                        Hash = parentEntry.Hash,
                        Type = parentEntry.Type
                    });
                    Console.WriteLine($"  Will restore: {parentEntry.Name}");
                }
            }
        }

        // Create the new tree
        var newTree = new Tree();
        foreach (var entry in entries.OrderBy(e => e.Name))
        {
            newTree.Entries.Add(entry);
        }
        newTree.Hash = newTree.ComputeHash();

        return newTree;
    }

    // Helper method for reverting initial commit
    private async Task<Tree> RemoveAddedFilesAsync(Tree? currentTree, Tree targetTree)
    {
        var entries = new List<TreeEntry>();

        // Start with current tree entries if it exists
        if (currentTree != null)
        {
            foreach (var entry in currentTree.Entries)
            {
                // Only keep entries that were not added in the target commit
                if (!targetTree.Entries.Any(e => e.Name == entry.Name))
                {
                    entries.Add(new TreeEntry
                    {
                        Mode = entry.Mode,
                        Name = entry.Name,
                        Hash = entry.Hash,
                        Type = entry.Type
                    });
                }
                else
                {
                    Console.WriteLine($"  Will remove: {entry.Name}");
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

                // Collect all tracked paths from the committed tree
                await CollectTreePathsAsync(currentTree, "", trackedPaths);
            }
        }

        // Check for untracked files
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
        CheckAuthorization(Permission.Read);

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

        // Get the commit and its tree
        var commit = await _objectStore.GetObjectAsync(commitHash) as Commit;
        if (commit is null)
        {
            Console.WriteLine("Error: Commit not found");
            return false;
        }

        // Clear current working directory
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
            var shortHash = commit.Hash.Length >= 8 ? commit.Hash[..8] : commit.Hash;
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