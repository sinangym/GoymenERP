using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace GoymenERP.BuildingBlocks.Security;

public sealed class AuthorizationSnapshot
{
    public AuthorizationSnapshot(
        string revision,
        IReadOnlySet<Guid> allowedCompanyIds,
        IReadOnlyDictionary<string, PermissionScope> permissionScopes)
    {
        if (string.IsNullOrWhiteSpace(revision))
        {
            throw new ArgumentException(
                "Authorization snapshot revision bos olamaz.",
                nameof(revision));
        }

        ArgumentNullException.ThrowIfNull(allowedCompanyIds);
        ArgumentNullException.ThrowIfNull(permissionScopes);
        Revision = revision.Trim();
        AllowedCompanyIds = allowedCompanyIds.ToFrozenSet();
        PermissionScopes = new ReadOnlyDictionary<string, PermissionScope>(
            permissionScopes.ToDictionary(
                entry => entry.Key,
                entry => Copy(entry.Value),
                StringComparer.Ordinal));
    }

    public string Revision { get; }

    public IReadOnlySet<Guid> AllowedCompanyIds { get; }

    public IReadOnlyDictionary<string, PermissionScope> PermissionScopes { get; }

    private static PermissionScope Copy(PermissionScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return new PermissionScope(
            scope.IsGranted,
            scope.IsCompanyWide,
            scope.RoleCodes.ToFrozenSet(StringComparer.Ordinal),
            scope.SiteIds.ToFrozenSet(),
            scope.LocationIds.ToFrozenSet(),
            scope.WorkCenterIds.ToFrozenSet());
    }
}
