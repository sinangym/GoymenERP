namespace GoymenERP.BuildingBlocks.Security;

public interface IErpAuthorizationService
{
    Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(
        Guid companyId,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken = default);

    Task<bool> HasPermissionAsync(
        string permission,
        Guid companyId,
        CancellationToken cancellationToken = default);

    Task<PermissionScope> GetPermissionScopeAsync(
        string permission,
        Guid companyId,
        CancellationToken cancellationToken = default);
}
