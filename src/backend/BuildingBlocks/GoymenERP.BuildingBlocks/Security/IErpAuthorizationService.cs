namespace GoymenERP.BuildingBlocks.Security;

public interface IErpAuthorizationService
{
    Task<bool> HasPermissionAsync(
        string permission,
        Guid companyId,
        CancellationToken cancellationToken = default);

    Task<PermissionScope> GetPermissionScopeAsync(
        string permission,
        Guid companyId,
        CancellationToken cancellationToken = default);
}
