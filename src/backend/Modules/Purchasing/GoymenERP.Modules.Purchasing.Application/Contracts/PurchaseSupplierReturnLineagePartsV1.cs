using System.Globalization;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Purchasing.Domain.Returns;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

// AK-8b (SAT-D433, SAT-D440–SAT-D445, K3-B2): the per-receipt parts of a supplier return that leaves a foreign source label.
// A lineage return takes from every receipt in the label's lineage (one stock exit per receipt, ONE atomic return document);
// inside each receipt the invoiced / invoice-pending part is the preparer's explicit choice. Kilograms are canonical texts.

/// <summary>Kilograms taken from one invoiced source (its recognition event) of a receipt.</summary>
public sealed record PurchaseSupplierReturnInvoicedSelectionV1(Guid SourceEventId, string QuantityKg);

/// <summary>
/// One receipt's part as the preparer posts it. Ids are client-made. <see cref="PendingKg"/> and <see cref="Invoiced"/> are the
/// explicit K3-B2 choice (both null when the receipt has a single open part: the choice is then implied, never guessed).
/// <see cref="ManualUnitCostTryPerKg"/> is only allowed when that receipt's invoice unit cost cannot be read (SAT-D393).
/// The kilograms and pieces of the part are not posted: they are the label's distribution (SAT-D440, SAT-D442).
/// </summary>
public sealed record PurchaseSupplierReturnLineagePartRequestV1(
    Guid GoodsAcceptanceId, Guid ReturnLineId, Guid InventoryMovementId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PendingKg = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PurchaseSupplierReturnInvoicedSelectionV1>? Invoiced = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ManualUnitCostTryPerKg = null);

/// <summary>One receipt's part as the service decided and stored it.</summary>
public sealed record PurchaseSupplierReturnLineagePartV1(
    Guid GoodsAcceptanceId, Guid PurchaseOrderId, Guid PurchaseOrderLineId, Guid ReturnLineId, Guid InventoryMovementId,
    decimal QuantityKg, PurchaseSupplierReturnCostBasis CostBasis, decimal UnitCostTryPerKg,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PieceCount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PendingKg = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PurchaseSupplierReturnInvoicedSelectionV1>? Invoiced = null,
    // SAT-D454/SAT-D455 (behind the quantity-rules gate; null on every earlier return): the price the supplier is billed at when
    // it is recorded apart from the stock unit cost — TL per kg, or TL per piece with the returned document pieces.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? SupplierUnitPriceTry = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? SupplierPieceQuantity = null,
    // SAT-D453/SAT-D456: the kuruş-rounded amount the document bills (quantity × the rounded net price).
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? SupplierAmountTry = null);

/// <summary>One receipt's part as it is shown; the unit cost is withheld without a fresh price-read grant (ALS-D70).</summary>
public sealed record PurchaseSupplierReturnLineagePartViewV1(
    Guid GoodsAcceptanceId, Guid PurchaseOrderId, Guid PurchaseOrderLineId, Guid ReturnLineId, Guid InventoryMovementId,
    string QuantityKg, string CostBasis, string? UnitCostTryPerKg,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PieceCount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PendingKg = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PurchaseSupplierReturnInvoicedSelectionV1>? Invoiced = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SupplierUnitPriceTry = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? SupplierPieceQuantity = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SupplierAmountTry = null);

/// <summary>The order, order line and supplier a recorded goods acceptance counts on.</summary>
public sealed record PurchaseSupplierReturnAcceptanceOwnerV1(Guid PurchaseOrderId, Guid PurchaseOrderLineId, Guid SupplierPartyId);

/// <summary>What is left of one invoiced source of a receipt; the remaining kilograms are cut down to three decimals.</summary>
public sealed record PurchaseSupplierReturnInvoicedPartV1(Guid SourceEventId, Guid InvoiceId, Guid InvoiceLineId, string RemainingKg);

/// <summary>
/// One goods receipt as the Inventory owner answers it for a return: received and open kilograms (TI3), the invoice-pending and
/// invoiced parts left (K3-B2) and, in a lineage plan, what this return takes from it (SAT-D440, SAT-D442).
/// <see cref="PartsReadable"/> false: the parts cannot be attributed from the recorded history; a return of it is refused.
/// </summary>
public sealed record PurchaseSupplierReturnReceiptStateV1(
    Guid GoodsAcceptanceId, string ReceivedKg, string OpenKg, bool PartsReadable, string? PendingRemainingKg,
    IReadOnlyList<PurchaseSupplierReturnInvoicedPartV1> Invoiced,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DistributedKg = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? DistributedPieces = null);

/// <summary>Where a part's stock cost comes from; a name, never an amount.</summary>
public static class PurchaseSupplierReturnCostSourceV1
{
    /// <summary>SAT-D392: the invoice unit cost of the receipt is readable.</summary>
    public const string InvoiceUnitCost = "InvoiceUnitCost";

    /// <summary>SAT-D393: it cannot be read; a manual cost and another person's approval are needed.</summary>
    public const string ManualCostException = "ManualCostException";

    /// <summary>SAT-D444: the location/product moving average (quantity return).</summary>
    public const string MovingAverage = "MovingAverage";
}

/// <summary>One receipt row of the preview.</summary>
public sealed record PurchaseSupplierReturnLineagePreviewPartV1(
    Guid GoodsAcceptanceId, Guid PurchaseOrderId, Guid PurchaseOrderLineId, string QuantityKg, int? PieceCount,
    string CostSource, string ReceivedKg, string OpenKg, string PendingRemainingKg,
    IReadOnlyList<PurchaseSupplierReturnInvoicedPartV1> Invoiced, bool SelectionRequired);

/// <summary>
/// AK-8b preview (read-only, price-free): how a return of <see cref="QuantityKg"/> from <see cref="SourceLabelId"/> would be
/// planned. <see cref="LineageMode"/> is Lineage (SAT-D433: one row per receipt of the lineage) or Quantity (SAT-D441: one row,
/// the chosen receipt). <see cref="Refusal"/> names the code the command would answer with when the plan cannot be posted as it
/// stands (a receipt limit, an undecided price rule …); the rows are still shown so the preparer sees why. The piece fields
/// are always present and null on a label that tracks no pieces.
/// </summary>
public sealed record PurchaseSupplierReturnLineagePreviewV1(
    Guid CompanyId, Guid PurchaseOrderId, Guid GoodsAcceptanceId, Guid SourceLabelId, string LineageMode, string QuantityKg,
    int? PieceCount, bool TracksPieces, string FreeKg, string BlockedKg, int? FreePieces, int? BlockedPieces,
    IReadOnlyList<PurchaseSupplierReturnLineagePreviewPartV1> Parts,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Refusal = null);

/// <summary>Shape rules of the posted parts and of the stored ones.</summary>
public static class PurchaseSupplierReturnLineagePartsV1
{
    public const int MaximumParts = 64;
    public const int MaximumInvoicedSources = 100;

    public static bool ValidRequestShape(CreatePurchaseSupplierReturnV1 request)
    {
        if (request.LineageParts is not { } parts) return true;
        return parts.Count is >= 1 and <= MaximumParts && parts.All(part => part is not null && part.GoodsAcceptanceId != Guid.Empty
                && part.ReturnLineId != Guid.Empty && part.InventoryMovementId != Guid.Empty
                && (part.PendingKg is null) == (part.Invoiced is null)
                && (part.PendingKg is null || PurchaseSupplierReturnJsonV1.IsQuantity(part.PendingKg))
                && (part.Invoiced is null || part.Invoiced.Count <= MaximumInvoicedSources && part.Invoiced.All(item => item is not null
                        && item.SourceEventId != Guid.Empty && PurchaseSupplierReturnJsonV1.IsPositiveQuantity(item.QuantityKg))
                    && part.Invoiced.Select(item => item.SourceEventId).Distinct().Count() == part.Invoiced.Count)
                && (part.ManualUnitCostTryPerKg is null || PurchaseSupplierReturnJsonV1.IsPositiveUnitCost(part.ManualUnitCostTryPerKg)))
            && parts.Select(part => part.GoodsAcceptanceId).Distinct().Count() == parts.Count
            && parts.Select(part => part.ReturnLineId).Distinct().Count() == parts.Count
            && parts.Select(part => part.InventoryMovementId).Distinct().Count() == parts.Count
            // The document's own receipt carries the document's line and movement ids.
            && parts.Count(part => part.GoodsAcceptanceId == request.GoodsAcceptanceId) == 1
            && parts.Single(part => part.GoodsAcceptanceId == request.GoodsAcceptanceId) is var anchor
            && anchor.ReturnLineId == request.ReturnLineId && anchor.InventoryMovementId == request.InventoryMovementId;
    }

    /// <summary>The stored parts of a return are coherent with its header (used on save and on every read back).</summary>
    public static bool ValidStored(PurchaseSupplierReturnDraft draft)
    {
        if (draft.LineageParts is not { } parts) return true;
        if (draft.SourceLabelId is null || draft.LineageMode is null || parts.Count is < 1 or > MaximumParts) return false;
        if (draft.LineageMode == PurchaseSupplierReturnLineageModeV1.Quantity && parts.Count != 1) return false;
        if (parts.Any(part => part is null || part.GoodsAcceptanceId == Guid.Empty || part.PurchaseOrderId == Guid.Empty
                || part.PurchaseOrderLineId == Guid.Empty || part.ReturnLineId == Guid.Empty || part.InventoryMovementId == Guid.Empty
                || part.QuantityKg <= 0m || decimal.Round(part.QuantityKg, 3) != part.QuantityKg || part.UnitCostTryPerKg <= 0m
                || !Enum.IsDefined(part.CostBasis) || part.PieceCount is < 0 || (part.PieceCount is null) != (draft.PieceCount is null)
                || (part.PendingKg is null) != (part.Invoiced is null)
                || part.PendingKg is not null && !SelectionAddsUp(part)
                || part.SupplierUnitPriceTry is <= 0m || part.SupplierPieceQuantity is < 1
                || part.SupplierPieceQuantity is not null && part.SupplierUnitPriceTry is null
                || (part.SupplierAmountTry is null) != (part.SupplierUnitPriceTry is null)
                || part.SupplierAmountTry is { } billed && (billed <= 0m || decimal.Round(billed, 2) != billed)
                || part.SupplierUnitPriceTry is not null && draft.LineageMode != PurchaseSupplierReturnLineageModeV1.Quantity)
            || parts.Select(part => part.GoodsAcceptanceId).Distinct().Count() != parts.Count
            || parts.Select(part => part.ReturnLineId).Distinct().Count() != parts.Count
            || parts.Select(part => part.InventoryMovementId).Distinct().Count() != parts.Count)
            return false;
        var anchor = parts.SingleOrDefault(part => part.GoodsAcceptanceId == draft.GoodsAcceptanceId);
        return anchor is not null && anchor.ReturnLineId == draft.ReturnLineId && anchor.InventoryMovementId == draft.InventoryMovementId
            && anchor.PurchaseOrderId == draft.PurchaseOrderId && anchor.PurchaseOrderLineId == draft.PurchaseOrderLineId
            && anchor.UnitCostTryPerKg == draft.UnitCostTryPerKg
            && parts.Sum(part => part.QuantityKg) == draft.QuantityKg
            && (draft.PieceCount is null || parts.Sum(part => part.PieceCount ?? 0) == draft.PieceCount)
            && parts.Any(part => part.CostBasis == PurchaseSupplierReturnCostBasis.ManualCostWithException)
                == (draft.CostBasis == PurchaseSupplierReturnCostBasis.ManualCostWithException);
    }

    /// <summary>The explicit selection of a part adds up to exactly its kilograms.</summary>
    public static bool SelectionAddsUp(PurchaseSupplierReturnLineagePartV1 part) =>
        TryKg(part.PendingKg, out var pending) && part.Invoiced is { } invoiced
        && invoiced.All(item => TryKg(item.QuantityKg, out _))
        && pending + invoiced.Sum(item => ParseKg(item.QuantityKg)) == part.QuantityKg;

    public static bool TryKg(string? text, out decimal value)
    {
        value = 0m;
        return PurchaseSupplierReturnJsonV1.IsQuantity(text)
            && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    public static decimal ParseKg(string text) => decimal.Parse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    public static string Kg(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
