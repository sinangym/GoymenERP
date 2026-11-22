using System.Globalization;
using GoymenERP.BuildingBlocks.Quantities;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Returns;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Purchasing.Application;

/// <summary>
/// AK-8 / AK-8b (SAT-D433, SAT-D440–SAT-D445, K3-B2): a supplier return that names a source label other than the receipt's own
/// label. Behind <see cref="PurchaseSupplierReturnLineageGoldenV1"/>; with it closed the request is refused 503 before anything is
/// read. The lineage, its distribution and each receipt's open parts come from Inventory's read port; each purchase root's
/// supplier and order from Purchasing's own acceptances.
/// <list type="bullet">
/// <item><b>Lineage</b> (only the returning supplier's receipts): every receipt of the lineage takes its share (SAT-D440 kg,
/// SAT-D442 pieces) — one stock exit per receipt, ONE atomic return document under one idempotency key. Each receipt's exit is
/// valued at that receipt's invoice unit cost (SAT-D392) or, when it cannot be read, at a manual cost that waits for another
/// person's approval (SAT-D393); one pending part makes the whole document wait and nothing leaves stock.</item>
/// <item><b>Quantity</b> (any other lineage): the named acceptance of this order is the chosen receipt; the supplier price is the
/// order line's net unit price after its discount, without the deferral (SAT-D451,
/// <see cref="PurchaseSupplierReturnQuantityPriceRuleV1"/>). Stock leaves at the moving average; when
/// that cannot be read the SAT-D393 exception path applies and no value is written without the approval.</item>
/// </list>
/// Inside every receipt the invoiced / invoice-pending part is the preparer's explicit choice (K3-B2); a receipt's open quantity
/// bounds its share (TI3) and nothing is moved to another receipt.
/// </summary>
public sealed partial class PurchasingService
{
    private bool QuantityRulesAccepted => (returnPolicy ?? PurchaseSupplierReturnPolicyV1.Default).QuantityRulesGoldenAccepted;

    private sealed record ReturnLineageRow(Guid GoodsAcceptanceId, PurchaseSupplierReturnAcceptanceOwnerV1 Owner, decimal QuantityKg,
        int? Pieces, PurchaseSupplierReturnReceiptStateV1 Receipt, bool SelectionRequired, decimal? InvoiceCost, string CostSource);

    private sealed record ReturnLineagePlan(PurchaseSupplierReturnLineageModeV1 Mode, PurchaseSupplierReturnLineageV1 Lineage,
        IReadOnlyList<ReturnLineageRow> Rows, PurchaseSupplierReturnQuantityPriceV1? QuantityPrice, PurchasingServiceException? Refusal);

    private sealed record ReturnLineageDecision(PurchaseSupplierReturnLineageModeV1 Mode, PurchaseSupplierReturnStatus Status,
        PurchaseSupplierReturnCostBasis CostBasis, decimal UnitCostTryPerKg, IReadOnlyList<PurchaseSupplierReturnLineagePartV1> Parts);

    /// <summary>
    /// The early gate and shape checks, before replay lookup or any read. Gate closed: the earlier 503. A source-label return
    /// never adjusts the receipt and never carries the receipt-label cost allocation: that combination is a request fault (400).
    /// </summary>
    private void RequireReturnLineageAdmissible(CreatePurchaseSupplierReturnV1 request)
    {
        if (request.SourceLabelId is null) return;
        if (!(returnPolicy ?? PurchaseSupplierReturnPolicyV1.Default).LineageGoldenAccepted)
            throw PurchaseSupplierReturnLineagePlannerV1.Unavailable();
        if (request.ExpectedPurchaseOrderVersion is not null || request.CostAllocation is not null)
            throw new PurchasingServiceException(400, "validation_failed");
    }

    public async Task<PurchaseSupplierReturnLineagePreviewV1> GetSupplierReturnLineagePreviewAsync(Guid orderId, Guid goodsAcceptanceId,
        Guid sourceLabelId, string quantityKg, int? pieceCount, CancellationToken cancellationToken = default)
    {
        var permission = bindings.ReadPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (orderId == Guid.Empty || goodsAcceptanceId == Guid.Empty) throw new PurchasingServiceException(404, "not_found");
        if (sourceLabelId == Guid.Empty || !PurchaseSupplierReturnJsonV1.IsPositiveQuantity(quantityKg) || pieceCount is < 1)
            throw new PurchasingServiceException(400, "validation_failed");
        if (!(returnPolicy ?? PurchaseSupplierReturnPolicyV1.Default).LineageGoldenAccepted)
            throw PurchaseSupplierReturnLineagePlannerV1.Unavailable();
        var returns = returnStore ?? throw ReturnUnavailable();
        var stored = await GuardedAsync(scope, permission, () => store.FindOrderAsync(orderId, cancellationToken), cancellationToken)
            ?? throw new PurchasingServiceException(404, "not_found");
        var lineId = await GuardedAsync(scope, permission,
                () => returns.FindAcceptanceLineAsync(orderId, goodsAcceptanceId, cancellationToken), cancellationToken)
            ?? throw new PurchasingServiceException(404, "not_found");
        var plan = await PlanReturnLineageAsync(scope, permission, returns, stored.Order, lineId, sourceLabelId, goodsAcceptanceId,
            quantityKg, pieceCount, collectRefusal: true, cancellationToken);
        await RequireStillAsync(scope, permission, cancellationToken);
        return new PurchaseSupplierReturnLineagePreviewV1(scope.CompanyId, orderId, goodsAcceptanceId, sourceLabelId, plan.Mode.ToString(),
            quantityKg, pieceCount, plan.Lineage.TracksPieces, plan.Lineage.FreeKg, plan.Lineage.BlockedKg, plan.Lineage.FreePieces,
            plan.Lineage.BlockedPieces,
            plan.Rows.Select(row => new PurchaseSupplierReturnLineagePreviewPartV1(row.GoodsAcceptanceId, row.Owner.PurchaseOrderId,
                row.Owner.PurchaseOrderLineId, PurchaseSupplierReturnLineagePartsV1.Kg(row.QuantityKg), row.Pieces, row.CostSource,
                row.Receipt.ReceivedKg, row.Receipt.OpenKg, row.Receipt.PendingRemainingKg ?? "0", row.Receipt.Invoiced,
                row.SelectionRequired)).ToArray(),
            plan.Refusal?.Code);
    }

    /// <summary>
    /// Plans the return from the owner facts. With <paramref name="collectRefusal"/> (the preview) a 409/422 found while planning
    /// is kept beside the rows instead of thrown, so the preparer sees which receipt stops the return; a 503 is always thrown.
    /// </summary>
    private async Task<ReturnLineagePlan> PlanReturnLineageAsync(Scope scope, string? permission, IPurchaseSupplierReturnStore returns,
        PurchaseOrder order, Guid lineId, Guid sourceLabelId, Guid goodsAcceptanceId, string quantityKg, int? pieceCount,
        bool collectRefusal, CancellationToken cancellationToken)
    {
        var port = returnLineage ?? throw PurchaseSupplierReturnLineagePlannerV1.Unavailable();
        var lineage = await GuardedAsync(scope, permission,
            () => port.PlanAsync(scope.CompanyId, sourceLabelId, quantityKg, pieceCount, cancellationToken), cancellationToken);
        if (lineage is null || lineage.LabelId != sourceLabelId || lineage.Roots is null)
            throw PurchaseSupplierReturnLineagePlannerV1.Unavailable();
        if (lineage.TracksPieces != (pieceCount is not null))
        {
            // KLN-D119: a piece-tracking label returns whole pieces with its kilograms; a kg-only label carries no piece count.
            throw new PurchasingServiceException(422, PurchaseSupplierReturnCodesV1.PieceCountRequired,
                [new("pieceCount", PurchaseSupplierReturnCodesV1.PieceCountRequired)]);
        }

        var owners = new Dictionary<Guid, PurchaseSupplierReturnAcceptanceOwnerV1?>();
        foreach (var acceptance in lineage.Roots.Where(root => root?.GoodsAcceptanceId is not null)
                     .Select(root => root.GoodsAcceptanceId!.Value).Distinct())
        {
            owners[acceptance] = await GuardedAsync(scope, permission,
                () => returns.FindAcceptanceOwnerAsync(acceptance, cancellationToken), cancellationToken);
        }

        var mode = PurchaseSupplierReturnLineagePlannerV1.Plan(lineage,
            owners.ToDictionary(pair => pair.Key, pair => pair.Value?.SupplierPartyId), order.SupplierPartyId, goodsAcceptanceId).Mode;
        PurchasingServiceException? refusal = null;
        void Refuse(PurchasingServiceException exception)
        {
            if (!collectRefusal || exception.Status == 503) throw exception;
            refusal ??= exception;
        }

        var total = PurchaseSupplierReturnLineagePartsV1.ParseKg(quantityKg);
        var rows = new List<ReturnLineageRow>();
        PurchaseSupplierReturnQuantityPriceV1? quantityPrice = null;
        if (mode == PurchaseSupplierReturnLineageModeV1.Lineage)
        {
            foreach (var root in lineage.Roots)
            {
                var receipt = root.Receipt;
                if (receipt is null || receipt.GoodsAcceptanceId != root.GoodsAcceptanceId
                    || !PurchaseSupplierReturnLineagePartsV1.TryKg(receipt.DistributedKg, out var kg)
                    || (receipt.DistributedPieces is null) != (pieceCount is null) || receipt.DistributedPieces is < 0)
                    throw PurchaseSupplierReturnLineagePlannerV1.Unavailable();
                if (kg == 0m)
                {
                    // A share that rounds to no kilograms takes nothing; it can never carry pieces alone (KLN-D119).
                    if (receipt.DistributedPieces is > 0) Refuse(PartsMismatch());
                    continue;
                }

                var invoiceCost = await ReadReturnInvoiceCostAsync(scope, permission, receipt.GoodsAcceptanceId, cancellationToken);
                rows.Add(Row(receipt, owners[receipt.GoodsAcceptanceId]!, kg, receipt.DistributedPieces, invoiceCost,
                    invoiceCost is null ? PurchaseSupplierReturnCostSourceV1.ManualCostException : PurchaseSupplierReturnCostSourceV1.InvoiceUnitCost,
                    Refuse));
            }

            if (rows.Sum(row => row.QuantityKg) != total || pieceCount is { } pieces && rows.Sum(row => row.Pieces ?? 0) != pieces)
                throw PurchaseSupplierReturnLineagePlannerV1.Unavailable();
            if (rows.All(row => row.GoodsAcceptanceId != goodsAcceptanceId))
                Refuse(new PurchasingServiceException(409, PurchaseSupplierReturnCodesV1.LineageReceiptMismatch,
                    [new("goodsAcceptanceId", PurchaseSupplierReturnCodesV1.LineageReceiptMismatch)]));
        }
        else
        {
            var receipt = await GuardedAsync(scope, permission,
                () => port.ReadReceiptAsync(scope.CompanyId, goodsAcceptanceId, cancellationToken), cancellationToken);
            if (receipt is null || receipt.GoodsAcceptanceId != goodsAcceptanceId) throw PurchaseSupplierReturnLineagePlannerV1.Unavailable();
            var price = PurchaseSupplierReturnQuantityPriceRuleV1.Resolve(QuantityPriceLine(order, lineId), QuantityRulesAccepted);
            switch (price.Outcome)
            {
                case PurchaseSupplierReturnQuantityPriceOutcomeV1.Priced:
                    quantityPrice = price;
                    break;
                case PurchaseSupplierReturnQuantityPriceOutcomeV1.NotAKilogramTryLine:
                    Refuse(new PurchasingServiceException(409, PurchaseSupplierReturnCodesV1.LineageReceiptMismatch,
                        [new("goodsAcceptanceId", PurchaseSupplierReturnCodesV1.LineageReceiptMismatch)]));
                    break;
                case PurchaseSupplierReturnQuantityPriceOutcomeV1.DocumentDiscount:
                    Refuse(new PurchasingServiceException(422, PurchaseSupplierReturnCodesV1.QuantityDocumentDiscount,
                        [new("goodsAcceptanceId", PurchaseSupplierReturnCodesV1.QuantityDocumentDiscount)]));
                    break;
                default:
                    // The net price does not fit four decimals; its rounding is not decided (never rounded here).
                    Refuse(new PurchasingServiceException(422, PurchaseSupplierReturnCodesV1.QuantityPriceUndecided,
                        [new("goodsAcceptanceId", PurchaseSupplierReturnCodesV1.QuantityPriceUndecided)]));
                    break;
            }

            rows.Add(Row(receipt, new(order.Id, lineId, order.SupplierPartyId), total, pieceCount, null,
                lineage.AverageCostReadable ? PurchaseSupplierReturnCostSourceV1.MovingAverage : PurchaseSupplierReturnCostSourceV1.ManualCostException,
                Refuse));
        }

        return new ReturnLineagePlan(mode, lineage, rows, quantityPrice, refusal);
    }

    /// <summary>TI3 for one receipt: an unreadable part history is 503; a share above the receipt's open quantity is refused.</summary>
    private static ReturnLineageRow Row(PurchaseSupplierReturnReceiptStateV1 receipt, PurchaseSupplierReturnAcceptanceOwnerV1 owner,
        decimal kg, int? pieces, decimal? invoiceCost, string costSource, Action<PurchasingServiceException> refuse)
    {
        if (!receipt.PartsReadable || !PurchaseSupplierReturnLineagePartsV1.TryKg(receipt.OpenKg, out var open)
            || !PurchaseSupplierReturnLineagePartsV1.TryKg(receipt.PendingRemainingKg, out var pending) || receipt.Invoiced is null
            || receipt.Invoiced.Any(item => item is null || item.SourceEventId == Guid.Empty
                || !PurchaseSupplierReturnLineagePartsV1.TryKg(item.RemainingKg, out _))
            || receipt.Invoiced.Select(item => item.SourceEventId).Distinct().Count() != receipt.Invoiced.Count)
            throw PurchaseSupplierReturnLineagePlannerV1.Unavailable();
        if (kg > open) refuse(LimitExceeded());
        var openParts = (pending > 0m ? 1 : 0)
            + receipt.Invoiced.Count(item => PurchaseSupplierReturnLineagePartsV1.ParseKg(item.RemainingKg) > 0m);
        return new ReturnLineageRow(receipt.GoodsAcceptanceId, owner, kg, pieces, receipt, openParts > 1, invoiceCost, costSource);
    }

    /// <summary>
    /// Decides the parts of the command from its plan: the posted parts must be exactly the plan's receipts, each explicit
    /// selection must add up to the receipt's share and stay inside what is left of each part, and each part's cost follows
    /// SAT-D392/SAT-D393 (lineage) or SAT-D444 (quantity).
    /// </summary>
    private static ReturnLineageDecision DecideReturnLineage(ReturnLineagePlan plan, CreatePurchaseSupplierReturnV1 request,
        bool quantityRulesAccepted)
    {
        var posted = request.LineageParts;
        if (posted is null && plan.Rows.Count != 1 || posted is not null && (posted.Count != plan.Rows.Count
                || plan.Rows.Any(row => posted.All(part => part.GoodsAcceptanceId != row.GoodsAcceptanceId))))
            throw PartsMismatch();
        // SAT-D455: the document's piece quantity belongs to a quantity return from a piece order line only.
        if (request.SupplierPieceQuantity is not null && plan.Mode != PurchaseSupplierReturnLineageModeV1.Quantity)
            throw new PurchasingServiceException(400, "validation_failed");
        var single = plan.Rows.Count == 1;
        if (!single && request.ManualUnitCostTryPerKg is not null
            || plan.Mode == PurchaseSupplierReturnLineageModeV1.Quantity && posted?[0].ManualUnitCostTryPerKg is not null)
            throw new PurchasingServiceException(400, "validation_failed");
        var parts = new List<PurchaseSupplierReturnLineagePartV1>();
        foreach (var row in plan.Rows)
        {
            var part = posted?.Single(candidate => candidate.GoodsAcceptanceId == row.GoodsAcceptanceId)
                ?? new PurchaseSupplierReturnLineagePartRequestV1(request.GoodsAcceptanceId, request.ReturnLineId, request.InventoryMovementId);
            RequireSelection(row, part);
            var manualText = part.ManualUnitCostTryPerKg ?? (single ? request.ManualUnitCostTryPerKg : null);
            if (part.ManualUnitCostTryPerKg is not null && single && request.ManualUnitCostTryPerKg is not null
                && part.ManualUnitCostTryPerKg != request.ManualUnitCostTryPerKg)
                throw new PurchasingServiceException(400, "validation_failed");
            var manual = manualText is null ? (decimal?)null : ParseUnitCost(manualText);
            PurchaseSupplierReturnCostBasis basis;
            decimal unitCost;
            decimal? supplierPrice = null, supplierAmount = null;
            int? supplierPieces = null;
            if (plan.Mode == PurchaseSupplierReturnLineageModeV1.Quantity)
            {
                var price = plan.QuantityPrice ?? throw PurchaseSupplierReturnLineagePlannerV1.Unavailable();
                var unitPrice = price.UnitPriceTry ?? throw PurchaseSupplierReturnLineagePlannerV1.Unavailable();
                // SAT-D455: a piece order line bills the pieces the preparer enters × the net piece price; a kg line never names them.
                if (price.PerPiece != (request.SupplierPieceQuantity is not null))
                    throw price.PerPiece
                        ? new PurchasingServiceException(422, PurchaseSupplierReturnCodesV1.PieceCountRequired,
                            [new("supplierPieceQuantity", PurchaseSupplierReturnCodesV1.PieceCountRequired)])
                        : new PurchasingServiceException(400, "validation_failed");
                // SAT-D459: the document's pieces are the pieces that leave the label; anything else is refused.
                if (price.PerPiece && request.SupplierPieceQuantity != request.PieceCount)
                    throw new PurchasingServiceException(422, PurchaseSupplierReturnCodesV1.QuantityPieceQuantityMismatch,
                        [new("supplierPieceQuantity", PurchaseSupplierReturnCodesV1.QuantityPieceQuantityMismatch)]);
                supplierPieces = price.PerPiece ? request.SupplierPieceQuantity : null;
                if (quantityRulesAccepted)
                {
                    // SAT-D453/SAT-D456: the document amount is the quantity × the rounded net price, rounded once to kuruş;
                    // Inventory measures the difference between the kuruş-rounded stock exit amount and this amount.
                    supplierPrice = unitPrice;
                    supplierAmount = PurchaseSupplierReturnQuantityPriceRuleV1.Amount(supplierPieces ?? row.QuantityKg, unitPrice);
                    if (supplierAmount <= 0m) throw PurchaseSupplierReturnLineagePlannerV1.Unavailable();
                }

                if (row.CostSource == PurchaseSupplierReturnCostSourceV1.MovingAverage)
                {
                    if (manual is not null)
                        throw new PurchasingServiceException(409, PurchaseSupplierReturnCodesV1.QuantityAverageAvailable,
                            [new("manualUnitCostTryPerKg", PurchaseSupplierReturnCodesV1.QuantityAverageAvailable)]);
                    // Gate closed: the kg price itself is the stock cost Inventory compares with the average. Gate open: the
                    // stock leaves at the average Inventory reads; the price here is replaced by it before the document is saved.
                    (basis, unitCost) = (PurchaseSupplierReturnCostBasis.InvoiceUnitCost, unitPrice);
                }
                else
                {
                    // SAT-D393: the average cannot be read; the manual cost waits for another person's approval. SAT-D454 (behind
                    // the gate): it may differ from the order price — stock leaves at the approved manual cost, the supplier is
                    // billed at the order's net price, both recorded. Gate closed: a differing manual cost is refused (SAT-D371).
                    if (manual is null)
                        throw new PurchasingServiceException(409, PurchaseSupplierReturnCodesV1.CostRequired,
                            [new("manualUnitCostTryPerKg", PurchaseSupplierReturnCodesV1.CostRequired)]);
                    if (!quantityRulesAccepted && manual != unitPrice)
                        throw new PurchasingServiceException(409, PurchaseSupplierReturnCodesV1.QuantityManualCostDifference,
                            [new("manualUnitCostTryPerKg", PurchaseSupplierReturnCodesV1.QuantityManualCostDifference)]);
                    (basis, unitCost) = (PurchaseSupplierReturnCostBasis.ManualCostWithException, manual.Value);
                }
            }
            else if (row.InvoiceCost is { } invoiced)
            {
                if (manual is not null)
                    throw new PurchasingServiceException(409, PurchaseSupplierReturnCodesV1.InvoiceCostAvailable,
                        [new("manualUnitCostTryPerKg", PurchaseSupplierReturnCodesV1.InvoiceCostAvailable)]);
                (basis, unitCost) = (PurchaseSupplierReturnCostBasis.InvoiceUnitCost, invoiced);
            }
            else
            {
                if (manual is null)
                    throw new PurchasingServiceException(409, PurchaseSupplierReturnCodesV1.CostRequired,
                        [new("manualUnitCostTryPerKg", PurchaseSupplierReturnCodesV1.CostRequired)]);
                (basis, unitCost) = (PurchaseSupplierReturnCostBasis.ManualCostWithException, manual.Value);
            }

            parts.Add(new PurchaseSupplierReturnLineagePartV1(row.GoodsAcceptanceId, row.Owner.PurchaseOrderId, row.Owner.PurchaseOrderLineId,
                part.ReturnLineId, part.InventoryMovementId, row.QuantityKg, basis, unitCost, row.Pieces, part.PendingKg, part.Invoiced,
                supplierPrice, supplierPrice is null ? null : supplierPieces, supplierAmount));
        }

        var anchor = parts.Single(part => part.GoodsAcceptanceId == request.GoodsAcceptanceId);
        var pending = parts.Any(part => part.CostBasis == PurchaseSupplierReturnCostBasis.ManualCostWithException);
        return new ReturnLineageDecision(plan.Mode,
            pending ? PurchaseSupplierReturnStatus.PendingCostException : PurchaseSupplierReturnStatus.Recorded,
            pending ? PurchaseSupplierReturnCostBasis.ManualCostWithException : PurchaseSupplierReturnCostBasis.InvoiceUnitCost,
            anchor.UnitCostTryPerKg, parts);
    }

    /// <summary>K3-B2 / TI3: the explicit choice adds up to the share and stays inside each part; several open parts need a choice.</summary>
    private static void RequireSelection(ReturnLineageRow row, PurchaseSupplierReturnLineagePartRequestV1 part)
    {
        if (part.PendingKg is null)
        {
            if (row.SelectionRequired)
                throw new PurchasingServiceException(409, PurchaseSupplierReturnCodesV1.LineageSelectionRequired,
                    [new("lineageParts", PurchaseSupplierReturnCodesV1.LineageSelectionRequired)]);
            return;
        }

        var pending = PurchaseSupplierReturnLineagePartsV1.ParseKg(part.PendingKg);
        var invoiced = part.Invoiced!;
        if (invoiced.Any(item => row.Receipt.Invoiced.All(source => source.SourceEventId != item.SourceEventId))
            || pending + invoiced.Sum(item => PurchaseSupplierReturnLineagePartsV1.ParseKg(item.QuantityKg)) != row.QuantityKg)
            throw PartsMismatch();
        if (pending > PurchaseSupplierReturnLineagePartsV1.ParseKg(row.Receipt.PendingRemainingKg!)
            || invoiced.Any(item => PurchaseSupplierReturnLineagePartsV1.ParseKg(item.QuantityKg) > PurchaseSupplierReturnLineagePartsV1.ParseKg(
                row.Receipt.Invoiced.Single(source => source.SourceEventId == item.SourceEventId).RemainingKg)))
            throw LimitExceeded();
    }

    private static PurchasingServiceException PartsMismatch() => new(409, PurchaseSupplierReturnCodesV1.LineagePartsMismatch,
        [new("lineageParts", PurchaseSupplierReturnCodesV1.LineagePartsMismatch)]);

    private static PurchasingServiceException LimitExceeded() => new(409, PurchaseSupplierReturnCodesV1.LineageReceiptLimitExceeded,
        [new("quantityKg", PurchaseSupplierReturnCodesV1.LineageReceiptLimitExceeded)]);

    /// <summary>The facts <see cref="PurchaseSupplierReturnQuantityPriceRuleV1"/> needs of the chosen order line (SAT-D444).</summary>
    private static PurchaseSupplierReturnQuantityPriceLineV1 QuantityPriceLine(PurchaseOrder order, Guid lineId)
    {
        var line = order.Lines.SingleOrDefault(candidate => candidate.Id == lineId) ?? throw ReturnUnavailable();
        var price = line.Price;
        return new PurchaseSupplierReturnQuantityPriceLineV1(
            order.Money is { } money && money.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira,
            line.Quantity.Unit == QuantityUnit.Kilogram, price is not null && price.PriceUnitId == line.UnitId, price?.UnitPrice,
            // SAT-D451: the discount is part of the price; the deferral (maturity difference) is deliberately not passed.
            price?.DiscountPercent, price?.DiscountAmount, line.Quantity.Unit == QuantityUnit.Piece);
    }
}
