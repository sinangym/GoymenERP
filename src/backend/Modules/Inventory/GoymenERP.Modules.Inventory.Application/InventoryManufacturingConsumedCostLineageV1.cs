using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>A real customer-return movement explicitly bound to the original shipment issue.</summary>
public sealed record InventoryManufacturingConsumedCostReturnV1(Guid ReturnId, Guid MovementId,
    int MovementLineNumber, Guid SourceIssueMovementId, decimal QuantityKg, DateTimeOffset RecordedAtUtc);

/// <summary>
/// Physical attribution only. ShipmentId and LoadingInstructionLineId are the original movement's source identities;
/// the consumer must additionally prove the loading instruction/dispatch/invoice linkage through their owners.
/// RootShare and NetRootQuantityKg are exact material quantities, never a price or an allocation of consumed TL.
/// RecordedAtUtc is the movement's immutable recording time, not its backdatable business occurrence time.
/// CreationSequence is null: the existing rowversion cursor was added to legacy outbox rows and cannot prove their
/// original creation order. Equal timestamps without a separately proved sequence remain an ambiguous chronology.
/// </summary>
public sealed record InventoryManufacturingConsumedCostShipmentV1(Guid ShipmentId, Guid MovementId,
    int MovementLineNumber, Guid LoadingInstructionLineId, Guid LabelId, Guid ProductId, string ProductVersion,
    StockPartyKind OwnerKind, Guid OwnerId, StockPartyKind CustodianKind, Guid CustodianId, Guid LocationId,
    BlockState Block, decimal QuantityKg, InventoryExactCostRatioV1 RootShare,
    InventoryExactCostRatioV1 NetRootQuantityKg, IReadOnlyList<InventoryManufacturingConsumedCostReturnV1> Returns,
    DateTimeOffset RecordedAtUtc, string? CreationSequence = null);

/// <summary>
/// Captured in the generation's transaction under the same label-version fence as its physical reconciliation.
/// Empty Shipments is an actual empty snapshot; null on an older output means no historical attribution was recorded.
/// The original movement stamp, aggregate consumed TL and every Accounting journal remain unchanged.
/// </summary>
public sealed record InventoryManufacturingConsumedCostLineageV1(string SourceVersion, string EvidenceSha256,
    InventoryExactCostRatioV1 NetShippedRootQuantityKg, IReadOnlyList<InventoryManufacturingConsumedCostShipmentV1> Shipments);

public sealed record InventoryManufacturingConsumedCostOutputV1(Guid OutputId, Guid ReceiptMovementId,
    Guid RootLabelId, decimal NetQuantityKg, InventoryExactCostRatioV1 ConsumedRootQuantityKg,
    InventoryManufacturingConsumedCostLineageV1 Lineage);

/// <summary>The outbox carries this bounded reference, not the possibly large shipment graph stored in the same-TX history.</summary>
public sealed record InventoryManufacturingConsumedCostLineageReferenceV1(string SourceVersion,
    Guid GenerationId, string GenerationVersion, string HistorySha256);

public sealed record InventoryManufacturingConsumedCostLineageSnapshotV1(Guid CompanyId, Guid ManufacturingOrderId,
    Guid GenerationId, string GenerationVersion, string ClosedOrderVersion, InventoryManufacturingCostGenerationStateV1 State,
    string InputFingerprint, Guid? CostDifferenceEventId, IReadOnlyList<InventoryManufacturingConsumedCostOutputV1> Outputs);

public enum InventoryManufacturingConsumedCostLineageOutcomeV1
{ Available = 1, NotFound = 2, Unreadable = 3, UnreadableLegacy = 4 }

public sealed record InventoryManufacturingConsumedCostLineageReadV1(InventoryManufacturingConsumedCostLineageOutcomeV1 Outcome,
    Guid CompanyId, Guid GenerationId, string GenerationVersion, InventoryManufacturingConsumedCostLineageSnapshotV1? Snapshot,
    string SourceVersion, string? Reason = null);

/// <summary>
/// Internal, company-fenced owner read of one immutable generation revision; no current-graph reconstruction.
/// Carries no permission or posting authority. The consumer fences its own actor/company/operation before and after
/// awaiting this port and independently admits the real Sales, Logistics and Accounting evidence. Available is only
/// physical attribution; it does not turn an unavailable original TL stamp into a price or authorize a second SMM.
/// </summary>
public interface IInventoryManufacturingConsumedCostLineageReadPortV1
{
    Task<InventoryManufacturingConsumedCostLineageReadV1> ReadAsync(Guid companyId, Guid generationId,
        string generationVersion, CancellationToken cancellationToken = default);
}

/// <summary>Canonical, money-free evidence codec shared by the generation writer and the immutable owner read.</summary>
public static class InventoryManufacturingConsumedCostLineageEvidenceV1
{
    public const string SourceVersion = "inventory.manufacturing-consumed-cost-lineage.v1";
    public const int MaximumMovementLines = 65536;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static InventoryManufacturingConsumedCostLineageV1 Create(Guid companyId, Guid outputId,
        Guid receiptMovementId, Guid rootLabelId, decimal netQuantityKg, InventoryExactCostRatioV1 netShippedRootQuantityKg,
        IReadOnlyList<InventoryManufacturingConsumedCostShipmentV1> shipments)
    {
        var value = new InventoryManufacturingConsumedCostLineageV1(SourceVersion, "", netShippedRootQuantityKg, shipments);
        value = value with { EvidenceSha256 = Hash(companyId, outputId, receiptMovementId, rootLabelId, netQuantityKg, value) };
        if (!IsValid(companyId, outputId, receiptMovementId, rootLabelId, netQuantityKg, value))
            throw new InvalidOperationException("The captured physical consumption lineage is inconsistent.");
        return value;
    }

    public static bool IsValid(Guid companyId, Guid outputId, Guid receiptMovementId, Guid rootLabelId,
        decimal netQuantityKg, InventoryManufacturingConsumedCostLineageV1? value)
    {
        try
        {
            if (companyId == Guid.Empty || outputId == Guid.Empty || receiptMovementId == Guid.Empty || rootLabelId == Guid.Empty
                || netQuantityKg < 0m || value is null || value.SourceVersion != SourceVersion
                || value.Shipments is null || value.Shipments.Count > MaximumMovementLines
                || value.NetShippedRootQuantityKg is null || value.NetShippedRootQuantityKg.Value().Sign < 0
                || value.EvidenceSha256 != Hash(companyId, outputId, receiptMovementId, rootLabelId, netQuantityKg, value)) return false;
            var movements = new HashSet<(Guid, int)>();
            var shipmentIds = new HashSet<Guid>();
            var returnIds = new HashSet<Guid>();
            var total = ExactCostRatio.Of(0m);
            var returnCount = 0;
            Guid? productId = null;
            foreach (var shipment in value.Shipments)
            {
                if (shipment is null || shipment.ShipmentId == Guid.Empty || shipment.MovementId == Guid.Empty
                    || shipment.MovementLineNumber != 1 || !movements.Add((shipment.MovementId, shipment.MovementLineNumber))
                    || !shipmentIds.Add(shipment.ShipmentId) || shipment.LoadingInstructionLineId == Guid.Empty
                    || shipment.LabelId == Guid.Empty || shipment.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(shipment.ProductVersion)
                    || productId is { } previousProduct && previousProduct != shipment.ProductId
                    || shipment.OwnerKind is not (StockPartyKind.Company or StockPartyKind.Counterparty) || shipment.OwnerId == Guid.Empty
                    || shipment.CustodianKind is not (StockPartyKind.Company or StockPartyKind.Counterparty) || shipment.CustodianId == Guid.Empty
                    || shipment.LocationId == Guid.Empty || shipment.Block is not (BlockState.Free or BlockState.Blocked)
                    || shipment.QuantityKg <= 0m || shipment.RootShare is null || shipment.NetRootQuantityKg is null
                    || shipment.RecordedAtUtc == default || shipment.RecordedAtUtc.Offset != TimeSpan.Zero || shipment.CreationSequence is not null
                    || shipment.Returns is null || shipment.Returns.Count > MaximumMovementLines - returnCount) return false;
                productId = shipment.ProductId;
                returnCount += shipment.Returns.Count;
                var share = shipment.RootShare.Value();
                if (share.Sign <= 0 || share.CompareTo(ExactCostRatio.Of(1m)) > 0) return false;
                var returned = ExactCostRatio.Of(0m);
                foreach (var item in shipment.Returns)
                {
                    if (item is null || item.ReturnId == Guid.Empty || !returnIds.Add(item.ReturnId) || item.MovementId == Guid.Empty
                        || item.MovementLineNumber != 1 || !movements.Add((item.MovementId, item.MovementLineNumber))
                        || item.SourceIssueMovementId != shipment.MovementId || item.QuantityKg <= 0m
                        || item.RecordedAtUtc == default || item.RecordedAtUtc.Offset != TimeSpan.Zero) return false;
                    returned = returned.Add(ExactCostRatio.Of(item.QuantityKg));
                }
                var net = ExactCostRatio.Of(shipment.QuantityKg).Subtract(returned);
                if (net.Sign < 0 || net.Multiply(share).CompareTo(shipment.NetRootQuantityKg.Value()) != 0) return false;
                total = total.Add(shipment.NetRootQuantityKg.Value());
            }
            return total.CompareTo(value.NetShippedRootQuantityKg.Value()) == 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or OverflowException)
        { return false; }
    }

    public static bool TryVersion(string? text, out long version) => long.TryParse(text, NumberStyles.None,
        CultureInfo.InvariantCulture, out version) && version > 0 && text == version.ToString(CultureInfo.InvariantCulture);

    public static string Sha256(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>Existing event content stays intact; only the new physical graph is replaced by a hash of the saved full JSON.
    /// Does not impose a new command-size/business limit or change a legacy result with no captured lineage.</summary>
    public static InventoryManufacturingCostGenerationResultV1 ForOutbox(InventoryManufacturingCostGenerationResultV1 result,
        string savedHistoryJson) => result.Outputs.Any(output => output.ConsumedCostLineage is not null)
        ? result with
        {
            Outputs = Array.AsReadOnly(result.Outputs.Select(output => output with { ConsumedCostLineage = null }).ToArray()),
            ConsumedLineageReference = new(SourceVersion, result.GenerationId, result.GenerationVersion, Sha256(savedHistoryJson)),
        } : result;

    private static string Hash(Guid companyId, Guid outputId, Guid receiptMovementId, Guid rootLabelId, decimal netQuantityKg,
        InventoryManufacturingConsumedCostLineageV1 value) => Sha256(JsonSerializer.Serialize(new
        { companyId, outputId, receiptMovementId, rootLabelId, netQuantityKg, value.SourceVersion, value.NetShippedRootQuantityKg, value.Shipments }, Json));
}
