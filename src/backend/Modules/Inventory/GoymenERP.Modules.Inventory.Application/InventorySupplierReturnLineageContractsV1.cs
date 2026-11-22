using GoymenERP.Modules.Inventory.Domain.Costing;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// AK-8 (SAT-D433, SAT-D441, SAT-D445): how a supplier return leaves a label that is not the receipt's own label. The purchasing
/// owner decides the mode from the lineage and the supplier; Inventory re-checks what it owns (the lineage and the cost pool).
/// </summary>
public enum InventorySupplierReturnLineageModeV1
{
    /// <summary>SAT-D433: the label's lineage is the named receipt alone; it leaves at that receipt's cost.</summary>
    Lineage = 1,

    /// <summary>
    /// SAT-D441/SAT-D444/SAT-D445: the label's lineage is not only the supplier's receipts. Stock leaves at the warehouse/product
    /// moving average; the supplier price is the chosen order's unit price. A readable difference between the two is refused
    /// (SAT-D371) with <see cref="InventorySupplierReturnExitCodesV1.CostDifferenceUnposted"/>; an unreadable average is 503.
    /// </summary>
    Quantity = 2,
}

public enum InventorySupplierReturnLineageRootKindV1
{
    /// <summary>A purchase goods receipt (the root label was brought in by one goods acceptance).</summary>
    PurchaseReceipt = 1,

    /// <summary>Anything else: production output, opening stock, customer return or any other first entry (SAT-D441).</summary>
    Other = 2,
}

/// <summary>
/// One root of a label's lineage. <see cref="Share"/> is the exact fraction of the label's kilograms that came from this root
/// (TI1); <see cref="ReceivedAtUtc"/> orders receipts for ties (SAT-D440, SAT-D442). Acceptance and receipt movement are set only
/// for a purchase receipt.
/// </summary>
public sealed record InventorySupplierReturnLineageRootV1(
    Guid RootLabelId,
    InventorySupplierReturnLineageRootKindV1 Kind,
    Guid? GoodsAcceptanceId,
    Guid? ReceiptMovementId,
    DateTimeOffset? ReceivedAtUtc,
    InventoryExactCostRatioV1 Share);

/// <summary>A label's lineage roots, company-fenced and price-free. The shares add up to exactly one.</summary>
public sealed record InventorySupplierReturnLineageV1(
    Guid CompanyId,
    Guid LabelId,
    Guid ProductId,
    IReadOnlyList<InventorySupplierReturnLineageRootV1> Roots);

/// <summary>
/// Reads the lineage of one label back to its roots. An unknown label, another company's label or an unreadable lineage is
/// refused with 503 <see cref="InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid"/>; a root is never guessed.
/// </summary>
public interface IInventorySupplierReturnLineageReadPortV1
{
    Task<InventorySupplierReturnLineageV1> ReadAsync(Guid companyId, Guid labelId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The AK-8 gate on the Inventory side. With the golden gate closed a source label other than the receipt's own label keeps the
/// earlier 503 receipt-trace boundary; with it open the request must name a mode, and a mode without a source label is refused.
/// </summary>
public static class InventorySupplierReturnLineageGateV1
{
    /// <summary>True when the request leaves another label than the receipt's; throws the closed-contract refusal otherwise.</summary>
    public static bool Admit(Guid? sourceLabelId, InventorySupplierReturnLineageModeV1? mode, Guid receiptLabelId,
        bool? acceptedOverrideForTests = null)
    {
        if (sourceLabelId is null || sourceLabelId == receiptLabelId)
        {
            // The receipt's own label is the existing return (its lineage is that receipt alone): no mode is named for it.
            if (mode is not null)
                throw InventorySupplierReturnExitException.Refused(400, InventorySupplierReturnExitCodesV1.InvalidRequest);
            return false;
        }

        if (!SupplierReturnLineageGoldenV1.IsAccepted(acceptedOverrideForTests))
            throw InventorySupplierReturnExitException.Refused(503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid);
        if (sourceLabelId == Guid.Empty || mode is null || !Enum.IsDefined(mode.Value))
            throw InventorySupplierReturnExitException.Refused(400, InventorySupplierReturnExitCodesV1.InvalidRequest);
        return true;
    }

    /// <summary>
    /// SAT-D433 single-receipt check owned by Inventory: in <see cref="InventorySupplierReturnLineageModeV1.Lineage"/> mode the
    /// source label's lineage must be exactly the named receipt's root label (share one). A multi-receipt lineage needs one exit
    /// per receipt in one atomic return, which this participant does not stage: it stays 503.
    /// </summary>
    public static void RequireLineageMode(InventorySupplierReturnLineageV1 lineage, Guid receiptLabelId)
    {
        ArgumentNullException.ThrowIfNull(lineage);
        var roots = lineage.Roots;
        if (roots.Count == 1 && roots[0].RootLabelId == receiptLabelId
            && roots[0].Kind == InventorySupplierReturnLineageRootKindV1.PurchaseReceipt
            && roots[0].Share.Value().CompareTo(ExactCostRatio.Of(1m)) == 0)
            return;
        if (roots.Any(root => root.RootLabelId == receiptLabelId) && roots.Count > 1
            && roots.All(root => root.Kind == InventorySupplierReturnLineageRootKindV1.PurchaseReceipt))
            throw InventorySupplierReturnExitException.Refused(503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid);
        throw InventorySupplierReturnExitException.Refused(409, InventorySupplierReturnExitCodesV1.LineageMismatch);
    }
}
