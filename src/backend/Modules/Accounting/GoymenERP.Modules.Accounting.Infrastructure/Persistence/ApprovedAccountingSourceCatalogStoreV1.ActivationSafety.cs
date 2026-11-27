using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class ApprovedAccountingSourceCatalogStoreV1
{
    public Task<AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>> RunActivationAsync(
        Func<bool, Task<AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>>> attempt,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default)
        => activationSafetyOwner is null
            ? Task.FromResult(new AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>(AccountingApiStatus.Unavailable, null, false, null))
            : activationSafetyOwner.RunCatalogActivationAsync(this, dbContext, auditEvents, attempt, freshFence, cancellationToken);

    private bool IsOwnedActivation => activationSafetyOwner?.IsOwnedCatalogActivation(this, dbContext, auditEvents) == true;
    private Task CatalogActivationFenceAsync(CancellationToken token) => IsOwnedActivation
        ? activationSafetyOwner!.CatalogActivationFenceAsync(this, dbContext, auditEvents, token) : Task.CompletedTask;
}
