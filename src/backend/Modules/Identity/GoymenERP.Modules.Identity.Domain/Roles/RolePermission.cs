namespace GoymenERP.Modules.Identity.Domain.Roles;

public sealed class RolePermission
{
    private RolePermission()
    {
    }

    internal RolePermission(Guid roleId, string permissionCode)
    {
        RoleId = roleId;
        PermissionCode = permissionCode;
    }

    public Guid RoleId { get; private set; }

    public string PermissionCode { get; private set; } = string.Empty;
}
