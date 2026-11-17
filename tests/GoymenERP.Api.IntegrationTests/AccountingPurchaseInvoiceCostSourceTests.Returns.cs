using System.Text.Json;
using GoymenERP.Api.AccountingRuntime;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingPurchaseInvoiceCostSourceTests
{
    [Fact]
    public void A_zero_first_cost_keeps_only_actual_fully_returned_shipment_evidence()
    {
        var source = FirstReturnedSource(Guid.NewGuid());
        var result = AccountingPurchaseInvoiceCostSourceV1.Read(Envelope(source), source.CompanyId);
        var component = Assert.Single(result.Components);
        Assert.Equal(AccountingInventoryCostTraceV1.PurchaseFirstLineComponent(source.InitialRecognitions![0].InvoiceLineId), component.ComponentKey);
        Assert.Equal(new("0", "1"), component.SignedTlAmount);
        Assert.Equal(new("0", "1"), component.RecordedRounding!.Rounding.ExactSignedAmount);
        Assert.Equal(new("0", "1"), component.RecordedRounding.Rounding.ExactSignedResidue);
        Assert.Contains("returns", component.SourceProofJson);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var allStock in new[] { false, true })
        {
            var line = source.InitialRecognitions[0];
            var receipts = line.Receipts.Select(receipt => {
                var physical = JsonSerializer.Deserialize<InventoryPurchaseFirstCostPhysicalSourceV1>(receipt.PhysicalSourceJson, json)!;
                physical = physical with { Shipments = allStock ? [] : physical.Shipments.Select(x => x with { Returns = [] }).ToArray() };
                var proof = JsonSerializer.Serialize(physical, json);
                return receipt with { PhysicalSourceJson = proof, PhysicalSourceSha256 = AccountingInventoryCostSourceValidationV1.Sha256(proof) };
            }).ToArray();
            var invalid = source with { InitialRecognitions = [line with { Receipts = receipts }] };
            Assert.Throws<OutboxPermanentFailureException>(() => AccountingPurchaseInvoiceCostSourceV1.Read(Envelope(invalid), invalid.CompanyId));
        }
    }

    internal static InventoryPurchaseInvoiceCostApplicationResultV1 FirstReturnedSource(Guid company)
    {
        var Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var source = FirstSource(100m, company); var line = source.InitialRecognitions![0];
        var receipts = line.Receipts.Select(receipt => {
            var physical = JsonSerializer.Deserialize<InventoryPurchaseFirstCostPhysicalSourceV1>(receipt.PhysicalSourceJson, Json)!;
            var shipment = physical.Shipments[0];
            physical = physical with { RemainingRootQuantityKg = new("1", "1"), NetShippedRootQuantityKg = new("0", "1"),
                Labels = [physical.Labels[0] with { Positions = [new(StockPartyKind.Company, company, StockPartyKind.Company,
                    company, receipt.ReceiptLocationId, BlockState.Free, 1m)] }],
                Shipments = [shipment with { NetRootQuantityKg = new("0", "1"), Returns = [new(Guid.NewGuid(), Guid.NewGuid(), 1,
                    shipment.MovementId, 1m, shipment.RecordedAtUtc.AddMinutes(1))] }] };
            var json = JsonSerializer.Serialize(physical, Json);
            return receipt with { ConsumedQuantityKg = new("0", "1"), ConsumedExactTlAmount = new("0", "1"),
                PhysicalSourceJson = json, PhysicalSourceSha256 = AccountingInventoryCostSourceValidationV1.Sha256(json) };
        }).ToArray();
        line = line with { ConsumedQuantityKg = new("0", "1"), ConsumedTlAmount = 0m, ConsumedExactTlAmount = new("0", "1"),
            ConsumedRoundingResidue = new("0", "1"), Receipts = receipts,
            Positions = receipts.Select((receipt, index) => new InventoryPurchaseFirstCostPositionV1(receipt.ReceiptLocationId,
                $"SENTETIK-{index}", new("1", "1"), new("50", "1"), new("50", "1"), new("0", "1"), 1, true)).ToArray() };
        return source with { InitialRecognitions = [line], UnabsorbedTlDifference = new("0", "1") };
    }
}
