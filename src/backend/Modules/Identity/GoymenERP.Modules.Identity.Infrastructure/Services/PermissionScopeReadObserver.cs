namespace GoymenERP.Modules.Identity.Infrastructure.Services;

/// <summary>
/// Counts the authority reads a permission question actually costs, so the price of asking for a
/// fresh measurement can be measured rather than assumed. Production uses the null observer.
/// </summary>
internal interface IPermissionScopeReadObserver
{
    void ObserveDatabaseRead(string permission, Guid companyId);
}

internal sealed class NullPermissionScopeReadObserver : IPermissionScopeReadObserver
{
    public void ObserveDatabaseRead(string permission, Guid companyId)
    {
    }
}
