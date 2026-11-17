namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class AccountingApiService
{
    public async Task<AccountingApiResult<AccountingPeriodLifecycleViewV1>> GetPeriodLifecycleAsync(Guid periodId,
        CancellationToken cancellationToken)
    {
        var scope = await RequireAsync(AccountingPermissionManifestV1.PeriodsRead, cancellationToken);
        if (scope is null) return Denied<AccountingPeriodLifecycleViewV1>();
        if (periods is not IAccountingPeriodReopeningStoreV1 store)
            return PeriodLifecycleUnavailable<AccountingPeriodLifecycleViewV1>();
        try
        {
            var view = await GuardedAsync(scope, AccountingPermissionManifestV1.PeriodsRead,
                () => store.ReadLifecycleAsync(periodId, cancellationToken), cancellationToken);
            return view is null ? NotFoundResult<AccountingPeriodLifecycleViewV1>()
                : new(AccountingApiStatus.Ok, view, false, null);
        }
        catch (FreshAuthorityDeniedException) { return Denied<AccountingPeriodLifecycleViewV1>(); }
    }

    public Task<AccountingApiResult<AccountingPeriodReopeningViewV1>> ProposePeriodReopeningAsync(Guid periodId,
        ProposeAccountingPeriodReopeningV1? request, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (periodId == Guid.Empty || request is null || request.ExpectedVersion < 1 || request.ProposalId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000 || request.Reason != request.Reason.Trim())
            return Task.FromResult(Invalid<AccountingPeriodReopeningViewV1>("request"));
        if (periods is not IAccountingPeriodReopeningStoreV1 store)
            return Task.FromResult(PeriodLifecycleUnavailable<AccountingPeriodReopeningViewV1>());
        return RunAsync<AccountingPeriodReopeningViewV1>(AccountingPermissionManifestV1.PeriodsPropose, idempotencyKey,
            "accounting.period.reopening.propose.v1", new { periodId, request }, AccountingApiStatus.Ok,
            async (metadata, actor) => new(AccountingApiStatus.Ok,
                await store.ProposeReopeningAsync(periodId, request, actor, metadata, cancellationToken), false, null),
            (stored, _) => (AccountingPeriodReopeningViewV1)stored, cancellationToken);
    }

    public Task<AccountingApiResult<AccountingPeriodReopeningViewV1>> ApprovePeriodReopeningAsync(Guid periodId, Guid proposalId,
        ApproveAccountingPeriodReopeningV1? request, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (periodId == Guid.Empty || proposalId == Guid.Empty || request is null || request.ExpectedVersion < 1
            || request.ExpectedPeriodVersion < 1) return Task.FromResult(Invalid<AccountingPeriodReopeningViewV1>("expectedVersion"));
        if (periods is not IAccountingPeriodReopeningStoreV1 store)
            return Task.FromResult(PeriodLifecycleUnavailable<AccountingPeriodReopeningViewV1>());
        return RunAsync<AccountingPeriodReopeningViewV1>(AccountingPermissionManifestV1.PeriodsApprove, idempotencyKey,
            "accounting.period.reopening.approve.v1", new { periodId, proposalId, request }, AccountingApiStatus.Ok,
            async (metadata, actor) => new(AccountingApiStatus.Ok,
                await store.ApproveReopeningAsync(periodId, proposalId, request, actor, metadata, cancellationToken), false, null),
            (stored, _) => (AccountingPeriodReopeningViewV1)stored, cancellationToken);
    }

    private static AccountingApiResult<T> PeriodLifecycleUnavailable<T>() where T : class =>
        new(AccountingApiStatus.Unavailable, null, false, "accounting_period_carry_source_unavailable");
}
