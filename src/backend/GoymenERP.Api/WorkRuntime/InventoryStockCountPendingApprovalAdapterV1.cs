using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.WorkRuntime;

internal sealed class InventoryStockCountPendingApprovalAdapterV1(
    IInventoryStockCountPendingApprovalReadServiceV1 owner)
    : INativeApprovalReadAdapterV1
{
    public string Kind => NativeApprovalKindsV1.InventoryStockCount;

    public async Task<NativeApprovalReadResultV1<NativeApprovalReadPageV1>> ListAsync(
        NativeApprovalReadContextV1 context,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var result = await owner.ListAsync(Inventory(context), cursor, cancellationToken);
        if (result.Outcome != InventoryPendingApprovalReadOutcomeV1.Found)
        {
            return StrictFailure<NativeApprovalReadPageV1>(result.Outcome, result.Resource);
        }
        if (!TryMapPage(context, result.Resource, out var page))
        {
            return new(NativeApprovalReadOutcomeV1.Unavailable, null);
        }
        return new(NativeApprovalReadOutcomeV1.Found, page);
    }

    public async Task<NativeApprovalReadResultV1<NativeApprovalReadDetailV1>> GetAsync(
        NativeApprovalReadContextV1 context,
        Guid approvalId,
        CancellationToken cancellationToken)
    {
        if (approvalId == Guid.Empty)
        {
            return new(NativeApprovalReadOutcomeV1.NotFound, null);
        }

        var result = await owner.GetAsync(Inventory(context), approvalId, cancellationToken);
        if (result.Outcome != InventoryPendingApprovalReadOutcomeV1.Found)
        {
            return StrictFailure<NativeApprovalReadDetailV1>(result.Outcome, result.Resource);
        }
        if (result.Resource is null
            || !TryMapItem(context, approvalId, result.Resource.Item, out var item))
        {
            return new(NativeApprovalReadOutcomeV1.Unavailable, null);
        }
        return new(NativeApprovalReadOutcomeV1.Found,
            new InventoryStockCountNativeApprovalReadDetailV1(item));
    }

    private static bool TryMapPage(
        NativeApprovalReadContextV1 context,
        InventoryStockCountPendingApprovalPageV1? source,
        out NativeApprovalReadPageV1 page)
    {
        page = null!;
        if (source is null
            || source.CompanyId != context.CompanyId
            || source.Items is null
            || source.NextCursor is { Length: 0 })
        {
            return false;
        }

        var items = new NativeApprovalReadItemV1[source.Items.Count];
        var seen = new HashSet<Guid>();
        for (var index = 0; index < source.Items.Count; index++)
        {
            var sourceItem = source.Items[index];
            if (sourceItem is null
                || !seen.Add(sourceItem.CountId)
                || !TryMapItem(context, sourceItem.CountId, sourceItem, out var item))
            {
                return false;
            }
            items[index] = item;
        }

        page = new(source.CompanyId, items, source.NextCursor);
        return true;
    }

    private static bool TryMapItem(
        NativeApprovalReadContextV1 context,
        Guid approvalId,
        InventoryStockCountPendingApprovalItemV1? source,
        out InventoryStockCountNativeApprovalReadItemV1 item)
    {
        item = null!;
        if (source is null
            || source.CountId == Guid.Empty
            || source.CountId != approvalId
            || source.CompanyId != context.CompanyId
            || source.LabelId == Guid.Empty
            || !InventoryPendingApprovalReadContractV1.IsCanonicalLabelCode(source.LabelCode)
            || source.ProductId == Guid.Empty
            || source.LocationId == Guid.Empty
            || source.OwnerId == Guid.Empty
            || source.CustodianId == Guid.Empty
            || string.IsNullOrWhiteSpace(source.LabelVersion)
            || string.IsNullOrWhiteSpace(source.OwnerKind)
            || string.IsNullOrWhiteSpace(source.CustodianKind)
            || string.IsNullOrWhiteSpace(source.Block)
            || string.IsNullOrWhiteSpace(source.BookKg)
            || string.IsNullOrWhiteSpace(source.CountedKg)
            || string.IsNullOrWhiteSpace(source.DifferenceKg)
            || source.DifferenceKg == "0"
            || string.IsNullOrWhiteSpace(source.Reason)
            || string.IsNullOrWhiteSpace(source.SubmittedBy)
            || string.Equals(source.SubmittedBy, context.ActorId, StringComparison.Ordinal))
        {
            return false;
        }

        item = new(
            source.CountId,
            source.CompanyId,
            InventoryStockCountStatusesV1.PendingApproval,
            source.LabelVersion,
            source.LabelId,
            source.ProductId,
            source.LocationId,
            source.DifferenceKg,
            source.LabelVersion,
            source.OwnerKind,
            source.OwnerId,
            source.CustodianKind,
            source.CustodianId,
            source.Block,
            source.BookKg,
            source.CountedKg,
            source.Reason,
            context.IncludeInventoryLabelCode ? source.LabelCode : null,
            source.SubmittedBy,
            source.SubmittedAtUtc);
        return true;
    }

    private static InventoryPendingApprovalReadContextV1 Inventory(
        NativeApprovalReadContextV1 context) => new(context.CompanyId, context.ActorId);

    private static NativeApprovalReadResultV1<TTarget> StrictFailure<TTarget>(
        InventoryPendingApprovalReadOutcomeV1 outcome,
        object? resource)
        where TTarget : class =>
        resource is null && outcome is InventoryPendingApprovalReadOutcomeV1.Unavailable
            or InventoryPendingApprovalReadOutcomeV1.Denied
            or InventoryPendingApprovalReadOutcomeV1.NotFound
                ? new(Map(outcome), null)
                : new(NativeApprovalReadOutcomeV1.Unavailable, null);

    private static NativeApprovalReadOutcomeV1 Map(
        InventoryPendingApprovalReadOutcomeV1 outcome) => outcome switch
    {
        InventoryPendingApprovalReadOutcomeV1.Found => NativeApprovalReadOutcomeV1.Found,
        InventoryPendingApprovalReadOutcomeV1.NotFound => NativeApprovalReadOutcomeV1.NotFound,
        InventoryPendingApprovalReadOutcomeV1.Denied => NativeApprovalReadOutcomeV1.Denied,
        InventoryPendingApprovalReadOutcomeV1.Unavailable => NativeApprovalReadOutcomeV1.Unavailable,
        _ => NativeApprovalReadOutcomeV1.Unavailable,
    };
}
