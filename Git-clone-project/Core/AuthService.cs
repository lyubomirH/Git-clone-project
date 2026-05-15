using GitClone.Models;
using System.Text;
using System.Text.Json;

namespace GitClone.Core;

public class AuthService
{
    private readonly string _authFile;
    private Dictionary<string, User> _users;
    private User? _currentUser;
    private readonly object _lock = new();

    public AuthService(string gitDirectory)
    {
        _authFile = Path.Combine(gitDirectory, "auth.json");
        _users = new Dictionary<string, User>();
        LoadUsers();
    }

    private void LoadUsers()
    {
        if (File.Exists(_authFile))
        {
            var json = File.ReadAllText(_authFile);
            var users = JsonSerializer.Deserialize<List<User>>(json);
            if (users != null)
            {
                _users = users.ToDictionary(u => u.Username, u => u);
            }
        }

        // Create default admin user if no users exist
        if (_users.Count == 0)
        {
            Console.WriteLine("\n\u001b[33mFirst time setup - Create admin account:\u001b[0m");
            Console.Write("Username (default: admin): ");
            var username = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(username)) username = "admin";

            Console.Write("Email: ");
            var email = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(email)) email = "admin@localhost";

            string? password;
            string? confirm;
            do
            {
                Console.Write("Password: ");
                password = ReadPassword();
                Console.Write("Confirm password: ");
                confirm = ReadPassword();

                if (password != confirm)
                {
                    Console.WriteLine("\u001b[31mPasswords do not match. Please try again.\u001b[0m");
                }
                else if (string.IsNullOrWhiteSpace(password))
                {
                    Console.WriteLine("\u001b[31mPassword cannot be empty.\u001b[0m");
                }
            }
            while (password != confirm || string.IsNullOrWhiteSpace(password));

            var admin = new User
            {
                Username = username,
                Email = email,
                Role = UserRole.Admin,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };
            admin.SetPassword(password!);
            _users[username] = admin;
            SaveUsers();
            Console.WriteLine($"\u001b[32m✓ Admin user '{username}' created successfully!\u001b[0m");
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

    private void SaveUsers()
    {
        lock (_lock)
        {
            var json = JsonSerializer.Serialize(_users.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_authFile, json);
        }
    }

    public bool Login(string username, string password)
    {
        if (_users.TryGetValue(username, out var user))
        {
            if (user.IsActive && user.VerifyPassword(password))
            {
                _currentUser = user;
                user.LastLogin = DateTime.UtcNow;
                SaveUsers();
                return true;
            }
        }
        return false;
    }

    public void Logout()
    {
        _currentUser = null;
    }

    public User? GetCurrentUser() => _currentUser;

    public bool IsAuthenticated() => _currentUser != null;

    public bool Register(string username, string password, string email, UserRole role = UserRole.User)
    {
        if (_users.ContainsKey(username))
            return false;

        var user = new User
        {
            Username = username,
            Email = email,
            Role = role,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            AllowedRepositories = new List<string>() // Initialize empty list
        };

        // For non-admin users, grant access to current repository
        // This will be set by the admin when creating the user
        if (role != UserRole.Admin)
        {
            // Don't add default repository - admin must grant access
            Console.WriteLine($"Created user {username} with role {role}. Admin must grant repository access.");
        }
        else
        {
            // Admin has access to all repositories
            user.GrantAccessToAllRepositories();
        }

        user.SetPassword(password);
        _users[username] = user;
        SaveUsers();
        return true;
    }

    public bool ChangePassword(string username, string oldPassword, string newPassword)
    {
        if (_users.TryGetValue(username, out var user))
        {
            if (user.VerifyPassword(oldPassword))
            {
                user.SetPassword(newPassword);
                SaveUsers();
                return true;
            }
        }
        return false;
    }

    public bool RegisterUserWithRepository(string username, string password, string email, string repositoryName, UserRole role = UserRole.User)
    {
        if (_users.ContainsKey(username))
            return false;

        var user = new User
        {
            Username = username,
            Email = email,
            Role = role,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            AllowedRepositories = new List<string> { repositoryName }
        };

        user.SetPassword(password);
        _users[username] = user;
        SaveUsers();
        return true;
    }

    public bool DeleteUser(string username)
    {
        if (_users.ContainsKey(username) && username != "admin") // Prevent deleting admin
        {
            _users.Remove(username);
            SaveUsers();
            return true;
        }
        return false;
    }

    public List<User> GetAllUsers()
    {
        return _users.Values.ToList();
    }

    public bool UpdateUserRole(string username, UserRole newRole)
    {
        if (_users.TryGetValue(username, out var user))
        {
            user.Role = newRole;
            SaveUsers();
            return true;
        }
        return false;
    }

    public bool HasPermission(Permission required)
    {
        if (!IsAuthenticated())
            return false;

        var userPermissions = PermissionHelper.GetPermissionsForRole(_currentUser!.Role);
        return PermissionHelper.HasPermission(userPermissions, required);
    }

    public bool HasRepositoryAccess(string repositoryName, Permission required)
    {
        if (!IsAuthenticated())
            return false;

        if (_currentUser == null)
            return false;

        // Debug output
        Console.WriteLine($"Checking access: User={_currentUser.Username}, Role={_currentUser.Role}, Repo={repositoryName}, Required={required}");

        // Get user's role-based permissions
        var userPermissions = PermissionHelper.GetPermissionsForRole(_currentUser.Role);

        // Admin has access to everything regardless of repository restrictions
        if (_currentUser.Role == UserRole.Admin)
        {
            Console.WriteLine($"Admin access granted for {_currentUser.Username}");
            return PermissionHelper.HasPermission(userPermissions, required);
        }

        // For non-admin users, if no repository restrictions, allow access
        if (_currentUser.AllowedRepositories == null || _currentUser.AllowedRepositories.Count == 0)
        {
            Console.WriteLine($"No repository restrictions for {_currentUser.Username}, allowing access");
            return PermissionHelper.HasPermission(userPermissions, required);
        }

        // Check if user has access to this specific repository
        bool hasRepoAccess = _currentUser.AllowedRepositories.Contains(repositoryName) ||
                             _currentUser.AllowedRepositories.Contains("*");

        if (!hasRepoAccess)
        {
            Console.WriteLine($"User {_currentUser.Username} does not have access to repository '{repositoryName}'");
            Console.WriteLine($"Allowed repositories: {string.Join(", ", _currentUser.AllowedRepositories)}");
            return false;
        }

        // Check if the user's role has the required permission
        var result = PermissionHelper.HasPermission(userPermissions, required);
        Console.WriteLine($"Permission check: Required={required}, UserPermissions={userPermissions}, Result={result}");
        return result;
    }

    public void GrantRepositoryAccess(string username, string repositoryName)
    {
        if (_users.TryGetValue(username, out var user))
        {
            if (!user.AllowedRepositories.Contains(repositoryName))
            {
                user.AllowedRepositories.Add(repositoryName);
                SaveUsers();
            }
        }
    }

    public void RevokeRepositoryAccess(string username, string repositoryName)
    {
        if (_users.TryGetValue(username, out var user))
        {
            user.AllowedRepositories.Remove(repositoryName);
            SaveUsers();
        }
    }
}