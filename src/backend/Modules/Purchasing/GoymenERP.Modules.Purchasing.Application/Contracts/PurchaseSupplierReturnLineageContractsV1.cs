using System.Globalization;
using System.Numerics;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// AK-8 gate on the Purchasing side (SAT-D433, SAT-D440–SAT-D445; golden <c>docs/decisions/ADET_KG_FORMUL_GOLDEN_V1.md</c>
/// section 4, part of this train). It mirrors Inventory's <c>SupplierReturnLineageGoldenV1</c>; with it false a return that
/// names a source label is refused 503 <see cref="PurchaseSupplierReturnCodesV1.LineageUnavailable"/> before anything is read
/// or recorded, exactly the earlier boundary. <see cref="PurchaseSupplierReturnPolicyV1.LineageGoldenAccepted"/> carries it so a
/// test can pin the closed behaviour.
/// </summary>
public static class PurchaseSupplierReturnLineageGoldenV1
{
    public const bool SupplierReturnLineageGoldenAccepted = true;
}

/// <summary>How a return leaves a foreign source label (stored as 1/2; shown as its name).</summary>
public enum PurchaseSupplierReturnLineageModeV1
{
    /// <summary>SAT-D433: the label's lineage is only the returning supplier's receipts.</summary>
    Lineage = 1,

    /// <summary>SAT-D441/SAT-D444/SAT-D445: any other lineage; the whole return is taken from the chosen receipt.</summary>
    Quantity = 2,
}

/// <summary>One lineage root as Inventory answers it; <see cref="GoodsAcceptanceId"/> only for a purchase receipt root.</summary>
public sealed record PurchaseSupplierReturnLineageRootV1(bool IsPurchaseReceipt, Guid? GoodsAcceptanceId, string ShareNumerator,
    string ShareDenominator);

/// <summary>A label's lineage roots (company-fenced); the shares add up to one.</summary>
public sealed record PurchaseSupplierReturnLineageV1(Guid LabelId, IReadOnlyList<PurchaseSupplierReturnLineageRootV1> Roots);

/// <summary>
/// Reads a label's lineage from the Inventory owner (wired by the host). Purchasing never reads Inventory tables. An unreadable
/// lineage is answered by throwing; a host without the port leaves foreign-label returns 503.
/// </summary>
public interface IPurchaseSupplierReturnLineageReadPortV1
{
    Task<PurchaseSupplierReturnLineageV1> ReadAsync(Guid companyId, Guid labelId, CancellationToken cancellationToken);
}

/// <summary>The planned way a foreign-label return leaves stock.</summary>
public sealed record PurchaseSupplierReturnLineagePlanV1(PurchaseSupplierReturnLineageModeV1 Mode);

/// <summary>
/// AK-8 planning rules (pure). SAT-D445: lineage distribution only when every root is a purchase receipt of the returning
/// supplier; any other lineage is a quantity return from the chosen receipt. SAT-D433 with SAT-D440/D442 needs one exit per
/// receipt in one atomic return; a single-receipt lineage (a split of that receipt) is one exit of the whole return, a
/// multi-receipt lineage is not staged yet (503, never a partial or re-routed share).
/// </summary>
public static class PurchaseSupplierReturnLineagePlannerV1
{
    public static PurchaseSupplierReturnLineagePlanV1 Plan(PurchaseSupplierReturnLineageV1 lineage,
        IReadOnlyDictionary<Guid, Guid?> supplierByAcceptance, Guid supplierId, Guid goodsAcceptanceId)
    {
        ArgumentNullException.ThrowIfNull(lineage);
        ArgumentNullException.ThrowIfNull(supplierByAcceptance);
        var roots = lineage.Roots;
        if (roots is null || roots.Count == 0 || supplierId == Guid.Empty || goodsAcceptanceId == Guid.Empty
            || roots.Any(root => root is null || root.IsPurchaseReceipt != (root.GoodsAcceptanceId is not null)
                || root.GoodsAcceptanceId == Guid.Empty)
            || !SharesAddUpToOne(roots))
            throw Unavailable();
        var purchase = roots.Where(root => root.IsPurchaseReceipt).Select(root => root.GoodsAcceptanceId!.Value).ToArray();
        if (purchase.Distinct().Count() != purchase.Length
            || purchase.Any(acceptance => !supplierByAcceptance.TryGetValue(acceptance, out var supplier) || supplier is null))
            throw Unavailable();
        var onlySupplier = roots.All(root => root.IsPurchaseReceipt && supplierByAcceptance[root.GoodsAcceptanceId!.Value] == supplierId);
        if (!onlySupplier) return new(PurchaseSupplierReturnLineageModeV1.Quantity);
        if (purchase.Length > 1) throw Unavailable();
        if (purchase[0] != goodsAcceptanceId)
            throw new PurchasingServiceException(409, PurchaseSupplierReturnCodesV1.LineageReceiptMismatch,
                [new("goodsAcceptanceId", PurchaseSupplierReturnCodesV1.LineageReceiptMismatch)]);
        return new(PurchaseSupplierReturnLineageModeV1.Lineage);
    }

    internal static PurchasingServiceException Unavailable() => new(503, PurchaseSupplierReturnCodesV1.LineageUnavailable);

    private static bool SharesAddUpToOne(IReadOnlyList<PurchaseSupplierReturnLineageRootV1> roots)
    {
        BigInteger numerator = BigInteger.Zero, denominator = BigInteger.One;
        foreach (var root in roots)
        {
            if (!BigInteger.TryParse(root.ShareNumerator, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                || !BigInteger.TryParse(root.ShareDenominator, NumberStyles.None, CultureInfo.InvariantCulture, out var d)
                || n.IsZero || d.IsZero) return false;
            numerator = numerator * d + n * denominator;
            denominator *= d;
        }

        return numerator == denominator;
    }
}
