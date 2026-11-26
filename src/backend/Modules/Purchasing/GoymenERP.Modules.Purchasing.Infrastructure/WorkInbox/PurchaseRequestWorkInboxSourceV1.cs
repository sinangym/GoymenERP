using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Requests;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.WorkInbox;

/// <summary>
/// WP7 (AI-D31, AI-D32, YOL-D18): the purchase requests waiting for the purchase manager's decision, as Purchasing lists them
/// for the Work inbox. A request waits while it is Submitted — exactly what <c>ApprovePurchaseRequestV1</c> accepts (ALS-D2);
/// it is decided on the request's own screen by a holder of <c>purchasing.requests.approve</c> (the inbox decides nothing).
/// Every submitted request of the company is returned, its makers' own included: Work hides the item from the owner's "may not
/// decide" set (D4) — <see cref="PurchaseRequest.DeciderExclusions"/>: the preparer (as maker) and the submitter.
/// <para>
/// A submitted request changes only by its decision or withdrawal, so its last change time is the submission time it waits
/// from. A request has no document number: the label is the short reference the Purchasing request list shows. Purchasing
/// stores no threshold or matrix for a request, so the rule carries no numbers. A submitted request whose makers are not both
/// recorded can be decided by no one; the read is then unavailable rather than showing an item nobody may decide.
/// </para>
/// </summary>
internal sealed class PurchaseRequestWorkInboxSourceV1(
    PurchasingDbContext dbContext,
    PurchasingWorkInboxFence fence) : IWorkApprovalDecisionSourceV1
{
    public const string ModuleName = "purchasing";
    public const string KindName = "purchasing.request.v1";

    /// <summary>Every submitted request needs the purchase manager's decision by someone other than its makers (ALS-D2).</summary>
    public const string ApprovalRequiredRule = "purchasing.request-approval-required";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => PurchasingPermissionManifestV1.RequestsApprove;

    /// <summary>WP8 approval-only purpose read. No general request browse grant, lines, quantities or prices.
    /// Decided rows remain readable to let the native command return its immutable replay/refusal.</summary>
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (itemId == Guid.Empty || await fence.CheckAsync(context, ApproverPermission, cancellationToken) is not null) return null;
        var row = await dbContext.Requests.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.CompanyId == context.CompanyId && candidate.Id == itemId, cancellationToken);
        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is not null || row is null) return null;
        if (row.Version < 1 || !Enum.IsDefined((PurchaseRequestStatus)row.Status) || !TryItem(context, row, out var item))
            throw new InvalidOperationException("Talep karar kaynağı geçersiz.");
        return new(context.CompanyId, item, row.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            row.Status == (int)PurchaseRequestStatus.Submitted);
    }

    public async Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        cancellationToken.ThrowIfCancellationRequested();
        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is { } refused)
        {
            return refused;
        }

        List<PurchaseRequestRow> rows;
        int total;
        try
        {
            var submitted = (int)PurchaseRequestStatus.Submitted;
            var pending = dbContext.Requests.AsNoTracking()
                .Where(row => row.CompanyId == context.CompanyId && row.Status == submitted);
            total = await pending.CountAsync(cancellationToken);
            rows = await pending.OrderBy(row => row.UpdatedAtUtc).ThenBy(row => row.Id)
                .Take(maxCount)
                .ToListAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await fence.CheckAsync(context, ApproverPermission, cancellationToken)
                ?? WorkInboxSourceResultV1.Unavailable();
        }

        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        var items = new List<WorkInboxItemV1>(rows.Count);
        foreach (var row in rows)
        {
            if (!TryItem(context, row, out var item))
            {
                // A record Purchasing cannot read as its own invariants say: unavailable, never a partial list.
                return WorkInboxSourceResultV1.Unavailable();
            }

            items.Add(item);
        }

        return WorkInboxSourceResultV1.Found(context.CompanyId, items, total > rows.Count);
    }

    private static bool TryItem(WorkInboxSourceContextV1 context, PurchaseRequestRow row, out WorkInboxItemV1 item)
    {
        item = null!;
        if (row.Id == Guid.Empty || row.CompanyId != context.CompanyId
            || PurchaseRequest.DeciderExclusions(row.CreatedBy, row.SubmittedBy) is not { } excluded)
        {
            return false;
        }

        var preparedBy = row.CreatedBy;
        var others = excluded.Where(actor => !string.Equals(actor, preparedBy, StringComparison.Ordinal)).ToArray();
        item = new WorkInboxItemV1(ModuleName, KindName, row.Id, PurchasingWorkInboxFence.ShortReference(row.Id),
            row.UpdatedAtUtc.ToUniversalTime(),
            "/onaylarim/purchasing.request.v1/" + row.Id.ToString("D"), preparedBy,
            [new(ApprovalRequiredRule, null, null, null, null)], [], others);
        return true;
    }
}
