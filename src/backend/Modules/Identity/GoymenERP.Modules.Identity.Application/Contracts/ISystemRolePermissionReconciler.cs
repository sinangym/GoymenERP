using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Modules.Identity.Application.Contracts;

public interface ISystemRolePermissionReconciler
{
    Task<SystemRolePermissionReconciliationResult> ReconcileAsync(
        SystemRolePermissionReconciliationPlan plan,
        CancellationToken cancellationToken = default);
}

public sealed class SystemRolePermissionReconciliationPlan
{
    public SystemRolePermissionReconciliationPlan(
        IReadOnlyCollection<SystemRolePermissionTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        if (targets.Count == 0)
        {
            throw new ArgumentException(
                "En az bir sistem rolü uzlaştırma hedefi zorunludur.",
                nameof(targets));
        }

        var targetArray = targets.ToArray();
        if (targetArray.Any(static target => target is null))
        {
            throw new ArgumentException(
                "Sistem rolü uzlaştırma hedefi boş olamaz.",
                nameof(targets));
        }

        if (targetArray.Select(static target => target.RoleId).Distinct().Count()
            != targetArray.Length)
        {
            throw new ArgumentException(
                "Aynı rol kimliği bir uzlaştırma planında tekrar edemez.",
                nameof(targets));
        }

        if (targetArray.Select(static target => target.RoleCode)
                .Distinct(StringComparer.Ordinal)
                .Count()
            != targetArray.Length)
        {
            throw new ArgumentException(
                "Aynı rol kodu bir uzlaştırma planında tekrar edemez.",
                nameof(targets));
        }

        Targets = Array.AsReadOnly(targetArray);
    }

    public IReadOnlyList<SystemRolePermissionTarget> Targets { get; }
}

public sealed class SystemRolePermissionTarget
{
    public SystemRolePermissionTarget(
        Guid roleId,
        string roleCode,
        IReadOnlyCollection<string> permissionsToEnsure)
    {
        if (roleId == Guid.Empty)
        {
            throw new ArgumentException("Rol kimliği boş olamaz.", nameof(roleId));
        }

        RoleId = roleId;
        RoleCode = ExactRequired(roleCode, 100, nameof(roleCode));

        ArgumentNullException.ThrowIfNull(permissionsToEnsure);
        if (permissionsToEnsure.Count == 0)
        {
            throw new ArgumentException(
                "En az bir yetki kodu zorunludur.",
                nameof(permissionsToEnsure));
        }

        var permissions = permissionsToEnsure
            .Select(permission => ExactRequired(permission, 160, nameof(permissionsToEnsure)))
            .ToArray();
        if (permissions.Distinct(StringComparer.Ordinal).Count() != permissions.Length)
        {
            throw new ArgumentException(
                "Aynı yetki kodu bir rol hedefinde tekrar edemez.",
                nameof(permissionsToEnsure));
        }

        var unknownPermissions = permissions
            .Where(permission => !ErpPermissions.IsKnown(permission))
            .ToArray();
        if (unknownPermissions.Length > 0)
        {
            throw new ArgumentException(
                $"Tanımsız uzlaştırma yetkisi: {string.Join(", ", unknownPermissions)}",
                nameof(permissionsToEnsure));
        }

        PermissionsToEnsure = Array.AsReadOnly(permissions);
    }

    public Guid RoleId { get; }

    public string RoleCode { get; }

    public IReadOnlyList<string> PermissionsToEnsure { get; }

    private static string ExactRequired(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Değer boş olamaz.", parameterName);
        }

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Değer başında veya sonunda boşluk içeremez.",
                parameterName);
        }

        if (value.Length > maximumLength)
        {
            throw new ArgumentException(
                $"Değer {maximumLength} karakterden uzun olamaz.",
                parameterName);
        }

        return value;
    }
}

public sealed record SystemRolePermissionReconciliationResult(
    int RolesChecked,
    int PermissionsAdded)
{
    public bool Changed => PermissionsAdded > 0;
}

public sealed class SystemRolePermissionReconciliationException(string message)
    : InvalidOperationException(message);
