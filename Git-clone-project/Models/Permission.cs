namespace GitClone.Models;

[Flags]
public enum Permission
{
    None = 0,
    Read = 1 << 0,
    Write = 1 << 1,
    Commit = 1 << 2,
    CreateBranch = 1 << 3,
    DeleteBranch = 1 << 4,
    Merge = 1 << 5,
    CreateRepository = 1 << 6,
    DeleteRepository = 1 << 7,
    ManageUsers = 1 << 8,
    ManagePermissions = 1 << 9,
    AdminAll = Read | Write | Commit | CreateBranch | DeleteBranch | Merge | CreateRepository | DeleteRepository | ManageUsers | ManagePermissions
}

public static class PermissionHelper
{
    public static Permission GetPermissionsForRole(UserRole role)
    {
        return role switch
        {
            UserRole.Guest => Permission.Read,
            UserRole.User => Permission.Read | Permission.Write | Permission.Commit,
            UserRole.Developer => Permission.Read | Permission.Write | Permission.Commit | Permission.CreateBranch | Permission.Merge,
            UserRole.Maintainer => Permission.Read | Permission.Write | Permission.Commit | Permission.CreateBranch |
                                   Permission.DeleteBranch | Permission.Merge | Permission.CreateRepository,
            UserRole.Admin => Permission.AdminAll,
            _ => Permission.None
        };
    }

    public static bool HasPermission(Permission userPermissions, Permission required)
    {
        return (userPermissions & required) == required;
    }
}