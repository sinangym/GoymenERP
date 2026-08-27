namespace GoymenERP.Modules.Identity.Domain.Roles;

public sealed class RoleDefinition
{
    private readonly List<RolePermission> _permissions = [];

    private RoleDefinition()
    {
    }

    public RoleDefinition(Guid id, string code, string name, bool isSystem)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Rol kimliği boş olamaz.", nameof(id));
        }

        Id = id;
        Code = Required(code, nameof(code));
        Name = Required(name, nameof(name));
        IsSystem = isSystem;
        IsActive = true;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public bool IsSystem { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public long Version { get; private set; }

    public IReadOnlyCollection<RolePermission> Permissions => _permissions.AsReadOnly();

    public void AddPermission(string permissionCode)
    {
        var normalized = Required(permissionCode, nameof(permissionCode));
        if (_permissions.Any(x => string.Equals(
                x.PermissionCode,
                normalized,
                StringComparison.Ordinal)))
        {
            return;
        }

        _permissions.Add(new RolePermission(Id, normalized));
    }

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Değer boş olamaz.", parameterName);
        }

        return value.Trim();
    }
}
