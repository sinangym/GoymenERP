using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

internal sealed partial class SalesRemainderChangeWorkInboxSourceV1(ICompanyContext companyContext, ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority, ISalesRemainderDecisionReadV1? owner = null)
    : SalesWorkInboxSourceBaseV1(companyContext, currentUser, authority), IWorkApprovalDecisionVersionSourceV1
{
    public const string KindName = SalesRemainderChangeDecisionBindingV1.KindName;
    public const string WorkDecisionPath = "/onaylarim/sales.remainder-change.v1";
    public const string NativeScreenPath = "/satis/siparisler/:draftId";
    public override string Kind => KindName;
    public override string ApproverPermission => SalesPermissionManifestV1.OrdersApprove;
    // Reimplement the interface so no authority await occurs AFTER the native owner has frozen its actual persona.
    Task<WorkInboxSourceResultV1> IWorkInboxSourceV1.ListPendingAsync(WorkInboxSourceContextV1 context, int maxCount, CancellationToken token) => ListPendingAsync(context, maxCount, token);
    public new async Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int maxCount, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); token.ThrowIfCancellationRequested();
        try
        {
            await Fence(context, token);
            if (owner is null) return WorkInboxSourceResultV1.Unavailable();
            var result = await owner.ListPendingRemainderDecisionsAsync(Math.Clamp(maxCount, 1, NativeApprovalQueueRulesV1.MaxItemsPerSource), ct => Fence(context, ct), token);
            token.ThrowIfCancellationRequested(); if (!SameContext(context)) return WorkInboxSourceResultV1.Denied();
            if (result is { State: SalesRemainderDecisionReadStateV1.Denied, Items: null }) return WorkInboxSourceResultV1.Denied();
            if (result is not { State: SalesRemainderDecisionReadStateV1.Found, Items: { } rows, IsSalesSupport: { } support } || result.CompanyId != context.CompanyId)
                return WorkInboxSourceResultV1.Unavailable();
            var items = rows.Select(row => SalesRemainderChangeDecisionBindingV1.PendingItem(row, support)).ToArray();
            return WorkInboxSourceResultV1.Found(context.CompanyId, items, result.HasMore);
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { return WorkInboxSourceResultV1.Denied(); }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.Data.Common.DbException) { return WorkInboxSourceResultV1.Unavailable(); }
    }
    // A base-typed caller must not accidentally use the base's final permission-only await after the persona-bound owner read.
    protected override Task<(IReadOnlyList<WorkInboxItemV1> Items, bool HasMore)> ReadAsync(Guid companyId, int take, bool disclosePrices, CancellationToken token) =>
        throw new InvalidOperationException("Satır kalanı listesi aynı native karar sahibi üzerinden okunur.");
    private async Task Fence(WorkInboxSourceContextV1 context, CancellationToken token)
    { token.ThrowIfCancellationRequested(); if (!await DecisionAllowedAsync(context, token)) throw new UnauthorizedAccessException(); token.ThrowIfCancellationRequested(); }
}
