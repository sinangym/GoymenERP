using System.Text.Json;
using GoymenERP.Modules.Accounting.Domain.Periods;

namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class AccountingApiService : IAccountingPeriodDecisionReadV1
{
    public async Task<AccountingApiResult<AccountingPeriodOpeningDecisionDetailV1>> ReadOpeningDecisionAsync(Guid periodId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence); cancellationToken.ThrowIfCancellationRequested();
        if (periodId == Guid.Empty) return Invalid<AccountingPeriodOpeningDecisionDetailV1>("periodId");
        var scope = await RequireAsync(AccountingPermissionManifestV1.PeriodsApprove, cancellationToken);
        if (scope is null) return Denied<AccountingPeriodOpeningDecisionDetailV1>();
        try
        {
            await ReadFence(cancellationToken);
            if (periods is not IAccountingPeriodDecisionStoreV1 reader) return PeriodLifecycleUnavailable<AccountingPeriodOpeningDecisionDetailV1>();
            var source = await reader.ReadOpeningDecisionAsync(periodId, ReadFence, cancellationToken);
            await ReadFence(cancellationToken);
            if (source is null) return NotFoundResult<AccountingPeriodOpeningDecisionDetailV1>();
            if (!ValidPeriodSource(source, scope.CompanyId, periodId)) return PeriodLifecycleUnavailable<AccountingPeriodOpeningDecisionDetailV1>();
            if (source.Period.PreparedByUserId == scope.ActorGuid) return Denied<AccountingPeriodOpeningDecisionDetailV1>();
            var view = PeriodView(source, scope.ActorGuid);
            return new(AccountingApiStatus.Ok, new(view, PeriodOpeningSourceFactsDigest(view), PeriodOpeningPreviewDigest(view)), false, null);
        }
        catch (FreshAuthorityDeniedException) { return Denied<AccountingPeriodOpeningDecisionDetailV1>(); }
        catch (AccountingStoreException error) when (error.Code == "accounting_period_carry_source_unavailable")
        { return PeriodLifecycleUnavailable<AccountingPeriodOpeningDecisionDetailV1>(); }
        Task ReadFence(CancellationToken token) => PeriodReadFenceAsync(scope, AccountingPermissionManifestV1.PeriodsApprove, freshConsumerFence, token);
    }

    public async Task<AccountingApiResult<AccountingPeriodReopeningDecisionDetailV1>> ReadReopeningDecisionAsync(Guid proposalId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence); cancellationToken.ThrowIfCancellationRequested();
        if (proposalId == Guid.Empty) return Invalid<AccountingPeriodReopeningDecisionDetailV1>("proposalId");
        var scope = await RequireAsync(AccountingPermissionManifestV1.PeriodsReopen, cancellationToken);
        if (scope is null) return Denied<AccountingPeriodReopeningDecisionDetailV1>();
        try
        {
            await ReadFence(cancellationToken);
            if (periods is not IAccountingPeriodDecisionStoreV1 reader) return PeriodLifecycleUnavailable<AccountingPeriodReopeningDecisionDetailV1>();
            var source = await reader.ReadReopeningDecisionAsync(proposalId, ReadFence, cancellationToken);
            await ReadFence(cancellationToken);
            if (source is null) return NotFoundResult<AccountingPeriodReopeningDecisionDetailV1>();
            if (!ValidReopeningSource(source, scope.CompanyId, proposalId)) return PeriodLifecycleUnavailable<AccountingPeriodReopeningDecisionDetailV1>();
            // ROL-D12: only THIS reopening proposal's maker is excluded; no new exclusion for the original opening maker.
            if (source.Reopening.ProposedBy == scope.ActorGuid) return Denied<AccountingPeriodReopeningDecisionDetailV1>();
            var view = PeriodView(source.Period, scope.ActorGuid);
            return new(AccountingApiStatus.Ok, new(source.Reopening, view, source.Closure,
                PeriodReopeningSourceFactsDigest(source), PeriodReopeningPreviewDigest(source.Reopening, view, source.Closure)) { LaterPeriodClosed = source.LaterPeriodClosed }, false, null);
        }
        catch (FreshAuthorityDeniedException) { return Denied<AccountingPeriodReopeningDecisionDetailV1>(); }
        catch (AccountingStoreException error) when (error.Code == "accounting_period_carry_source_unavailable")
        { return PeriodLifecycleUnavailable<AccountingPeriodReopeningDecisionDetailV1>(); }
        Task ReadFence(CancellationToken token) => PeriodReadFenceAsync(scope, AccountingPermissionManifestV1.PeriodsReopen, freshConsumerFence, token);
    }
    private async Task PeriodReadFenceAsync(AccessScope scope, string permission, Func<CancellationToken, Task> consumer, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); await consumer(token); token.ThrowIfCancellationRequested();
        await RequireStillAsync(scope, permission, token); token.ThrowIfCancellationRequested();
    }
    private static bool ValidPeriodSource(AccountingPeriodRecord source, Guid company, Guid id) =>
        source.Version > 0 && source.Period.CompanyId == company && source.Period.Id == id && source.Period.PreparedByUserId != Guid.Empty;
    private static bool ValidReopeningSource(AccountingPeriodReopeningDecisionSourceV1 source, Guid company, Guid id) =>
        ValidPeriodSource(source.Period, company, source.Reopening.PeriodId)
        && source.Reopening.Id == id && source.Reopening.CompanyId == company && source.Reopening.ProposedBy != Guid.Empty
        && source.Reopening.Version > 0 && source.Closure.CompanyId == company && source.Closure.PeriodId == source.Reopening.PeriodId
        && source.Closure.Id == source.Reopening.ClosureId && source.Closure.ClosedPeriodVersion == source.Reopening.ClosedPeriodVersion
        && source.Closure.CarryVoucherId != Guid.Empty && source.LaterPeriodClosed is not null;
    public static string PeriodOpeningSourceFactsDigest(PeriodViewV1 view) => Sha256(JsonSerializer.Serialize(new
    { view.PeriodId, view.CompanyId, view.StartsOn, view.EndsOn, view.PreparedByUserId, view.PreparedAtUtc }, Json));
    public static string PeriodOpeningPreviewDigest(PeriodViewV1 view) => Sha256(JsonSerializer.Serialize(view, Json));
    public static string PeriodReopeningSourceFactsDigest(AccountingPeriodReopeningDecisionSourceV1 source) => Sha256(JsonSerializer.Serialize(new
    {
        source.Reopening.Id, source.Reopening.CompanyId, source.Reopening.PeriodId, source.Reopening.ClosureId,
        source.Reopening.ClosedPeriodVersion, source.Reopening.ProposedBy, source.Reopening.ProposedAtUtc, source.Reopening.Reason,
        source.Closure.TargetPeriodId, source.Closure.CarryVoucherId, source.Closure.SourceHash, source.Closure.LineCount,
        source.Closure.ClosedBy, source.Closure.ClosedAtUtc,
        OpeningSourceFactsDigest = PeriodOpeningSourceFactsDigest(PeriodView(source.Period, Guid.Empty)),
    }, Json));
    public static string PeriodReopeningPreviewDigest(AccountingPeriodReopeningViewV1 reopening, PeriodViewV1 period, AccountingPeriodClosureViewV1 closure) =>
        Sha256(JsonSerializer.Serialize(new { reopening, period, closure }, Json));
}
