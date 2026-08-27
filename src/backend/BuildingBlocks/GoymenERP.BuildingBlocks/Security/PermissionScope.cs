namespace GoymenERP.BuildingBlocks.Security;

public sealed record PermissionScope(
    bool IsGranted,
    bool IsCompanyWide,
    IReadOnlySet<Guid> SiteIds,
    IReadOnlySet<Guid> LocationIds,
    IReadOnlySet<Guid> WorkCenterIds)
{
    public static PermissionScope Denied { get; } = new(
        false,
        false,
        new HashSet<Guid>(),
        new HashSet<Guid>(),
        new HashSet<Guid>());
}
