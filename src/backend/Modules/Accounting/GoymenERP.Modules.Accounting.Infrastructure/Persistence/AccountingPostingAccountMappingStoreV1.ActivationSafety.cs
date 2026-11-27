using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingPostingAccountMappingStoreV1
{
    public Task<AccountingApiResult<AccountingPostingAccountMappingViewV1>> RunActivationAsync(
        Func<bool, Task<AccountingApiResult<AccountingPostingAccountMappingViewV1>>> attempt,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default)
        => activationSafetyOwner is null
            ? Task.FromResult(new AccountingApiResult<AccountingPostingAccountMappingViewV1>(AccountingApiStatus.Unavailable, null, false, null))
            : activationSafetyOwner.RunMappingActivationAsync(this, dbContext, auditEvents, attempt, freshFence, cancellationToken);

    private bool IsOwnedActivation => activationSafetyOwner?.IsOwnedMappingActivation(this, dbContext, auditEvents) == true;
    private Task MappingActivationFenceAsync(CancellationToken token) => IsOwnedActivation
        ? activationSafetyOwner!.MappingActivationFenceAsync(this, dbContext, auditEvents, token) : Task.CompletedTask;
    internal bool IsOwnedBy(AccountingStore owner) => ReferenceEquals(activationSafetyOwner, owner)
        && owner.IsOwnedMappingActivation(this, dbContext, auditEvents);
}
