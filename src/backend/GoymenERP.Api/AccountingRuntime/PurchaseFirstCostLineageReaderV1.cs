using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;

namespace GoymenERP.Api.AccountingRuntime;

/// <summary>Uses the actual committed envelope and its existing owner adapter, then copies immutable physical facts.</summary>
public sealed class PurchaseFirstCostLineageReaderV1(ICompanyContext company) : IPurchaseFirstCostLineageReaderV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PurchaseFirstCostLineageV1> ReadAsync(WorkInboxEventV1 envelope, AccountingInventoryCostSourceV1 source,
        AccountingInventoryCostComponentV1 component, Func<CancellationToken, Task> freshConsumerFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence);
        async Task Fence()
        {
            await freshConsumerFence(cancellationToken);
            if (!company.IsResolved || company.CompanyId != source.CompanyId || source.CompanyId == Guid.Empty)
                throw Refuse();
        }
        await Fence();
        // Reuse the exact source validator, including receipt/line conservation and frozen physical source hashes.
        // No synthetic envelope, latest Inventory graph, current average or reconstructed production identity is used.
        var actual = AccountingPurchaseInvoiceCostSourceV1.Read(envelope, source.CompanyId);
        if (JsonSerializer.Serialize(actual, Json) != JsonSerializer.Serialize(source, Json)
            || !AccountingInventoryCostTraceV1.IsPurchaseFirstLineComponent(component.ComponentKey)
            || actual.Components.Count(x => x.ComponentKey == component.ComponentKey) != 1
            || JsonSerializer.Serialize(actual.Components.Single(x => x.ComponentKey == component.ComponentKey), Json)
                != JsonSerializer.Serialize(component, Json) || component.RecordedRounding is null) throw Refuse();
        InventoryPurchaseInvoiceCostApplicationResultV1 value;
        try { value = JsonSerializer.Deserialize<InventoryPurchaseInvoiceCostApplicationResultV1>(envelope.PayloadJson, Json) ?? throw Refuse(); }
        catch (JsonException) { throw Refuse(); }
        var line = value.InitialRecognitions!.Single(x => AccountingInventoryCostTraceV1.PurchaseFirstLineComponent(x.InvoiceLineId) == component.ComponentKey);
        var receipts = new List<PurchaseFirstCostReceiptV1>();
        foreach (var receipt in line.Receipts)
        {
            InventoryPurchaseFirstCostPhysicalSourceV1 physical;
            try { physical = JsonSerializer.Deserialize<InventoryPurchaseFirstCostPhysicalSourceV1>(receipt.PhysicalSourceJson, Json) ?? throw Refuse(); }
            catch (JsonException) { throw Refuse(); }
            var shipments = physical.Shipments.Select(x => new PurchaseFirstCostShipmentV1(x.ShipmentId, x.MovementId,
                x.MovementLineNumber, x.LoadingInstructionLineId, x.ProductId, x.LabelId, x.PhysicalQuantityKg,
                Ratio(x.RootShare), Ratio(x.NetRootQuantityKg), x.RecordedAtUtc, x.ProvenOriginalSequence,
                x.Returns.Select(r => new PurchaseFirstCostReturnV1(r.ReturnId, r.MovementId, r.MovementLineNumber,
                    r.SourceIssueMovementId, r.QuantityKg, r.RecordedAtUtc)).ToArray(), JsonSerializer.Serialize(x, Json))).ToArray();
            receipts.Add(new(receipt.AcceptanceId, receipt.ReceiptMovementId, receipt.RootLabelId, receipt.ReceiptLocationId,
                Ratio(InventoryExactCostRatioV1.From(ExactCostRatio.Of(receipt.MatchedQuantity).Divide(receipt.AcceptedQuantity))),
                Ratio(receipt.ConsumedQuantityKg), Ratio(receipt.ConsumedExactTlAmount), receipt.PhysicalSourceSha256, shipments));
        }
        await Fence();
        return new(source.CompanyId, source.EventId, source.InvoiceId, source.InvoiceVersion, source.AdmissionId,
            line.InvoiceLineId, line.InvoiceLineNo, line.ProductId, component.ComponentKey, component.SourceProofSha256,
            component.RecordedRounding, receipts);
    }

    private static AccountingInventoryCostRatioV1 Ratio(InventoryExactCostRatioV1 ratio) => new(ratio.Numerator, ratio.Denominator);
    private static AccountingStoreException Refuse() => new("purchase_cost_attribution_source_unavailable",
        "İlk alış maliyetinin değişmez kaynak ve fiziksel pay kanıtı doğrulanamadı.");
}
