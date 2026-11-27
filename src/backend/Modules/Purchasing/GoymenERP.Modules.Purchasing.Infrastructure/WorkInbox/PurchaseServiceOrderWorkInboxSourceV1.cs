using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.WorkInbox;

/// <summary>
/// WP7 (AI-D31, AI-D32, YOL-D18): the service and outgoing subcontract purchase orders waiting for approval (ALS-D29/D40), as
/// Purchasing lists them for the Work inbox. A service order waits while its current revision is a Draft — exactly what
/// <c>ApprovePurchaseServiceOrderV1</c> accepts; it is approved on the service order's own screen by a holder of
/// <c>purchasing.orders.approve</c> other than its preparer (<see cref="PurchaseServiceOrder.SameActor"/>). Every draft of the
/// company is returned, the preparer's own included: Work hides a maker's own item.
/// <para>
/// The state lives in the immutable revision snapshot of the order's current version (the store's own reading rule), so the
/// drafts are filtered in memory. A draft waits from its last change (creation or the revision that reopened it); a price
/// revision of an approved order (ALS-D18/D9) reopens it and the rule names that.
/// Purchasing stores no approval threshold for a service order, so the rules carry no numbers.
/// </para>
/// </summary>
internal sealed class PurchaseServiceOrderWorkInboxSourceV1(
    PurchasingDbContext dbContext,
    PurchasingWorkInboxFence fence) : IWorkApprovalDecisionSourceV1
{
    public const string ModuleName = "purchasing";
    public const string KindName = "purchasing.service-order.v1";

    /// <summary>Every service order needs one approval by someone other than its preparer.</summary>
    public const string ApprovalRequiredRule = "purchasing.service-order-approval-required";

    /// <summary>The order was approved and a later price revision reopened it as a draft (ALS-D18/D9).</summary>
    public const string PriceRevisedRule = "purchasing.service-order-price-revised";

    /// <summary>The order sends the company's own material to a subcontractor (outgoing subcontract, ALS-D40).</summary>
    public const string OwnMaterialSubcontractRule = "purchasing.service-order-own-material-subcontract";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => PurchasingPermissionManifestV1.OrdersApprove;

    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (itemId == Guid.Empty || await fence.CheckAsync(context, ApproverPermission, cancellationToken) is not null) return null;
        var row = await (from parent in dbContext.ServiceOrders.AsNoTracking()
            where parent.CompanyId == context.CompanyId && parent.OrderId == itemId
            join revision in dbContext.ServiceOrderRevisions.AsNoTracking()
                on new { parent.CompanyId, parent.OrderId, parent.Version }
                equals new { revision.CompanyId, revision.OrderId, revision.Version }
            select revision).SingleOrDefaultAsync(cancellationToken);
        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is not null || row is null) return null;
        var value = Restore(context.CompanyId, row);
        PurchaseServiceOrder.Validate(value);
        if (string.IsNullOrWhiteSpace(value.DocumentNumber) || value.DocumentNumber.Length > 64 || !TryItem(value, out var item))
            throw new InvalidOperationException("Hizmet siparişi karar kaynağı geçersiz.");
        return new(context.CompanyId, item, value.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            value.Status == PurchaseServiceOrderStatus.Draft);
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

        List<PurchaseServiceOrderState> drafts;
        try
        {
            var current = await (
                    from parent in dbContext.ServiceOrders.AsNoTracking()
                    where parent.CompanyId == context.CompanyId
                    join revision in dbContext.ServiceOrderRevisions.AsNoTracking()
                        on new { parent.CompanyId, parent.OrderId, parent.Version }
                        equals new { revision.CompanyId, revision.OrderId, revision.Version }
                    select revision)
                .ToListAsync(cancellationToken);
            drafts = current.Select(revision => Restore(context.CompanyId, revision))
                .Where(value => value.Status == PurchaseServiceOrderStatus.Draft)
                .OrderBy(value => value.UpdatedAtUtc).ThenBy(value => value.OrderId)
                .ToList();
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

        var items = new List<WorkInboxItemV1>(Math.Min(drafts.Count, maxCount));
        foreach (var draft in drafts.Take(maxCount))
        {
            if (!TryItem(draft, out var item))
            {
                return WorkInboxSourceResultV1.Unavailable();
            }

            items.Add(item);
        }

        return WorkInboxSourceResultV1.Found(context.CompanyId, items, drafts.Count > maxCount);
    }

    /// <summary>The store's own reading rule (PurchaseServiceOrderStoreV1.ReadAsync): an inconsistent snapshot throws.</summary>
    private static PurchaseServiceOrderState Restore(Guid companyId, PurchaseServiceOrderRevisionRow row)
    {
        var value = JsonSerializer.Deserialize<PurchaseServiceOrderState>(row.SnapshotJson, PurchaseServiceOrderStoreV1.Json)
            ?? throw new InvalidOperationException("Hizmet siparişi görüntüsü okunamadı.");
        if (value.CompanyId != companyId || row.CompanyId != companyId || value.OrderId != row.OrderId || value.Version != row.Version)
        {
            throw new InvalidOperationException("Hizmet siparişi kapsamı uyuşmuyor.");
        }

        return value;
    }

    private static bool TryItem(PurchaseServiceOrderState value, out WorkInboxItemV1 item)
    {
        item = null!;
        var label = value.DocumentNumber;
        if (value.OrderId == Guid.Empty || string.IsNullOrWhiteSpace(value.PreparedBy) || string.IsNullOrWhiteSpace(label)
            || label.Length > WorkInboxRulesV1.MaxDocumentLabelLength || value.SupplierPartyId == Guid.Empty
            || value.CompanyAccountId == Guid.Empty)
        {
            return false;
        }

        var rules = new List<WorkApprovalRuleResultV1>(3) { new(ApprovalRequiredRule, null, null, null, null) };
        if (value.PriceRevision > 0)
        {
            rules.Add(new(PriceRevisedRule, null, null, null, null));
        }

        if (value.Kind == PurchaseServiceOrderKind.OwnMaterialSubcontract)
        {
            rules.Add(new(OwnMaterialSubcontractRule, null, null, null, null));
        }

        var references = new List<WorkApprovalReferenceV1>(2)
        {
            new("supplier", PurchasingWorkInboxFence.ShortReference(value.SupplierPartyId), value.SupplierPartyId, null),
            new("company-account", PurchasingWorkInboxFence.ShortReference(value.CompanyAccountId), value.CompanyAccountId, null),
        };
        item = new WorkInboxItemV1(ModuleName, KindName, value.OrderId, label, value.UpdatedAtUtc.ToUniversalTime(),
            $"/onaylarim/{KindName}/{value.OrderId:D}", value.PreparedBy,
            rules, references);
        return true;
    }
}
