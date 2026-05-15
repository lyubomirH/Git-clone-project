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

    public static string GetPermissionString(Permission permission)
    {
        var permissions = new List<string>();
        if ((permission & Permission.Read) != 0) permissions.Add("Read");
        if ((permission & Permission.Write) != 0) permissions.Add("Write");
        if ((permission & Permission.Commit) != 0) permissions.Add("Commit");
        if ((permission & Permission.CreateBranch) != 0) permissions.Add("CreateBranch");
        if ((permission & Permission.DeleteBranch) != 0) permissions.Add("DeleteBranch");
        if ((permission & Permission.Merge) != 0) permissions.Add("Merge");
        if ((permission & Permission.CreateRepository) != 0) permissions.Add("CreateRepository");
        if ((permission & Permission.DeleteRepository) != 0) permissions.Add("DeleteRepository");
        if ((permission & Permission.ManageUsers) != 0) permissions.Add("ManageUsers");
        if ((permission & Permission.ManagePermissions) != 0) permissions.Add("ManagePermissions");

        return permissions.Count > 0 ? string.Join(", ", permissions) : "None";
    }
}