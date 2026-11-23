using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Returns;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.WorkInbox;

/// <summary>
/// WP7 (AI-D31, AI-D32, YOL-D18): the returns to supplier whose invoice unit cost could not be read and whose manual cost waits
/// for its exception approval (SAT-D393), as Purchasing lists them for the Work inbox. A return waits while it is
/// PendingCostException — exactly what <c>ApprovePurchaseSupplierReturnCostExceptionV1</c> accepts; it is decided on the
/// order's own screen (the return panel) under <c>purchasing.orders.approve</c> (SAT-D396) by someone other than the person
/// who entered it (ALS-D128, E-75, completing SAT-D396; <see cref="PurchaseSupplierReturnRuleCodes.SameActor"/>). Every pending
/// return of the company is returned, the enterer's own included: Work hides it from its maker.
/// <para>
/// The rule carries the manual unit cost (TRY per kg) the return would leave stock at only when the caller may see purchase
/// costs (<c>purchasing.orders.price.read</c>, the owner's disclosure rule); otherwise it carries no number. The returned
/// kilograms are always shown. A pending return whose enterer is not recorded can be decided by no one; the read is then
/// unavailable.
/// </para>
/// </summary>
internal sealed class PurchaseSupplierReturnCostExceptionWorkInboxSourceV1(
    PurchasingDbContext dbContext,
    PurchasingWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "purchasing";
    public const string KindName = "purchasing.supplier-return-cost-exception.v1";

    /// <summary>The invoice cost could not be read; the manual unit cost (TRY/kg) needs its exception approval (SAT-D393).</summary>
    public const string ManualCostRule = "purchasing.supplier-return-manual-cost";

    /// <summary>The kilograms that leave stock when the exception is approved.</summary>
    public const string QuantityRule = "purchasing.supplier-return-quantity";

    /// <summary>The currency of the stored unit cost (<c>UnitCostTryPerKg</c>).</summary>
    private const string CostUnit = "TRY";

    private const string KilogramUnit = "kg";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => PurchasingPermissionManifestV1.OrdersApprove;

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

        List<PurchaseSupplierReturnRow> rows;
        Dictionary<Guid, string?> orders;
        int total;
        try
        {
            var pendingStatus = (int)PurchaseSupplierReturnStatus.PendingCostException;
            var pending = dbContext.SupplierReturns.AsNoTracking()
                .Where(row => row.CompanyId == context.CompanyId && row.Status == pendingStatus);
            total = await pending.CountAsync(cancellationToken);
            rows = await pending.OrderBy(row => row.RequestedAtUtc).ThenBy(row => row.ReturnId)
                .Take(maxCount)
                .ToListAsync(cancellationToken);
            var orderIds = rows.Select(row => row.OrderId).Distinct().ToArray();
            orders = await dbContext.Orders.AsNoTracking()
                .Where(row => row.CompanyId == context.CompanyId && orderIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, row => row.DocumentNumber, cancellationToken);
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

        var disclosed = await fence.PricesDisclosedAsync(context, cancellationToken);
        var items = new List<WorkInboxItemV1>(rows.Count);
        foreach (var row in rows)
        {
            if (!TryItem(context, row, orders, disclosed, out var item))
            {
                return WorkInboxSourceResultV1.Unavailable();
            }

            items.Add(item);
        }

        return WorkInboxSourceResultV1.Found(context.CompanyId, items, total > rows.Count);
    }

    private static bool TryItem(WorkInboxSourceContextV1 context, PurchaseSupplierReturnRow row,
        IReadOnlyDictionary<Guid, string?> orders, bool disclosed, out WorkInboxItemV1 item)
    {
        item = null!;
        if (row.ReturnId == Guid.Empty || row.CompanyId != context.CompanyId || row.OrderId == Guid.Empty
            || string.IsNullOrWhiteSpace(row.RequestedBy) || !orders.TryGetValue(row.OrderId, out var documentNumber)
            || row.QuantityKg <= 0 || row.UnitCostTryPerKg < 0)
        {
            return false;
        }

        var orderLabel = PurchasingWorkInboxFence.OrderLabel(row.OrderId, documentNumber);
        if (orderLabel.Length > WorkInboxRulesV1.MaxDocumentLabelLength)
        {
            return false;
        }

        var orderLink = PurchasingWorkInboxFence.Link(PurchasingWorkInboxFence.OrderScreenPath, row.OrderId);
        var rules = new List<WorkApprovalRuleResultV1>(2)
        {
            disclosed
                ? new(ManualCostRule, null, WorkInboxRulesV1.Decimal(row.UnitCostTryPerKg), null, CostUnit)
                : new(ManualCostRule, null, null, null, null),
            new(QuantityRule, null, WorkInboxRulesV1.Decimal(row.QuantityKg), null, KilogramUnit),
        };
        item = new WorkInboxItemV1(ModuleName, KindName, row.ReturnId, orderLabel, row.RequestedAtUtc.ToUniversalTime(), orderLink,
            row.RequestedBy, rules, [new("purchase-order", orderLabel, row.OrderId, orderLink)]);
        return true;
    }
}
