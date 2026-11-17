using System.Text.Json;
using GoymenERP.Api.AccountingRuntime;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Fully_returned_purchase_shipments_keep_a_different_actor_zero_approval_witness_without_a_journal_or_a_fake_stock_claim()
    {
        await using var run = await Run.Start(); var period = await run.Period();
        var owner = AllReturnedPurchaseSource(); var reader = new ActualPurchaseCostReader(owner);
        await using var scope = run.Open(); var store = Costs(scope, reader, lineage: new PurchaseFirstCostLineageReaderV1(scope.CompanyContext));
        await store.AcceptAsync(reader.Envelope); var proposal = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.PurchaseInvoiceDifference));
        Assert.Equal("AwaitingPreparation", proposal.Status); Assert.Equal("0", proposal.ExactTlAmount);
        Assert.Equal(2, await run.Count("accounting.PurchaseCostAttributionCandidates"));
        await store.PrepareAsync(proposal.ProposalId, proposal.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        await Assert.ThrowsAsync<AccountingStoreException>(() => store.ApproveAsync(proposal.ProposalId, proposal.Kind, 2, Maker, Fence, Metadata(Maker)));
        var approved = await store.ApproveAsync(proposal.ProposalId, proposal.Kind, 2, Checker, Fence, Metadata(Checker));
        Assert.Equal("RoundedNoCost", approved.Status); Assert.Null(approved.JournalId);
        var shares = await ReadPurchaseShares(scope, reader);
        Assert.Equal(2, shares.Count);
        Assert.All(shares, read => {
            Assert.False(read.HasPendingCandidate); var share = Assert.Single(read.Shares);
            Assert.Equal("0", share.SignedAmount); Assert.Null(share.Approval.JournalId);
            Assert.Equal(new("0", "1"), share.ExactSignedAmount);
            Assert.All(share.Candidates, candidate => {
                Assert.Equal(new("0", "1"), candidate.InvoicedNetQuantityKg);
                Assert.Equal(new("1", "1"), candidate.InvoicedRootShare);
                Assert.Single(candidate.Shipment.Returns);
            });
            Assert.True(PostedCostPhysicalCoverageV1.IsComplete([], read.Shares));
        });
        await store.AcceptAsync(reader.Envelope);
        Assert.Equal(0, await run.Count("accounting.Journals")); Assert.Equal(1, await run.Count("accounting.PurchaseCostAttributionApprovals"));

        // Removing actual shipment evidence makes this all-stock source ineligible for a physical SMM claim.
        var allStock = owner with { InvoiceId = Guid.NewGuid(), SourceEventId = Guid.NewGuid(), CostDifferenceEventId = Guid.NewGuid() };
        var line = allStock.InitialRecognitions![0];
        allStock = allStock with { InitialRecognitions = [line with { Receipts = line.Receipts.Select(receipt => {
            var physical = JsonSerializer.Deserialize<InventoryPurchaseFirstCostPhysicalSourceV1>(receipt.PhysicalSourceJson, Json)! with { Shipments = [] };
            var json = JsonSerializer.Serialize(physical, Json);
            return receipt with { PhysicalSourceJson = json, PhysicalSourceSha256 = AccountingInventoryCostSourceValidationV1.Sha256(json) };
        }).ToArray() }] };
        var emptyReader = new ActualPurchaseCostReader(allStock); var emptyStore = Costs(scope, emptyReader,
            lineage: new PurchaseFirstCostLineageReaderV1(scope.CompanyContext));
        await emptyStore.AcceptAsync(emptyReader.Envelope);
        var noCost = Assert.Single((await emptyStore.ListAsync(AccountingInventoryCostKindV1.PurchaseInvoiceDifference))
            .Where(x => x.EventId == emptyReader.Envelope.EventId));
        Assert.Equal("NoCost", noCost.Status); Assert.Null(noCost.JournalId);
        await Assert.ThrowsAsync<AccountingStoreException>(() => emptyStore.PrepareAsync(noCost.ProposalId, noCost.Kind,
            CostPreparation(period), Maker, Fence, Metadata(Maker)));
        Assert.Equal(2, await run.Count("accounting.PurchaseCostAttributionSources"));
        Assert.Equal(2, await run.Count("accounting.PurchaseCostAttributionCandidates"));
        Assert.Equal(1, await run.Count("accounting.PurchaseCostAttributionApprovals"));
    }

    private static InventoryPurchaseInvoiceCostApplicationResultV1 AllReturnedPurchaseSource()
    {
        var source = AccountingPurchaseInvoiceCostSourceTests.FirstSource(100m, Company); var line = source.InitialRecognitions![0];
        var receipts = line.Receipts.Select(receipt => {
            var physical = JsonSerializer.Deserialize<InventoryPurchaseFirstCostPhysicalSourceV1>(receipt.PhysicalSourceJson, Json)!;
            var shipment = physical.Shipments[0];
            physical = physical with { RemainingRootQuantityKg = new("1", "1"), NetShippedRootQuantityKg = new("0", "1"),
                Labels = [physical.Labels[0] with { Positions = [new(StockPartyKind.Company, Company, StockPartyKind.Company,
                    Company, receipt.ReceiptLocationId, BlockState.Free, 1m)] }],
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
