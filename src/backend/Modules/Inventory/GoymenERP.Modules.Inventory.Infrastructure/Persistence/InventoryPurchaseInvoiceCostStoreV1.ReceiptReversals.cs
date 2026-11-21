using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

public sealed partial class InventoryPurchaseInvoiceCostStoreV1
{
    private async Task<ReceiptCostBasis> ReadReceiptCostBasisAsync(InventoryGoodsReceiptRequestV1 receipt,
        InventoryPurchaseInvoiceCostReceiptV1 share, CancellationToken token)
    {
        var originalKg = ExactCostRatio.Of(decimal.Parse(receipt.QuantityKg, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture));
        var reversals = await db.GoodsReceiptReversals.AsNoTracking().Where(row => row.AcceptanceId == share.AcceptanceId)
            .OrderBy(row => row.Id).ToListAsync(token);
        var exits = await (from movement in db.Movements.AsNoTracking()
            join line in db.MovementLines.AsNoTracking() on movement.Id equals line.MovementId
            where movement.SourceId == receipt.MovementId
                && (movement.Kind == (int)StockMovementKind.SupplierReturnExit
                    || movement.Kind == (int)StockMovementKind.PurchaseAcceptanceReversal)
            select new { Movement = movement, Line = line }).ToListAsync(token);
        // A legacy event has no undo quantity proof. Never infer purchase units from separately measured kg,
        // nor turn a valid remaining invoice into a permanent poison event just because that proof is missing.
        var adjustments = share.ReceiptAdjustments ?? [];
        if (reversals.Count != adjustments.Count || exits.Count != reversals.Count)
            throw Unavailable("purchase_invoice_cost_reversal_source_unavailable");
        var proof = new List<InventoryPurchaseFirstCostReceiptReversalV1>();
        foreach (var reversal in reversals)
        {
            var adjustment = adjustments.SingleOrDefault(x => x.AdjustmentId == reversal.Id);
            var physical = exits.SingleOrDefault(x => x.Movement.Id == reversal.MovementId);
            var isReturn = reversal.Kind == (int)InventoryGoodsReceiptReversalKindV1.SupplierReturn;
            var kind = isReturn ? StockMovementKind.SupplierReturnExit : StockMovementKind.PurchaseAcceptanceReversal;
            var sourceKind = isReturn ? InventorySupplierReturnExitContractV1.SourceKind : InventoryGoodsReceiptReversalCodesV1.SourceKind;
            if (adjustment is null || physical is null || adjustment.IsSupplierReturn != isReturn
                || !Enum.IsDefined((InventoryGoodsReceiptReversalKindV1)reversal.Kind)
                || reversal.OriginalMovementId != receipt.MovementId || reversal.LabelId != receipt.LabelId || reversal.QuantityKg <= 0m
                || physical.Movement.Kind != (int)kind || physical.Movement.SourceKind != sourceKind
                || physical.Line.LineNumber != 1 || physical.Line.Direction != (int)StockDirection.Out
                || physical.Line.LabelId != receipt.LabelId || physical.Line.ProductId != receipt.ProductId
                || physical.Line.QuantityKg != reversal.QuantityKg || physical.Line.OwnerKind != (int)receipt.OwnerKind
                || physical.Line.OwnerId != receipt.OwnerId || physical.Line.CustodianKind != (int)receipt.CustodianKind
                || physical.Line.CustodianId != receipt.CustodianId)
                throw Unavailable("purchase_invoice_cost_reversal_source_unavailable");
            proof.Add(new(reversal.Id, reversal.MovementId, (InventoryGoodsReceiptReversalKindV1)reversal.Kind,
                adjustment.UndoQuantity, reversal.QuantityKg));
        }
        var eligibleKg = proof.Aggregate(originalKg, (remaining, value) => remaining.Subtract(ExactCostRatio.Of(value.QuantityKg)));
        var eligibleQuantity = proof.Aggregate(ExactCostRatio.Of(share.AcceptedQuantity),
            (remaining, value) => remaining.Subtract(ExactCostRatio.Of(value.UndoQuantity)));
        if (eligibleKg.Sign <= 0 || eligibleQuantity.Sign <= 0) throw Invalid("purchase_invoice_cost_receipt_reversed");
        InventorySupplierReturnCostSourceReader.Snapshot? cohort = null;
        if (await InventorySupplierReturnCostSourceReader.ReadStoredPurchaseSourceAsync(db, receipt.MovementId, share.AcceptanceId, token) is not null)
            cohort = await new InventorySupplierReturnCostSourceReader(db, company).ReadAsync(receipt.CompanyId, share.AcceptanceId,
                new(share.AcceptedQuantity, "purchase-invoice-frozen-adjustments.v1", adjustments), token);
        return new(originalKg, eligibleKg, eligibleQuantity, proof, cohort);
    }

    private sealed record ReceiptCostBasis(ExactCostRatio OriginalKg, ExactCostRatio EligibleKg,
        ExactCostRatio EligibleQuantity, IReadOnlyList<InventoryPurchaseFirstCostReceiptReversalV1> Reversals,
        InventorySupplierReturnCostSourceReader.Snapshot? Cohort);

    internal static InventoryPurchaseFirstCostReceiptV1 FirstCostReceipt(PurchaseInvoiceReceiptCostEffectRow effect)
    {
        var saved = JsonSerializer.Deserialize<FirstCostEffect>(effect.ResultJson, Json)?.Receipt
            ?? throw Unavailable("purchase_first_cost_share_unreadable");
        if (saved.AcceptanceId != effect.AcceptanceId || saved.ReceiptMovementId != effect.MovementId
            || saved.MatchedQuantity != effect.MatchedQuantity || saved.AcceptedQuantity != effect.AcceptedQuantity
            || Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(saved.PhysicalSourceJson))) != saved.PhysicalSourceSha256)
            throw Unavailable("purchase_first_cost_share_unreadable");
        return saved;
    }

    internal static InventoryPurchaseFirstCostPhysicalSourceV1 FirstCostPhysicalSource(PurchaseInvoiceReceiptCostEffectRow effect)
    {
        var saved = FirstCostReceipt(effect);
        return JsonSerializer.Deserialize<InventoryPurchaseFirstCostPhysicalSourceV1>(saved.PhysicalSourceJson, Json)
            ?? throw Unavailable("purchase_first_cost_share_unreadable");
    }

    internal static ExactCostRatio FirstCostReceiptFraction(PurchaseInvoiceReceiptCostEffectRow effect)
    {
        var physical = FirstCostPhysicalSource(effect);
        var eligible = (physical.ReceiptReversals ?? []).Aggregate(ExactCostRatio.Of(effect.AcceptedQuantity),
            (value, reversal) => value.Subtract(ExactCostRatio.Of(reversal.UndoQuantity)));
        if (eligible.Sign <= 0) throw Unavailable("purchase_first_cost_share_unreadable");
        return ExactCostRatio.Of(effect.MatchedQuantity).Divide(eligible);
    }

    private sealed record FirstCostEffect(InventoryPurchaseFirstCostReceiptV1 Receipt);
}
