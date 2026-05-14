using GitClone.Core;
using GitClone.Models;
using System.Text;

namespace GitClone;

internal static class Program
{
    private static GitRepository? _repository;
    private static string? _repositoryRoot;
    private static string _currentWorkingDirectory = Environment.CurrentDirectory;
    private static bool _isLoggedIn = false;
    private static UserRole _currentUserRole = UserRole.Guest;

    private static async Task Main(string[] args)
    {
        Console.Title = "Git Clone Secure CLI";
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔═══════════════════════════════════════════════╗");
        Console.WriteLine("║     Git Clone Secure Interactive CLI v2.0     ║");
        Console.WriteLine("╚═══════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();

        // Get repository path
        if (args.Length > 0)
        {
            _repositoryRoot = args[0];
        }
        else
        {
            Console.Write("Enter repository path (or press Enter for current directory): ");
            var path = Console.ReadLine();
            _repositoryRoot = string.IsNullOrWhiteSpace(path) ? Environment.CurrentDirectory : path;
        }

        // Resolve full path
        _repositoryRoot = Path.GetFullPath(_repositoryRoot);

        // Create repository directory if it doesn't exist
        if (!Directory.Exists(_repositoryRoot))
        {
            Console.WriteLine($"Creating directory: {_repositoryRoot}");
            Directory.CreateDirectory(_repositoryRoot);
        }

        try
        {
            Console.WriteLine($"Initializing repository at: {_repositoryRoot}");
            _repository = new GitRepository(_repositoryRoot);
            _currentWorkingDirectory = _repositoryRoot;
            Environment.CurrentDirectory = _repositoryRoot;
            Console.WriteLine($"\u001b[32m✓ Repository initialized successfully!\u001b[0m");

            // Show repository info after initialization
            _repository.ShowRepositoryInfo();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\u001b[31m✗ Error initializing repository: {ex.Message}\u001b[0m");
            return;
        }

        // Show login screen
        await ShowLoginScreen();

        if (_isLoggedIn)
        {
            _currentUserRole = _repository?.GetCurrentUser()?.Role ?? UserRole.Guest;
            await RunInteractiveShell();
        }
    }

    private static async Task ShowLoginScreen()
    {
        var attempts = 0;
        const int maxAttempts = 3;

        while (!_isLoggedIn && attempts < maxAttempts)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔════════════════════════════════╗");
            Console.WriteLine("║         Login Required         ║");
            Console.WriteLine("╚════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();

            Console.Write("Username: ");
            var username = Console.ReadLine();

            Console.Write("Password: ");
            var password = ReadPassword();

            Console.WriteLine();

            if (_repository!.Login(username ?? "", password ?? ""))
            {
                _isLoggedIn = true;
                _currentUserRole = _repository.GetCurrentUser()?.Role ?? UserRole.Guest;
                Console.WriteLine($"\n\u001b[32m✓ Welcome, {username}!\u001b[0m");
                Console.WriteLine($"  {_repository.GetCurrentUserInfo()}");
                await Task.Delay(1500);
            }
            else
            {
                attempts++;
                Console.WriteLine($"\n\u001b[31m✗ Invalid credentials. Attempts remaining: {maxAttempts - attempts}\u001b[0m");
                if (attempts < maxAttempts)
                {
                    Console.WriteLine("Press any key to try again...");
                    Console.ReadKey();
                }
            }
        }

        if (!_isLoggedIn)
        {
            Console.WriteLine("\n\u001b[31mToo many failed attempts. Exiting...\u001b[0m");
            Environment.Exit(1);
        }
    }

    private static string? ReadPassword()
    {
        var password = new StringBuilder();
        ConsoleKeyInfo key;

        do
        {
            key = Console.ReadKey(true);

            if (key.Key != ConsoleKey.Backspace && key.Key != ConsoleKey.Enter)
            {
                password.Append(key.KeyChar);
                Console.Write("*");
            }
            else if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password.Remove(password.Length - 1, 1);
                Console.Write("\b \b");
            }
        }
        while (key.Key != ConsoleKey.Enter);

        Console.WriteLine();
        return password.ToString();
    }

    private static (string command, string[] args) ParseCommandLine(string input)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var quoteChar = '\0';

        for (int i = 0; i < input.Length; i++)
        {
            var c = input[i];

            if (c == '"' || c == '\'')
            {
                if (!inQuotes)
                {
                    inQuotes = true;
                    quoteChar = c;
                    continue;
                }
                else if (c == quoteChar)
                {
                    inQuotes = false;
                    quoteChar = '\0';
                    continue;
                }
            }

            if (c == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        if (parts.Count == 0)
            return ("", Array.Empty<string>());

        var command = parts[0].ToLower();
        var args = parts.Skip(1).ToArray();

        return (command, args);
    }

    // Authorization helper methods
    private static bool HasRole(UserRole requiredRole)
    {
        return _currentUserRole >= requiredRole;
    }

    private static bool CanUseCommand(string command)
    {
        return GetRequiredRoleForCommand(command) <= _currentUserRole;
    }

    private static UserRole GetRequiredRoleForCommand(string command)
    {
        return command switch
        {
            // Admin only commands
            "users" => UserRole.Admin,
            "delete-branch" => UserRole.Maintainer,
            "db" => UserRole.Maintainer,

            // Developer/Maintainer commands
            "branch" => UserRole.Developer,
            "b" => UserRole.Developer,
            "checkout" => UserRole.Developer,
            "co" => UserRole.Developer,
            "merge" => UserRole.Developer,

            // User commands (everyone logged in)
            "commit" => UserRole.User,
            "c" => UserRole.User,
            "status" => UserRole.User,
            "s" => UserRole.User,
            "log" => UserRole.User,
            "l" => UserRole.User,
            "diff" => UserRole.User,
            "d" => UserRole.User,
            "verify" => UserRole.User,
            "v" => UserRole.User,
            "whoami" => UserRole.User,

            // Guest commands (even non-logged in users)
            "dir" => UserRole.Guest,
            "ls" => UserRole.Guest,
            "cd" => UserRole.Guest,
            "pwd" => UserRole.Guest,
            "help" => UserRole.Guest,
            "h" => UserRole.Guest,
            "?" => UserRole.Guest,
            "info" => UserRole.Guest,
            "repo-info" => UserRole.Guest,
            "logout" => UserRole.User,
            "exit" => UserRole.Guest,
            "quit" => UserRole.Guest,
            "q" => UserRole.Guest,

            _ => UserRole.Guest
        };
    }

    private static void PrintUnauthorized(string command)
    {
        var requiredRole = GetRequiredRoleForCommand(command);
        Console.WriteLine($"\u001b[31m⛔ Unauthorized: '{command}' requires {requiredRole} role or higher.\u001b[0m");
        Console.WriteLine($"  Current role: {_currentUserRole}");
    }

    private static async Task RunInteractiveShell()
    {
        var exit = false;

        while (!exit)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            var branch = _repository?.GetCurrentBranch() ?? "detached";
            var user = _repository?.GetCurrentUser()?.Username ?? "unknown";

            // Show role indicator
            string roleIndicator = _currentUserRole switch
            {
                UserRole.Admin => "👑",
                UserRole.Maintainer => "⭐",
                UserRole.Developer => "🔧",
                UserRole.User => "👤",
                _ => "👁️"
            };

            // Show current directory (relative to repository root if possible)
            string displayDir;
            if (_currentWorkingDirectory == _repositoryRoot)
            {
                displayDir = "/";
            }
            else if (_currentWorkingDirectory.StartsWith(_repositoryRoot!))
            {
                displayDir = "~/" + Path.GetRelativePath(_repositoryRoot!, _currentWorkingDirectory);
            }
            else
            {
                displayDir = _currentWorkingDirectory;
                if (displayDir.Length > 40)
                {
                    var parts = displayDir.Split(Path.DirectorySeparatorChar);
                    displayDir = "..." + Path.DirectorySeparatorChar + string.Join(Path.DirectorySeparatorChar.ToString(), parts.TakeLast(3));
                }
            }

            Console.Write($"{roleIndicator}[{user}@{branch} {displayDir}] ");
            Console.ResetColor();
            Console.Write("> ");

            var rawInput = Console.ReadLine()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(rawInput))
                continue;

            var (command, args) = ParseCommandLine(rawInput);

            // Check authorization before executing command
            if (!CanUseCommand(command))
            {
                PrintUnauthorized(command);
                continue;
            }

            try
            {
                switch (command)
                {
                    case "commit":
                    case "c":
                        await HandleCommit(args);
                        break;

                    case "status":
                    case "s":
                        await HandleStatus();
                        break;

                    case "log":
                    case "l":
                        await HandleLog();
                        break;

                    case "diff":
                    case "d":
                        await HandleDiff(args);
                        break;

                    case "branch":
                    case "b":
                        await HandleBranch(args);
                        break;

                    case "checkout":
                    case "co":
                        await HandleCheckout(args);
                        break;

                    case "delete-branch":
                    case "db":
                        await HandleDeleteBranch(args);
                        break;

                    case "verify":
                    case "v":
                        await HandleVerify();
                        break;

                    case "users":
                    case "u":
                        await HandleUsers(args);
                        break;

                    case "dir":
                    case "ls":
                        await HandleDir(args);
                        break;

                    case "cd":
                        HandleCd(args);
                        break;

                    case "pwd":
                        HandlePwd();
                        break;

                    case "info":
                    case "repo-info":
                        _repository?.ShowRepositoryInfo();
                        break;

                    case "whoami":
                        Console.WriteLine(_repository?.GetCurrentUserInfo());
                        Console.WriteLine($"Role: {_currentUserRole}");
                        break;

                    case "logout":
                        _repository?.Logout();
                        _isLoggedIn = false;
                        _currentUserRole = UserRole.Guest;
                        Console.WriteLine("\u001b[33mLogged out successfully\u001b[0m");
                        await ShowLoginScreen();
                        if (!_isLoggedIn) exit = true;
                        break;

                    case "help":
                    case "h":
                    case "?":
                        ShowHelp();
                        break;

                    case "exit":
                    case "quit":
                    case "q":
                        exit = true;
                        break;

                    case "revert":
                    case "rv":
                        await HandleRevert(args);
                        break;

                    default:
                        Console.WriteLine($"\u001b[31mUnknown command: {command}\u001b[0m");
                        Console.WriteLine("Type 'help' for available commands");
                        break;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"\u001b[31m⛔ Authorization Error: {ex.Message}\u001b[0m");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\u001b[31m✗ Error: {ex.Message}\u001b[0m");
            }
        }

        Console.WriteLine("\n\u001b[33mGoodbye!\u001b[0m");
    }

    private static async Task HandleCommit(string[] args)
    {
        // Check if we're inside the repository
        if (!_currentWorkingDirectory.StartsWith(_repositoryRoot!))
        {
            Console.WriteLine("\u001b[31mError: You are outside the repository. Cannot commit.\u001b[0m");
            Console.WriteLine($"Repository root: {_repositoryRoot}");
            Console.WriteLine($"Current directory: {_currentWorkingDirectory}");
            return;
        }

        Console.Write("Enter commit message: ");
        var message = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(message))
        {
            Console.WriteLine("\u001b[31mCommit message cannot be empty\u001b[0m");
            return;
        }

        var author = _repository?.GetCurrentUser()?.Username ?? "unknown";

        if (args.Length > 0)
        {
            var files = args;
            Console.WriteLine($"\nCommitting specific files:");
            foreach (var file in files)
            {
                Console.WriteLine($"  - {file}");
            }

            Console.Write("\nProceed? (y/n): ");
            var confirm = Console.ReadLine()?.ToLower();

            if (confirm == "y")
            {
                try
                {
                    var commitHash = await _repository!.CommitFilesAsync(message, author, files);
                    Console.WriteLine($"\u001b[32m✓ Commit created successfully!\u001b[0m");
                    Console.WriteLine($"  Hash: {commitHash[..8]}...");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\u001b[31m✗ Error creating commit: {ex.Message}\u001b[0m");
                }
            }
        }
        else
        {
            await HandleStatus();

            Console.Write("\nCommit all changes? (y/n): ");
            var confirm = Console.ReadLine()?.ToLower();

            if (confirm == "y")
            {
                try
                {
                    var commitHash = await _repository!.CommitAsync(message, author);
                    Console.WriteLine($"\u001b[32m✓ Commit created successfully!\u001b[0m");
                    Console.WriteLine($"  Hash: {commitHash[..8]}...");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\u001b[31m✗ Error creating commit: {ex.Message}\u001b[0m");
                }
            }
        }
    }

    private static async Task HandleStatus()
    {
        // Check if we're inside the repository
        if (!_currentWorkingDirectory.StartsWith(_repositoryRoot!))
        {
            Console.WriteLine("\u001b[31mError: You are outside the repository. Cannot show status.\u001b[0m");
            Console.WriteLine($"Repository root: {_repositoryRoot}");
            Console.WriteLine($"Current directory: {_currentWorkingDirectory}");
            return;
        }

        Console.WriteLine("\n\u001b[36mRepository Status:\u001b[0m");
        Console.WriteLine(new string('-', 40));

        try
        {
            var status = await _repository!.GetStatusAsync();
            status.Display();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\u001b[31mError getting status: {ex.Message}\u001b[0m");
        }
    }

    private static async Task HandleLog()
    {
        Console.WriteLine("\n\u001b[36mCommit History:\u001b[0m");
        Console.WriteLine(new string('-', 40));

        try
        {
            await _repository!.LogAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\u001b[31mError showing log: {ex.Message}\u001b[0m");
        }
    }

    private static async Task HandleDiff(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: diff <commit1> <commit2>");
            Console.WriteLine("Example: diff abc123 def456");
            return;
        }

        try
        {
            await _repository!.DiffAsync(args[0], args[1]);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\u001b[31mError showing diff: {ex.Message}\u001b[0m");
        }
    }

    private static async Task HandleBranch(string[] args)
    {
        if (args.Length == 0)
        {
            // List all branches
            Console.WriteLine("\n\u001b[36mBranches:\u001b[0m");
            Console.WriteLine(new string('-', 40));
            try
            {
                var branches = _repository?.GetAllBranches() ?? new List<string>();
                var current = _repository?.GetCurrentBranch();

                if (branches.Count == 0)
                {
                    Console.WriteLine("  \u001b[33mNo branches found. Create one with 'branch <name>'\u001b[0m");
                }
                else
                {
                    foreach (var branch in branches)
                    {
                        if (branch == current)
                            Console.WriteLine($"* \u001b[32m{branch}\u001b[0m");
                        else
                            Console.WriteLine($"  {branch}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\u001b[31mError listing branches: {ex.Message}\u001b[0m");
            }
        }
        else if (args.Length == 1)
        {
            // Create branch
            var branchName = args[0];
            try
            {
                _repository?.CreateBranch(branchName);
                Console.WriteLine($"\u001b[32m✓ Branch '{branchName}' created\u001b[0m");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\u001b[31mError creating branch: {ex.Message}\u001b[0m");
            }
        }
        else
        {
            Console.WriteLine("Usage: branch [branch-name]");
        }
    }

    private static async Task HandleCheckout(string[] args)
    {
        if (args.Length != 1)
        {
            Console.WriteLine("Usage: checkout <branch-name>");
            return;
        }

        var branchName = args[0];
        try
        {
            if (await _repository!.CheckoutAsync(branchName))
            {
                Console.WriteLine($"\u001b[32m✓ Switched to branch '{branchName}'\u001b[0m");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\u001b[31mError checking out branch: {ex.Message}\u001b[0m");
        }
    }

    private static async Task HandleDeleteBranch(string[] args)
    {
        if (args.Length != 1)
        {
            Console.WriteLine("Usage: delete-branch <branch-name>");
            return;
        }

        var branchName = args[0];
        try
        {
            if (_repository?.DeleteBranch(branchName) == true)
            {
                Console.WriteLine($"\u001b[32m✓ Branch '{branchName}' deleted\u001b[0m");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\u001b[31mError deleting branch: {ex.Message}\u001b[0m");
        }
    }

    private static async Task HandleVerify()
    {
        Console.Write("Enter commit hash to verify (or press Enter for latest): ");
        var hash = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(hash))
        {
            var history = _repository?.GetCommitHistory();
            hash = history?.FirstOrDefault()?.Hash;
        }

        if (string.IsNullOrWhiteSpace(hash))
        {
            Console.WriteLine("\u001b[31mNo commits to verify\u001b[0m");
            return;
        }

        Console.WriteLine($"\nVerifying commit: {hash[..Math.Min(8, hash.Length)]}...");
        try
        {
            var isValid = await _repository!.VerifyIntegrityAsync(hash);

            if (isValid)
            {
                Console.WriteLine("\u001b[32m✓ Repository integrity verified!\u001b[0m");
            }
            else
            {
                Console.WriteLine("\u001b[31m✗ Repository integrity check FAILED!\u001b[0m");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\u001b[31mError verifying commit: {ex.Message}\u001b[0m");
        }
    }

    private static async Task HandleUsers(string[] args)
    {
        if (args.Length == 0)
        {
            // List users
            try
            {
                var users = _repository?.GetAllUsers();
                if (users != null && users.Any())
                {
                    Console.WriteLine("\n\u001b[36mUsers:\u001b[0m");
                    Console.WriteLine(new string('-', 80));
                    Console.WriteLine($"{"Username",-20} {"Role",-15} {"Email",-30} {"Status",-10}");
                    Console.WriteLine(new string('-', 80));
                    foreach (var user in users)
                    {
                        var status = user.IsActive ? "Active" : "Inactive";
                        var roleColor = user.Role == UserRole.Admin ? ConsoleColor.Red :
                                       user.Role == UserRole.Maintainer ? ConsoleColor.Yellow :
                                       user.Role == UserRole.Developer ? ConsoleColor.Cyan :
                                       ConsoleColor.White;

                        Console.ForegroundColor = roleColor;
                        Console.WriteLine($"{user.Username,-20} {user.Role,-15} {user.Email,-30} {status,-10}");
                        Console.ResetColor();
                    }
                }
                else
                {
                    Console.WriteLine("\n\u001b[33mNo users found\u001b[0m");
                }
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine("\u001b[31mAdmin privileges required to list users\u001b[0m");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\u001b[31mError listing users: {ex.Message}\u001b[0m");
            }
        }
        else if (args[0] == "add" && args.Length >= 4)
        {
            try
            {
                var role = UserRole.User;
                if (args.Length > 4)
                {
                    role = args[4]?.ToLower() switch
                    {
                        "admin" => UserRole.Admin,
                        "maintainer" => UserRole.Maintainer,
                        "developer" => UserRole.Developer,
                        "user" => UserRole.User,
                        _ => UserRole.User
                    };
                }

                if (_repository?.RegisterUser(args[1], args[2], args[3], role) == true)
                {
                    Console.WriteLine($"\u001b[32m✓ User '{args[1]}' created successfully\u001b[0m");
                    Console.WriteLine($"  Role: {role}");
                }
                else
                {
                    Console.WriteLine("\u001b[31mFailed to create user (username may already exist)\u001b[0m");
                }
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine("\u001b[31mAdmin privileges required to add users\u001b[0m");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\u001b[31mError creating user: {ex.Message}\u001b[0m");
            }
        }
        else if (args[0] == "delete" && args.Length == 2)
        {
            try
            {
                if (_repository?.DeleteUser(args[1]) == true)
                {
                    Console.WriteLine($"\u001b[32m✓ User '{args[1]}' deleted\u001b[0m");
                }
                else
                {
                    Console.WriteLine("\u001b[31mFailed to delete user (admin cannot be deleted or user not found)\u001b[0m");
                }
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine("\u001b[31mAdmin privileges required to delete users\u001b[0m");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\u001b[31mError deleting user: {ex.Message}\u001b[0m");
            }
        }
        else
        {
            Console.WriteLine("\u001b[36mUser Management Commands:\u001b[0m");
            Console.WriteLine("  users                    - List all users");
            Console.WriteLine("  users add <username> <password> <email> [role]");
            Console.WriteLine("  users delete <username>");
            Console.WriteLine("\nRoles: user, developer, maintainer, admin");
            Console.WriteLine("Default role: user");
        }
    }

    private static async Task HandleDir(string[] args)
    {
        var showAll = false;
        var showDetails = false;
        var path = "";

        // Parse arguments
        foreach (var arg in args)
        {
            if (arg == "/a" || arg == "-a" || arg == "--all")
                showAll = true;
            else if (arg == "/l" || arg == "-l" || arg == "--long")
                showDetails = true;
            else if (!arg.StartsWith("/") && !arg.StartsWith("-"))
                path = arg;
        }

        try
        {
            // Resolve path relative to current working directory
            string targetPath;
            if (string.IsNullOrEmpty(path))
            {
                targetPath = _currentWorkingDirectory;
            }
            else if (Path.IsPathRooted(path))
            {
                targetPath = path;
            }
            else
            {
                targetPath = Path.Combine(_currentWorkingDirectory, path);
            }

            var listing = await _repository!.ListDirectoryAsync(targetPath, showAll, true);
            _repository.DisplayDirectoryListing(listing, true, showDetails);
        }
        catch (DirectoryNotFoundException ex)
        {
            Console.WriteLine($"\u001b[31mError: {ex.Message}\u001b[0m");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"\u001b[31mAccess denied: {ex.Message}\u001b[0m");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\u001b[31mError listing directory: {ex.Message}\u001b[0m");
        }
    }

    private static async Task HandleRevert(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: revert <commit-hash> [message]");
            Console.WriteLine("Example: revert abc123 \"Reverting the last change\"");
            Console.WriteLine();
            Console.WriteLine("This will create a new commit that undoes the changes from the specified commit.");
            return;
        }

        var commitHash = args[0];
        var customMessage = args.Length > 1 ? string.Join(" ", args.Skip(1)) : null;
        var author = _repository?.GetCurrentUser()?.Username ?? "unknown";

        // Show what commit we're reverting
        var commitToRevert = await _repository!.GetObjectAsync(commitHash) as Commit;
        if (commitToRevert == null)
        {
            Console.WriteLine($"\u001b[31mError: Commit '{commitHash}' not found\u001b[0m");
            return;
        }

        Console.WriteLine($"\n\u001b[36mReverting commit:\u001b[0m");
        Console.WriteLine($"  Hash: {commitToRevert.Hash}");
        Console.WriteLine($"  Message: {commitToRevert.Message}");
        Console.WriteLine($"  Author: {commitToRevert.Author}");
        Console.WriteLine($"  Date: {commitToRevert.Timestamp:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine();

        // Show what files will be affected
        Console.WriteLine("\u001b[33mThis will revert the following changes:\u001b[0m");

        // Get the current commit from history (most recent commit)
        var history = _repository.GetCommitHistory();
        var currentCommit = history.FirstOrDefault();

        if (currentCommit != null)
        {
            var currentTree = await _repository.GetObjectAsync(currentCommit.TreeHash) as Tree;
            var targetTree = await _repository.GetObjectAsync(commitToRevert.TreeHash) as Tree;

            await ShowRevertChangesAsync(currentTree, targetTree);
        }
        else
        {
            Console.WriteLine("  No previous commits found.");
        }

        Console.Write("\nProceed with revert? (y/n): ");
        var confirm = Console.ReadLine()?.ToLower();

        if (confirm != "y")
        {
            Console.WriteLine("Revert cancelled.");
            return;
        }

        try
        {
            Console.WriteLine("\nCreating revert commit...");
            var newCommitHash = await _repository.RevertCommitAsync(commitHash, author, customMessage);
            Console.WriteLine($"\u001b[32m✓ Revert commit created successfully!\u001b[0m");
            Console.WriteLine($"  New commit: {newCommitHash[..8]}...");
            Console.WriteLine($"  Working directory updated with reverted changes.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\u001b[31m✗ Error reverting commit: {ex.Message}\u001b[0m");
        }
    }

    private static async Task ShowRevertChangesAsync(Tree? currentTree, Tree? targetTree)
    {
        if (currentTree == null && targetTree == null)
        {
            Console.WriteLine("  No changes to revert.");
            return;
        }

        if (currentTree == null)
        {
            Console.WriteLine("  This will remove all files from the repository.");
            return;
        }

        if (targetTree == null)
        {
            Console.WriteLine("  This will restore all files to the repository.");
            return;
        }

        var changedFiles = new List<string>();
        var addedFiles = new List<string>();
        var deletedFiles = new List<string>();

        var currentEntries = currentTree.Entries.ToDictionary(e => e.Name);
        var targetEntries = targetTree.Entries.ToDictionary(e => e.Name);

        var allNames = currentEntries.Keys.Union(targetEntries.Keys).Distinct();

        foreach (var name in allNames)
        {
            bool hasCurrent = currentEntries.ContainsKey(name);
            bool hasTarget = targetEntries.ContainsKey(name);

            if (hasTarget && !hasCurrent)
            {
                // File was added in target commit - will be removed
                addedFiles.Add(name);
            }
            else if (!hasTarget && hasCurrent)
            {
                // File was deleted in target commit - will be restored
                deletedFiles.Add(name);
            }
            else if (hasTarget && hasCurrent)
            {
                var currentEntry = currentEntries[name];
                var targetEntry = targetEntries[name];

                if (currentEntry.Hash != targetEntry.Hash)
                {
                    if (currentEntry.Type == "blob" && targetEntry.Type == "blob")
                    {
                        changedFiles.Add(name);
                    }
                    else if (currentEntry.Type == "tree" && targetEntry.Type == "tree")
                    {
                        // Recursively check subdirectories
                        var subCurrent = await _repository!.GetObjectAsync(currentEntry.Hash) as Tree;
                        var subTarget = await _repository!.GetObjectAsync(targetEntry.Hash) as Tree;
                        if (subCurrent != null && subTarget != null)
                        {
                            var subChanges = await GetTreeDiffAsync(subCurrent, subTarget);
                            changedFiles.AddRange(subChanges.Select(f => Path.Combine(name, f)));
                        }
                    }
                }
            }
        }

        if (addedFiles.Any())
        {
            Console.WriteLine($"  \u001b[31m- Files that will be REMOVED: {addedFiles.Count}\u001b[0m");
            foreach (var file in addedFiles.Take(10))
                Console.WriteLine($"      {file}");
            if (addedFiles.Count > 10)
                Console.WriteLine($"      ... and {addedFiles.Count - 10} more");
        }

        if (deletedFiles.Any())
        {
            Console.WriteLine($"  \u001b[32m+ Files that will be RESTORED: {deletedFiles.Count}\u001b[0m");
            foreach (var file in deletedFiles.Take(10))
                Console.WriteLine($"      {file}");
            if (deletedFiles.Count > 10)
                Console.WriteLine($"      ... and {deletedFiles.Count - 10} more");
        }

        if (changedFiles.Any())
        {
            Console.WriteLine($"  \u001b[33m✎ Files that will be MODIFIED: {changedFiles.Count}\u001b[0m");
            foreach (var file in changedFiles.Take(10))
                Console.WriteLine($"      {file}");
            if (changedFiles.Count > 10)
                Console.WriteLine($"      ... and {changedFiles.Count - 10} more");
        }

        if (!addedFiles.Any() && !deletedFiles.Any() && !changedFiles.Any())
        {
            Console.WriteLine("  No changes detected - commit has already been reverted or no effect.");
        }
    }

    private static async Task<List<string>> GetTreeDiffAsync(Tree tree1, Tree tree2)
    {
        var diff = new List<string>();
        var dict1 = tree1.Entries.ToDictionary(e => e.Name);
        var dict2 = tree2.Entries.ToDictionary(e => e.Name);

        foreach (var entry in tree2.Entries)
        {
            if (!dict1.ContainsKey(entry.Name) || dict1[entry.Name].Hash != entry.Hash)
            {
                if (entry.Type == "blob")
                {
                    diff.Add(entry.Name);
                }
                else if (entry.Type == "tree")
                {
                    var subTree1 = await _repository!.GetObjectAsync(dict1.GetValueOrDefault(entry.Name)?.Hash ?? "") as Tree;
                    var subTree2 = await _repository!.GetObjectAsync(entry.Hash) as Tree;
                    if (subTree1 != null && subTree2 != null)
                    {
                        var subDiff = await GetTreeDiffAsync(subTree1, subTree2);
                        diff.AddRange(subDiff.Select(f => Path.Combine(entry.Name, f)));
                    }
                }
            }
        }

        return diff;
    }

    private static async Task CollectTreeFilesAsync(Tree tree, string prefix, List<string> files)
    {
        foreach (var entry in tree.Entries)
        {
            if (entry.Type == "blob")
            {
                files.Add(prefix + entry.Name);
            }
            else if (entry.Type == "tree")
            {
                var subTree = await _repository!.GetObjectAsync(entry.Hash) as Tree;
                if (subTree != null)
                {
                    await CollectTreeFilesAsync(subTree, prefix + entry.Name + "/", files);
                }
            }
        }
    }

    private static void HandleCd(string[] args)
    {
        // Join all args since they might be part of a quoted path
        var rawPath = string.Join(" ", args);

        // Remove surrounding quotes if present
        if (rawPath.StartsWith("\"") && rawPath.EndsWith("\""))
            rawPath = rawPath[1..^1];
        if (rawPath.StartsWith("'") && rawPath.EndsWith("'"))
            rawPath = rawPath[1..^1];

        string newPath;

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            newPath = _repositoryRoot!;
        }
        else if (rawPath == "..")
        {
            newPath = Path.GetDirectoryName(_currentWorkingDirectory) ?? _currentWorkingDirectory;
        }
        else if (rawPath == "~" || rawPath == "~/")
        {
            newPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        else if (rawPath == "-")
        {
            Console.WriteLine("\u001b[33mPrevious directory tracking not implemented yet\u001b[0m");
            return;
        }
        else if (Path.IsPathRooted(rawPath))
        {
            newPath = rawPath;
        }
        else
        {
            newPath = Path.Combine(_currentWorkingDirectory, rawPath);
        }

        try
        {
            newPath = Path.GetFullPath(newPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\u001b[31mInvalid path: {ex.Message}\u001b[0m");
            return;
        }

        if (Directory.Exists(newPath))
        {
            _currentWorkingDirectory = newPath;
            Environment.CurrentDirectory = newPath;
            Console.WriteLine($"Changed to: {newPath}");

            if (!newPath.StartsWith(_repositoryRoot!))
            {
                Console.WriteLine($"\u001b[33m⚠ You are now outside the repository: {_repositoryRoot}\u001b[0m");
            }
        }
        else
        {
            Console.WriteLine($"\u001b[31mDirectory not found: {rawPath}\u001b[0m");
            Console.WriteLine($"  Tried: {newPath}");
        }
    }

    private static void HandlePwd()
    {
        Console.WriteLine(_currentWorkingDirectory);
    }

    private static void ShowHelp()
    {
        Console.WriteLine("\n\u001b[36m╔══════════════════════════════════════════════════════════════╗\u001b[0m");
        Console.WriteLine("\u001b[36m║                    AVAILABLE COMMANDS                        ║\u001b[0m");
        Console.WriteLine("\u001b[36m╚══════════════════════════════════════════════════════════════╝\u001b[0m");
        Console.WriteLine();

        // Show commands based on user role
        Console.WriteLine($"\u001b[33mYour Role: {_currentUserRole}\u001b[0m");
        Console.WriteLine();

        // Basic Commands (User+)
        if (_currentUserRole >= UserRole.User)
        {
            Console.WriteLine("\u001b[33mBasic Commands:\u001b[0m");
            Console.WriteLine($"  {"commit, c [files...]",-35} - Commit changes (specific files optional)");
            Console.WriteLine($"  {"revert, rv <hash> [message]",-35} - Revert a previous commit");
            Console.WriteLine($"  {"status, s",-35} - Show working directory status");
            Console.WriteLine($"  {"log, l",-35} - Show commit history");
            Console.WriteLine($"  {"diff, d <c1> <c2>",-35} - Show diff between commits");
            Console.WriteLine($"  {"verify, v",-35} - Verify repository integrity");
            Console.WriteLine();
        }

        // Branch Commands (Developer+)
        if (_currentUserRole >= UserRole.Developer)
        {
            Console.WriteLine("\u001b[33mBranch Commands:\u001b[0m");
            Console.WriteLine($"  {"branch, b [name]",-35} - List or create branches");
            Console.WriteLine($"  {"checkout, co <branch>",-35} - Switch branches");
            Console.WriteLine();
        }

        // Maintainer Commands
        if (_currentUserRole >= UserRole.Maintainer)
        {
            Console.WriteLine("\u001b[33mMaintainer Commands:\u001b[0m");
            Console.WriteLine($"  {"delete-branch, db <branch>",-35} - Delete a branch");
            Console.WriteLine();
        }

        // File System Commands (Everyone)
        Console.WriteLine("\u001b[33mFile System Commands:\u001b[0m");
        Console.WriteLine($"  {"dir, ls [path] [/a] [/l]",-35} - List directory contents");
        Console.WriteLine($"      {"",-35} /a, -a, --all - Show hidden files");
        Console.WriteLine($"      {"",-35} /l, -l, --long - Show detailed view");
        Console.WriteLine($"  {"cd <path>",-35} - Change directory (supports quoted paths with spaces)");
        Console.WriteLine($"  {"pwd",-35} - Print working directory");
        Console.WriteLine();

        // Admin Commands
        if (_currentUserRole >= UserRole.Admin)
        {
            Console.WriteLine("\u001b[33mAdmin Commands:\u001b[0m");
            Console.WriteLine($"  {"users, u",-35} - Manage users");
            Console.WriteLine($"  {"users add <user> <pass> <email> [role]",-35} - Create new user");
            Console.WriteLine($"  {"users delete <username>",-35} - Delete a user");
            Console.WriteLine();
        }

        // Session Commands (Everyone)
        Console.WriteLine("\u001b[33mSession Commands:\u001b[0m");
        Console.WriteLine($"  {"whoami",-35} - Show current user info");
        Console.WriteLine($"  {"logout",-35} - Log out current user");
        Console.WriteLine($"  {"info, repo-info",-35} - Show repository information");
        Console.WriteLine($"  {"help, h, ?",-35} - Show this help");
        Console.WriteLine($"  {"exit, quit, q",-35} - Exit the application");
        Console.WriteLine();

        Console.WriteLine("\u001b[36mExamples:\u001b[0m");
        Console.WriteLine($"  \u001b[36m> dir\u001b[0m");
        Console.WriteLine($"  \u001b[36m> dir \"My Documents\" /l\u001b[0m");
        Console.WriteLine($"  \u001b[36m> cd \"Program Files\"\u001b[0m");
        Console.WriteLine($"  \u001b[36m> commit \"Fix bug\"\u001b[0m");
        Console.WriteLine($"  \u001b[36m> branch feature\u001b[0m");
        Console.WriteLine($"  \u001b[36m> checkout feature\u001b[0m");

        if (_currentUserRole >= UserRole.Admin)
        {
            Console.WriteLine($"  \u001b[36m> users add john pass123 john@example.com developer\u001b[0m");
        }

        Console.WriteLine();
        Console.WriteLine("\u001b[33mRole Indicators:\u001b[0m");
        Console.WriteLine($"  👑 - Admin (full access)");
        Console.WriteLine($"  ⭐ - Maintainer (can delete branches)");
        Console.WriteLine($"  🔧 - Developer (can create branches)");
        Console.WriteLine($"  👤 - User (can commit, view)");
        Console.WriteLine($"  👁️ - Guest (view only)");
    }
}