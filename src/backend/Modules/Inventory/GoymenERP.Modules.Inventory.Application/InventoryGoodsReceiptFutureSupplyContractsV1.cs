using System.Data.Common;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// F-017 STOK §3.5 (F017-c): at goods receipt, in the SAME host transaction and under the SAME purchase line lock as the receipt
/// (ALS-D84 order: purchase acceptance, then the Inventory label, then this conversion), the accepted quantity consumes the purchase
/// general line's active future supply allocations in order and each consumed part becomes a physical <c>sales.order-line</c>
/// allocation on the new label. The future allocation and the physical allocation are linked by id
/// (<c>inventory.FutureSupplyConversions</c>). No HTTP route, OpenAPI entry or permission is declared here: only the goods receipt
/// host calls the port, after its own permission check, inside its own transaction. BEFORE any acceptance stage takes a purchase
/// lock, the host resolves Purchasing's general line, reads its active future allocations, locks their complete Sales-line union
/// ascending, then its purchase-line union ascending, and re-reads a subset of the prelocked allocation set. Growth discards the
/// prewrite transaction and prepares afresh; a business callback is never retried. This shares serialization with physical and
/// future allocation creators on OTHER purchase lines and prevents an interleaved physical+future demand-cap read.
/// </summary>
public static class InventoryGoodsReceiptFutureSupplyV1
{
    /// <summary>The Inventory idempotency row of one conversion; keyed by the receipt's own idempotency key.</summary>
    public const string CommandName = "inventory.future-allocation.convert-on-receipt.v1";

    /// <summary>The audit operation of a consumed future allocation.</summary>
    public const string ConvertOperation = "Convert";

    /// <summary>
    /// 422: the host-supplied consumption order (F17-3 <c>AllocationOrder</c>) names an id twice or an id that is not an active future
    /// allocation of the received line. Nothing is written (the receipt rolls back with it).
    /// </summary>
    public const string AllocationOrderInvalid = "inventory_future_allocation_order_invalid";

    /// <summary>The release code of a business link failure (§3.5); the reason text names which link failed.</summary>
    public const string LinkFailedReleaseReason = InventoryFutureAllocationReleaseReasonsV1.AcceptanceLinkFailed;

    /// <summary>The reason stored on the physical allocation the conversion creates.</summary>
    public const string PhysicalAllocationReason = "Mal kabulde gelecek tedarik ayırmasından fiziki tahsise çevrildi.";

    /// <summary>The audit reason of a consumed future allocation.</summary>
    public const string ConvertedAuditReason = "Mal kabulde kabul edilen miktar gelecek tedarik ayırmasını tüketti.";
}

/// <summary>What happened to one future allocation at goods receipt (stored as the number on the link row).</summary>
public enum InventoryFutureSupplyConversionOutcomeV1
{
    /// <summary>A part (or all) of the remainder became a physical allocation on the received label.</summary>
    Converted = 1,

    /// <summary>A business link failed; the whole remainder was released with acceptance-link-failed (§3.5).</summary>
    LinkFailed = 2,
}

/// <summary>
/// F-017 STOK §3.5: which business link failed. The goods receipt and the label stay; only the future allocation is released, in the
/// same transaction, with <see cref="InventoryGoodsReceiptFutureSupplyV1.LinkFailedReleaseReason"/> and the released event.
/// </summary>
public static class InventoryFutureSupplyLinkFailuresV1
{
    /// <summary>The received label has no Free position (received blocked / in quality hold).</summary>
    public const string LabelBlocked = "label-blocked";

    /// <summary>No central conversion source turns the purchase unit into kilograms for this product (no guessed factor).</summary>
    public const string UnitConversionUnavailable = "unit-conversion-unavailable";

    /// <summary>The Sales order line is no longer approved, or no longer exists.</summary>
    public const string DemandNotApproved = "demand-not-approved";

    /// <summary>The Sales order line now names another product than the received label.</summary>
    public const string ProductMismatch = "product-mismatch";

    /// <summary>The label's Free kilograms that no allocation holds are fewer than the converted share.</summary>
    public const string InsufficientLabelQuantity = "insufficient-label-quantity";

    /// <summary>The received position is not owned and held by the company, so a Sales line cannot reserve it.</summary>
    public const string OwnerNotCompany = "owner-not-company";

    public static string ReleaseText(string failure) => failure switch
    {
        LabelBlocked => "Mal kabul bağı kurulamadı: etiket serbest stokta değil (blokeli kabul).",
        UnitConversionUnavailable => "Mal kabul bağı kurulamadı: alış birimi kilograma merkezi kaynakla çevrilemedi.",
        DemandNotApproved => "Mal kabul bağı kurulamadı: satış sipariş satırı artık onaylı değil.",
        ProductMismatch => "Mal kabul bağı kurulamadı: satış sipariş satırının ürünü etiketle uyuşmuyor.",
        InsufficientLabelQuantity => "Mal kabul bağı kurulamadı: etiketin serbest miktarı yetmiyor.",
        OwnerNotCompany => "Mal kabul bağı kurulamadı: etiket şirketin mülkiyetinde ve elinde değil.",
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, "Unknown F017-c link failure."),
    };
}

/// <summary>
/// The goods receipt facts the conversion needs. <see cref="PurchaseOrderGeneralLineId"/> is the GENERAL line the future allocations
/// and the ledger name (a delivery line's receipt consumes its general line); <see cref="AcceptedQuantityText"/> is in
/// <see cref="PurchaseUnitId"/>; <see cref="ReceivedQuantityKgText"/> is the label's separately measured kilograms of that same
/// accepted quantity. A share that takes the WHOLE accepted quantity therefore becomes exactly those kilograms (a measured fact, no
/// factor); a partial share needs the central kilogram conversion (<see cref="IInventoryFutureUnitConverterV1.ConvertToKilogramsAsync"/>)
/// and is a link failure without it. <see cref="AllocationOrder"/> is the host's optional consumption order (F17-3); null or empty
/// means the stored order (manual sequence, Sales due date, creation time).
/// </summary>
public sealed record InventoryGoodsReceiptFutureSupplyRequestV1(
    Guid CompanyId,
    Guid AcceptanceId,
    Guid PurchaseOrderId,
    Guid PurchaseOrderGeneralLineId,
    Guid PurchaseUnitId,
    Guid ProductId,
    string ProductVersion,
    Guid LabelId,
    string AcceptedQuantityText,
    string ReceivedQuantityKgText,
    bool ReceiptIsReplay,
    IReadOnlyList<Guid>? AllocationOrder = null);

/// <summary>One link: the consumed (or released) quantity in the purchase unit and, when converted, the physical allocation.</summary>
public sealed record InventoryFutureSupplyConversionV1(
    Guid FutureAllocationId,
    string Outcome,
    string Quantity,
    Guid? PhysicalAllocationId,
    string? PhysicalQuantityKg,
    string? FailureReason);

/// <summary>What the receipt did to the line's future allocations; a replay answers the stored result and writes nothing.</summary>
public sealed record InventoryGoodsReceiptFutureSupplyResultV1(
    Guid AcceptanceId,
    IReadOnlyList<InventoryFutureSupplyConversionV1> Conversions,
    bool IsReplay);

/// <summary>
/// The conversion seam the goods receipt host enlists on its own open connection and transaction. The caller owns commit and
/// rollback; a technical failure throws (the receipt rolls back with it), a business link failure releases and returns.
/// </summary>
public interface IInventoryGoodsReceiptFutureSupplyPortV1
{
    Task<IInventoryGoodsReceiptFutureSupplySessionV1> EnlistAsync(DbConnection connection, DbTransaction transaction,
        CancellationToken cancellationToken = default);
}

public interface IInventoryGoodsReceiptFutureSupplySessionV1 : IAsyncDisposable
{
    Task<InventoryGoodsReceiptFutureSupplyResultV1> ConvertAsync(InventoryGoodsReceiptFutureSupplyRequestV1 request,
        InventoryCommandMetadata metadata, CancellationToken cancellationToken = default);
}

/// <summary>The converted event (§6 outbox): one per consumed future allocation, after the change, in the same save.</summary>
public static class InventoryFutureAllocationConvertedOutboxV1
{
    public const string EventType = "inventory.future-allocation.converted.v1";
    public const int SchemaVersion = 1;
    public const string SourceOperation = InventoryGoodsReceiptFutureSupplyV1.CommandName;
    public const string AggregateType = InventoryFutureAllocationCreatedOutboxV1.AggregateType;
}

public sealed record InventoryFutureAllocationConvertedV1(
    Guid CompanyId,
    Guid AllocationId,
    string AllocationVersion,
    Guid DemandId,
    Guid DemandLineId,
    Guid PurchaseOrderId,
    Guid PurchaseOrderLineId,
    Guid PurchaseUnitId,
    Guid AcceptanceId,
    string ConsumedQuantity,
    string RemainingQuantity,
    string Status,
    Guid PhysicalAllocationId,
    Guid LabelId,
    string PhysicalQuantityKg,
    string CorrelationId,
    string? CausationId);

/// <summary>One active future allocation as the consumption order sees it.</summary>
public sealed record InventoryFutureSupplyConsumptionCandidateV1(
    Guid AllocationId,
    int? ManualSequence,
    DateOnly? DueDate,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// F-017 STOK §3.5 consumption order: the host's <c>AllocationOrder</c> first (in its order; a partial list is allowed and the rest
/// follow), then the manual sequence (set before unset), then the Sales line due date (dated before undated), then the creation
/// time, then the id. The last two tie rules are technical and reversible (spec: "teknik, geri alınabilir").
/// </summary>
public static class InventoryFutureSupplyConsumptionOrderV1
{
    /// <summary>
    /// False when <paramref name="allocationOrder"/> repeats an id or names one that is not among
    /// <paramref name="candidates"/> (the caller refuses 422 and writes nothing).
    /// </summary>
    public static bool TryOrder(IReadOnlyList<InventoryFutureSupplyConsumptionCandidateV1> candidates,
        IReadOnlyList<Guid>? allocationOrder, out IReadOnlyList<InventoryFutureSupplyConsumptionCandidateV1> ordered)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ordered = [];
        var position = new Dictionary<Guid, int>();
        if (allocationOrder is not null)
        {
            var known = candidates.Select(candidate => candidate.AllocationId).ToHashSet();
            for (var index = 0; index < allocationOrder.Count; index++)
            {
                var id = allocationOrder[index];
                if (!known.Contains(id) || !position.TryAdd(id, index))
                {
                    return false;
                }
            }
        }

        ordered =
        [
            .. candidates
                .OrderBy(candidate => position.ContainsKey(candidate.AllocationId) ? 0 : 1)
                .ThenBy(candidate => position.TryGetValue(candidate.AllocationId, out var index) ? index : 0)
                .ThenBy(candidate => candidate.ManualSequence is null ? 1 : 0)
                .ThenBy(candidate => candidate.ManualSequence ?? 0)
                .ThenBy(candidate => candidate.DueDate is null ? 1 : 0)
                .ThenBy(candidate => candidate.DueDate ?? DateOnly.MinValue)
                .ThenBy(candidate => candidate.CreatedAtUtc)
                .ThenBy(candidate => candidate.AllocationId),
        ];
        return true;
    }
}
