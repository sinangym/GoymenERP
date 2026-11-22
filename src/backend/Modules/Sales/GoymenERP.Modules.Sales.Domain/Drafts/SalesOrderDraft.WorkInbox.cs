namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// W1-A (AI-D31, AI-D32): the owner's own line predicates, exposed read-only so the Sales Work inbox sources list exactly the
/// lines the approval commands (<see cref="ApprovePriceFloorExceptions"/>, <see cref="ApproveDiscountExceptions"/>) name. They
/// add no rule: each one is the private predicate those commands and the submission gates already use.
/// </summary>
public sealed partial class SalesOrderDraft
{
    /// <summary>AT-021: whether the line's net unit price is below its price-list floor (the below-floor approval names it).</summary>
    public static bool IsBelowPriceFloor(CommercialDraftFields commercial)
    {
        ArgumentNullException.ThrowIfNull(commercial);
        return IsBelowFloor(commercial);
    }

    /// <summary>
    /// SAT-D2 / SAT-D35 / SAT-D43: whether the line needs the discount check and still waits for an approval (unchecked, or its
    /// check requires one). A subcontract service order's lines are never named (SAT-D195); the caller applies that header rule.
    /// </summary>
    public static bool NeedsDiscountExceptionApproval(CommercialDraftFields commercial)
    {
        ArgumentNullException.ThrowIfNull(commercial);
        return NeedsDiscountApproval(commercial);
    }
}
