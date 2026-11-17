using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
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
        var owner = AccountingPurchaseInvoiceCostSourceTests.FirstReturnedSource(Company); var reader = new ActualPurchaseCostReader(owner);
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
        await Assert.ThrowsAsync<GoymenERP.BuildingBlocks.Messaging.OutboxPermanentFailureException>(
            () => emptyStore.AcceptAsync(emptyReader.Envelope));
        Assert.Equal(1, await run.Count("accounting.PurchaseCostAttributionSources"));
        Assert.Equal(2, await run.Count("accounting.PurchaseCostAttributionCandidates"));
        Assert.Equal(1, await run.Count("accounting.PurchaseCostAttributionApprovals"));
    }

    [SqlServerFact]
    public async Task Exact_zero_purchase_cost_cannot_be_accepted_when_the_actual_attribution_reader_has_no_returned_shipment_candidate()
    {
        await using var run = await Run.Start();
        var owner = AccountingPurchaseInvoiceCostSourceTests.FirstReturnedSource(Company); var reader = new ActualPurchaseCostReader(owner);
        await using var scope = run.Open();
        var store = Costs(scope, reader, lineage: new MissingReturnedCandidates(new PurchaseFirstCostLineageReaderV1(scope.CompanyContext)));
        var error = await Assert.ThrowsAsync<AccountingStoreException>(() => store.AcceptAsync(reader.Envelope));
        Assert.Equal("purchase_cost_attribution_source_conflict", error.Code);
        Assert.Equal(0, await run.Count("accounting.InventoryCostSources"));
        Assert.Equal(0, await run.Count("accounting.InventoryCostProposals"));
        Assert.Equal(0, await run.Count("accounting.PurchaseCostAttributionSources"));
        Assert.Equal(0, await run.Count("accounting.PurchaseCostAttributionCandidates"));
        Assert.Equal(0, await run.Count("accounting.Journals"));
        await Costs(scope, reader, lineage: new PurchaseFirstCostLineageReaderV1(scope.CompanyContext)).AcceptAsync(reader.Envelope);
        Assert.Equal(1, await run.Count("accounting.InventoryCostSources"));
        Assert.Equal(2, await run.Count("accounting.PurchaseCostAttributionCandidates"));
        Assert.Equal(0, await run.Count("accounting.Journals"));
    }

    private sealed class MissingReturnedCandidates(IPurchaseFirstCostLineageReaderV1 inner) : IPurchaseFirstCostLineageReaderV1
    {
        public async Task<PurchaseFirstCostLineageV1> ReadAsync(WorkInboxEventV1 envelope, AccountingInventoryCostSourceV1 source,
            AccountingInventoryCostComponentV1 component, Func<CancellationToken, Task> fence, CancellationToken cancellationToken = default)
        {
            var actual = await inner.ReadAsync(envelope, source, component, fence, cancellationToken);
            return actual with { Receipts = actual.Receipts.Select(x => x with { Shipments = [] }).ToArray() };
        }
    }
}
