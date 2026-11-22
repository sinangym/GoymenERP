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

    /// <summary>
    /// AK-8b: the label's lineage with what a return of <paramref name="quantityKg"/> (and <paramref name="pieceCount"/> pieces of
    /// a piece-tracking label) would take from each receipt (SAT-D440, SAT-D442), each receipt's open, invoiced and pending
    /// kilograms (TI3, K3-B2) and whether the label's stock pool average is readable (SAT-D444). Price-free.
    /// </summary>
    Task<InventorySupplierReturnLineagePlanV1> PlanAsync(Guid companyId, Guid labelId, string quantityKg, int? pieceCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// SAT-D456/SAT-D460: what <paramref name="quantityKg"/> of the label is worth at its location/product moving average —
    /// the kuruş-rounded stock exit amount and the average itself — or both null when the average cannot be read. Cost-bearing:
    /// for the owner's planning only, never for a price-free preview.
    /// </summary>
    Task<InventorySupplierReturnStockAmountV1> ReadStockAmountAsync(Guid companyId, Guid labelId, string quantityKg,
        CancellationToken cancellationToken = default);

    /// <summary>AK-8b: one goods receipt's open, invoiced and pending kilograms (the chosen receipt of a quantity return).</summary>
    Task<InventorySupplierReturnReceiptStateV1> ReadReceiptAsync(Guid companyId, Guid goodsAcceptanceId,
        CancellationToken cancellationToken = default);
}

/// <summary>The kuruş-rounded stock exit amount of a quantity return at the moving average; both null when it cannot be read.</summary>
public sealed record InventorySupplierReturnStockAmountV1(Guid CompanyId, Guid LabelId, string? StockExitAmountTry, string? AverageCostTryPerKg);

/// <summary>What is left of one invoiced source of a receipt (its recognition event) for a supplier return.</summary>
public sealed record InventorySupplierReturnInvoicedPartV1(Guid SourceEventId, Guid InvoiceId, Guid InvoiceLineId,
    InventoryExactCostRatioV1 RemainingKg);

/// <summary>
/// One goods receipt as a supplier return sees it. <see cref="OpenKg"/> = received − earlier returns (SAT-D306 (a), TI3).
/// <see cref="PartsReadable"/> false means the invoiced/pending parts cannot be attributed exactly from the recorded history;
/// then <see cref="PendingRemainingKg"/> and <see cref="Invoiced"/> are empty and a return of that receipt is refused, never
/// estimated. <see cref="DistributedKg"/>/<see cref="DistributedPieces"/> are set by a plan for a lineage receipt.
/// </summary>
public sealed record InventorySupplierReturnReceiptStateV1(
    Guid CompanyId, Guid GoodsAcceptanceId, Guid ReceiptMovementId, Guid RootLabelId, DateTimeOffset ReceivedAtUtc,
    string ReceivedKg, string OpenKg, bool PartsReadable, InventoryExactCostRatioV1? PendingRemainingKg,
    IReadOnlyList<InventorySupplierReturnInvoicedPartV1> Invoiced, string? DistributedKg = null, int? DistributedPieces = null);

/// <summary>
/// The plan of a return from one source label. <see cref="Receipts"/> follow the receipt order (SAT-D440/SAT-D442 ties) and are
/// present for every purchase root; the distribution is filled only when every root is a purchase receipt.
/// <see cref="FreeKg"/>/<see cref="BlockedKg"/> are the label's stock; pieces are null on a label that tracks none.
/// <see cref="AverageCostReadable"/>: the label's location/product moving average is known and reconciles with stock (SAT-D444).
/// </summary>
public sealed record InventorySupplierReturnLineagePlanV1(
    InventorySupplierReturnLineageV1 Lineage, bool TracksPieces, string FreeKg, string BlockedKg, int? FreePieces, int? BlockedPieces,
    bool AverageCostReadable, IReadOnlyList<InventorySupplierReturnReceiptStateV1> Receipts);

/// <summary>The preparer's explicit choice inside one receipt (K3-B2): canonical kilograms from the pending part and from each
/// invoiced source. They add up to the receipt's returned kilograms.</summary>
public sealed record InventorySupplierReturnPartSelectionV1(string PendingKg,
    IReadOnlyList<InventorySupplierReturnInvoicedSelectionV1> Invoiced);
public sealed record InventorySupplierReturnInvoicedSelectionV1(Guid SourceEventId, string QuantityKg);

/// <summary>One receipt's exit of an atomic lineage return (SAT-D433). The unit cost is that receipt's TL cost per kg.</summary>
public sealed record InventorySupplierReturnLineagePartV1(
    Guid GoodsAcceptanceId, Guid SupplierReturnLineId, Guid InventoryMovementId, string QuantityKg, int? PieceCount,
    string? OriginalUnitCostTryPerKg, InventorySupplierReturnPartSelectionV1? Selection);

/// <summary>
/// AK-8b (SAT-D433, SAT-D440, SAT-D442): ONE supplier return of a label whose lineage is several receipts of the supplier. Every
/// receipt gets its own exit movement; all of them and the single command record are staged together or not at all.
/// </summary>
public sealed record InventorySupplierReturnLineageExitRequestV1(
    Guid CompanyId, Guid SupplierReturnId, Guid SourceLabelId, GoymenERP.Modules.Inventory.Domain.Stock.BlockState SourceBlock,
    string QuantityKg, int? PieceCount, DateTimeOffset OccurredAtUtc, IReadOnlyList<InventorySupplierReturnLineagePartV1> Parts);

public sealed record InventorySupplierReturnLineageExitResultV1(
    Guid CompanyId, Guid SupplierReturnId, Guid LabelId, IReadOnlyList<InventorySupplierReturnExitResultV1> Parts,
    bool IsReplay, string OriginalCorrelationId);

/// <summary>The attribution a lineage or quantity exit records with its movement, read back by later returns of the receipt.</summary>
public sealed record InventorySupplierReturnLineageSelectionProofV1(Guid GoodsAcceptanceId, Guid SourceLabelId,
    InventorySupplierReturnLineageModeV1 Mode, IReadOnlyList<InventorySupplierReturnResolvedPartV1> Selected);
public sealed record InventorySupplierReturnResolvedPartV1(Guid? SourceEventId, InventoryExactCostRatioV1 QuantityKg);

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
    /// SAT-D433 single-exit check owned by Inventory: in <see cref="InventorySupplierReturnLineageModeV1.Lineage"/> mode the
    /// source label's lineage must be exactly the named receipt's root label (share one). A lineage of several receipts is one
    /// atomic return with one exit per receipt (<see cref="IInventorySupplierReturnExitParticipantV1.StageLineageAsync"/>); a
    /// single exit naming it is a request fault (400), and any other lineage is the 409 mismatch.
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
            throw InventorySupplierReturnExitException.Refused(400, InventorySupplierReturnExitCodesV1.InvalidRequest);
        throw InventorySupplierReturnExitException.Refused(409, InventorySupplierReturnExitCodesV1.LineageMismatch);
    }
}

/// <summary>AK-8b: shape validation of an atomic lineage return and the match of its parts with the owned distribution.</summary>
public static class InventorySupplierReturnLineageExitContractV1
{
    /// <summary>Validates identities, the total and each part's shape; returns the total kilograms.</summary>
    public static decimal Validate(InventorySupplierReturnLineageExitRequestV1 request, InventoryCommandMetadata metadata,
        Guid enlistedCompanyId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        if (request.Parts is null || request.Parts.Count == 0
            || request.Parts.Count > InventorySupplierReturnExitContractV1.MaximumLineageParts
            || request.SourceLabelId == Guid.Empty || request.Parts.Any(part => part is null)
            || request.Parts.Select(part => part.GoodsAcceptanceId).Distinct().Count() != request.Parts.Count
            || request.Parts.Select(part => part.SupplierReturnLineId).Distinct().Count() != request.Parts.Count
            || request.Parts.Select(part => part.InventoryMovementId).Distinct().Count() != request.Parts.Count
            || request.Parts.Any(part => (part.PieceCount is null) != (request.PieceCount is null)))
            throw InventorySupplierReturnExitException.Refused(400, InventorySupplierReturnExitCodesV1.InvalidRequest);
        var first = request.Parts[0];
        // The whole return is validated as one exit of its total (identities, block, UTC time, metadata, canonical kg, pieces).
        var total = InventorySupplierReturnExitContractV1.Validate(new InventorySupplierReturnExitRequestV1(request.CompanyId,
            request.SupplierReturnId, first.SupplierReturnLineId, first.GoodsAcceptanceId, first.InventoryMovementId,
            request.SourceBlock, request.QuantityKg, null, request.OccurredAtUtc, PieceCount: request.PieceCount), metadata,
            enlistedCompanyId);
        if (request.PieceCount is < 1)
            throw InventorySupplierReturnExitException.Refused(400, InventorySupplierReturnExitCodesV1.InvalidQuantity);
        return total.Quantity.Value;
    }
}

/// <summary>
/// AK-8b (SAT-D433 step 2, SAT-D440, SAT-D442): Inventory owns the lineage, so it re-derives what each receipt takes and admits
/// only a request that names exactly that. Nothing is moved to another receipt and no share is rounded for the caller.
/// </summary>
public static class InventorySupplierReturnLineageDistributionV1
{
    /// <summary>
    /// The request's parts in staging order. Refusals: a lineage with a non-purchase root, parts that are not the lineage's
    /// receipts, or kilograms/pieces that differ from the owned distribution — 409 lineage mismatch; a piece return over several
    /// receipts with the SAT-D442 gate closed — 503.
    /// </summary>
    public static IReadOnlyList<InventorySupplierReturnLineagePartV1> Match(InventorySupplierReturnLineageV1 lineage,
        decimal totalKg, InventorySupplierReturnLineageExitRequestV1 request, bool? pieceGoldenOverrideForTests = null)
    {
        ArgumentNullException.ThrowIfNull(lineage);
        ArgumentNullException.ThrowIfNull(request);
        if (lineage.LabelId != request.SourceLabelId || lineage.Roots.Count == 0
            || lineage.Roots.Any(root => root.Kind != InventorySupplierReturnLineageRootKindV1.PurchaseReceipt
                || root.GoodsAcceptanceId is null))
            throw InventorySupplierReturnExitException.Refused(409, InventorySupplierReturnExitCodesV1.LineageMismatch);
        if (request.PieceCount is not null && lineage.Roots.Count > 1
            && !InventorySupplierReturnPieceDistributionV1.IsAccepted(pieceGoldenOverrideForTests))
            throw InventorySupplierReturnExitException.Refused(503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid);
        var shares = lineage.Roots.Select((root, index) => new SupplierReturnLineageShare(root.RootLabelId, index + 1, root.Share.Value()))
            .ToArray();
        IReadOnlyList<SupplierReturnLineagePart> expected;
        try { expected = SupplierReturnLineageCalculator.DistributeByShare(shares, totalKg, request.PieceCount); }
        catch (ArgumentException)
        { throw InventorySupplierReturnExitException.Refused(503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid); }
        var taking = expected.Where(part => part.QuantityKg > 0m || part.Pieces is > 0).ToArray();
        // A receipt whose share rounds to no kilograms cannot carry pieces alone (KLN-D119: pieces and kilograms move together).
        if (taking.Any(part => part.QuantityKg <= 0m) || taking.Length != request.Parts.Count)
            throw InventorySupplierReturnExitException.Refused(409, InventorySupplierReturnExitCodesV1.LineageMismatch);
        var ordered = new List<(InventorySupplierReturnLineagePartV1 Part, int Pieces, int Order)>();
        foreach (var part in taking)
        {
            var root = lineage.Roots.Single(candidate => candidate.RootLabelId == part.RootId);
            var named = request.Parts.SingleOrDefault(candidate => candidate.GoodsAcceptanceId == root.GoodsAcceptanceId);
            if (named is null || named.PieceCount != part.Pieces
                || !string.Equals(named.QuantityKg, part.QuantityKg.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                    StringComparison.Ordinal))
                throw InventorySupplierReturnExitException.Refused(409, InventorySupplierReturnExitCodesV1.LineageMismatch);
            ordered.Add((named, part.Pieces ?? 0, part.ReceiptOrder));
        }

        // Fewest pieces first: a piece-tracking bucket never holds kilograms with zero pieces between two exits of one return.
        return ordered.OrderBy(item => item.Pieces).ThenBy(item => item.Order).Select(item => item.Part).ToArray();
    }
}
