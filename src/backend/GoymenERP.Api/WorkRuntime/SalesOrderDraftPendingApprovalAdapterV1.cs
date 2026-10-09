using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.WorkRuntime;

internal sealed class SalesOrderDraftPendingApprovalAdapterV1(
    ISalesOrderDraftPendingApprovalReadServiceV1 owner)
    : INativeApprovalReadAdapterV1
{
    private const string PendingApproval = "PendingApproval";

    public string Kind => NativeApprovalKindsV1.SalesOrderDraft;

    public async Task<NativeApprovalReadResultV1<NativeApprovalReadPageV1>> ListAsync(
        NativeApprovalReadContextV1 context,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var result = await owner.ListAsync(Sales(context), cursor, cancellationToken);
        if (result.Outcome != SalesOrderDraftPendingApprovalReadOutcomeV1.Found)
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

        var result = await owner.GetAsync(Sales(context), approvalId, cancellationToken);
        if (result.Outcome != SalesOrderDraftPendingApprovalReadOutcomeV1.Found)
        {
            return StrictFailure<NativeApprovalReadDetailV1>(result.Outcome, result.Resource);
        }
        if (result.Resource is null
            || !TryMapItem(context, approvalId, result.Resource.Item, out var item))
        {
            return new(NativeApprovalReadOutcomeV1.Unavailable, null);
        }
        return new(NativeApprovalReadOutcomeV1.Found,
            new SalesOrderDraftNativeApprovalReadDetailV1(item));
    }

    private static bool TryMapPage(
        NativeApprovalReadContextV1 context,
        SalesOrderDraftPendingApprovalPageV1? source,
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
                || !seen.Add(sourceItem.DraftId)
                || !TryMapItem(context, sourceItem.DraftId, sourceItem, out var item))
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
        SalesOrderDraftPendingApprovalItemV1? source,
        out SalesOrderDraftNativeApprovalReadItemV1 item)
    {
        item = null!;
        if (source is null
            || source.DraftId == Guid.Empty
            || source.DraftId != approvalId
            || source.CompanyId != context.CompanyId
            || string.IsNullOrWhiteSpace(source.DocumentNo)
            || string.IsNullOrWhiteSpace(source.Version)
            || string.IsNullOrWhiteSpace(source.Revision)
            || string.IsNullOrWhiteSpace(source.SubmittedBy))
        {
            return false;
        }

        item = new(
            source.DraftId,
            source.CompanyId,
            PendingApproval,
            source.Version,
            source.Revision,
            source.DocumentNo,
            source.IsOwnDraft,
            source.SubmittedBy,
            source.SubmittedAtUtc);
        return true;
    }

    private static SalesOrderDraftPendingApprovalReadContextV1 Sales(
        NativeApprovalReadContextV1 context) => new(context.CompanyId, context.ActorId);

    private static NativeApprovalReadResultV1<TTarget> StrictFailure<TTarget>(
        SalesOrderDraftPendingApprovalReadOutcomeV1 outcome,
        object? resource)
        where TTarget : class =>
        resource is null && outcome is SalesOrderDraftPendingApprovalReadOutcomeV1.Unavailable
            or SalesOrderDraftPendingApprovalReadOutcomeV1.Denied
            or SalesOrderDraftPendingApprovalReadOutcomeV1.NotFound
                ? new(Map(outcome), null)
                : new(NativeApprovalReadOutcomeV1.Unavailable, null);

    private static NativeApprovalReadOutcomeV1 Map(
        SalesOrderDraftPendingApprovalReadOutcomeV1 outcome) => outcome switch
    {
        SalesOrderDraftPendingApprovalReadOutcomeV1.Found => NativeApprovalReadOutcomeV1.Found,
        SalesOrderDraftPendingApprovalReadOutcomeV1.NotFound => NativeApprovalReadOutcomeV1.NotFound,
        SalesOrderDraftPendingApprovalReadOutcomeV1.Denied => NativeApprovalReadOutcomeV1.Denied,
        SalesOrderDraftPendingApprovalReadOutcomeV1.Unavailable => NativeApprovalReadOutcomeV1.Unavailable,
        _ => NativeApprovalReadOutcomeV1.Unavailable,
    };
}
