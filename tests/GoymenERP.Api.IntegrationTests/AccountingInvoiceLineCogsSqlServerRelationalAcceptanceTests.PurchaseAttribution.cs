using System.Data;
using System.Text.Json;
using GoymenERP.Api.AccountingRuntime;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Purchase_cost_attribution_Down_passes_without_evidence_and_refuses_before_pending_or_posted_source_proof_is_lost()
    {
        await using var run = await Run.Start(); var period = await run.Period();
        var guard = Assert.IsType<SqlOperation>(new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddPostedPurchaseCostAttribution().DownOperations[0]).Sql;
        await run.Execute(guard);
        var reader = new ActualPurchaseCostReader(AccountingPurchaseInvoiceCostSourceTests.FirstSource(.01m, Company));
        await using var scope = run.Open(); var store = Costs(scope, reader, lineage: new PurchaseFirstCostLineageReaderV1(scope.CompanyContext));
        await store.AcceptAsync(reader.Envelope);
        Assert.Equal(51804, (await Assert.ThrowsAsync<SqlException>(() => run.Execute(guard))).Number);
        Assert.Equal(1, await run.Count("accounting.PurchaseCostAttributionSources"));
        Assert.Equal(2, await run.Count("accounting.PurchaseCostAttributionCandidates"));
        var proposal = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.PurchaseInvoiceDifference));
        await store.PrepareAsync(proposal.ProposalId, proposal.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        await store.ApproveAsync(proposal.ProposalId, proposal.Kind, 2, Checker, Fence, Metadata(Checker));
        Assert.Equal(51804, (await Assert.ThrowsAsync<SqlException>(() => run.Execute(guard))).Number);
        Assert.Equal(1, await run.Count("accounting.PurchaseCostAttributionApprovals"));
        Assert.Equal(1, await run.Count("accounting.Journals"));
    }

    [SqlServerFact]
    public async Task First_purchase_candidates_are_pending_until_a_different_actor_posts_one_total_and_replay_keeps_the_same_physical_shares()
    {
        await using var run = await Run.Start(); var period = await run.Period();
        var reader = new ActualPurchaseCostReader(AccountingPurchaseInvoiceCostSourceTests.FirstSource(.01m, Company));
        await using var scope = run.Open(); var store = Costs(scope, reader, lineage: new PurchaseFirstCostLineageReaderV1(scope.CompanyContext));
        await store.AcceptAsync(reader.Envelope); await store.AcceptAsync(reader.Envelope);
        var proposal = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.PurchaseInvoiceDifference));
        Assert.Equal(2, await run.Count("accounting.PurchaseCostAttributionCandidates"));
        var pending = await ReadPurchaseShares(scope, reader);
        Assert.All(pending, x => { Assert.True(x.HasPendingCandidate); Assert.Empty(x.Shares); });
        await store.PrepareAsync(proposal.ProposalId, proposal.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        await Assert.ThrowsAsync<AccountingStoreException>(() => store.ApproveAsync(proposal.ProposalId, proposal.Kind, 2, Maker, Fence, Metadata(Maker)));
        Assert.Equal(0, await run.Count("accounting.PurchaseCostAttributionApprovals"));
        await store.ApproveAsync(proposal.ProposalId, proposal.Kind, 2, Checker, Fence, Metadata(Checker));
        var actual = await ReadPurchaseShares(scope, reader);
        Assert.All(actual, x => Assert.False(x.HasPendingCandidate));
        Assert.Equal(new[] { "0", "0.01" }, actual.Select(x => Assert.Single(x.Shares).SignedAmount));
        Assert.All(actual, x => Assert.True(PostedCostPhysicalCoverageV1.IsComplete([], x.Shares)));
        Assert.Equal(1, await run.Count("accounting.Journals")); Assert.Equal(1, await run.Count("accounting.PurchaseCostAttributionApprovals"));
        var saved = JsonSerializer.Serialize(actual, Json); await store.AcceptAsync(reader.Envelope);
        Assert.Equal(saved, JsonSerializer.Serialize(await ReadPurchaseShares(scope, reader), Json));
    }

    [SqlServerFact]
    public async Task A_first_purchase_rounded_to_zero_has_an_approved_source_witness_without_a_zero_journal_and_rolls_back_at_the_final_fence()
    {
        await using var run = await Run.Start(); var period = await run.Period();
        var reader = new ActualPurchaseCostReader(AccountingPurchaseInvoiceCostSourceTests.FirstSource(.004m, Company));
        await using var scope = run.Open(); var store = Costs(scope, reader, lineage: new PurchaseFirstCostLineageReaderV1(scope.CompanyContext));
        await store.AcceptAsync(reader.Envelope);
        var proposal = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.PurchaseInvoiceDifference));
        await store.PrepareAsync(proposal.ProposalId, proposal.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        Task RefuseAfterWitness(CancellationToken _)
        {
            if (scope.Db.ChangeTracker.Entries<PurchaseCostAttributionApprovalRow>().Any(x => x.State == EntityState.Added))
                throw new AccountingStoreException("synthetic_fence_revoked", "SENTETIK");
            return Task.CompletedTask;
        }
        await Assert.ThrowsAsync<AccountingStoreException>(() => store.ApproveAsync(proposal.ProposalId, proposal.Kind, 2,
            Checker, RefuseAfterWitness, Metadata(Checker)));
        Assert.Equal(0, await run.Count("accounting.PurchaseCostAttributionApprovals")); Assert.Equal(2, await run.Count("accounting.InventoryCostProposals"));
        var approved = await store.ApproveAsync(proposal.ProposalId, proposal.Kind, 2, Checker, Fence, Metadata(Checker));
        Assert.Equal("RoundedNoCost", approved.Status); Assert.Null(approved.JournalId);
        var shares = await ReadPurchaseShares(scope, reader);
        Assert.All(shares, read => { var share = Assert.Single(read.Shares); Assert.Equal("0", share.SignedAmount);
            Assert.Null(share.Approval.JournalId); Assert.Equal(new("1", "500"), share.ExactSignedAmount); });
        Assert.Equal(0, await run.Count("accounting.Journals"));
    }

    [SqlServerFact]
    public async Task Purchase_source_proofs_are_append_only_and_a_foreign_company_cannot_read_a_saved_share()
    {
        await using var run = await Run.Start(); var period = await run.Period();
        var reader = new ActualPurchaseCostReader(AccountingPurchaseInvoiceCostSourceTests.FirstSource(.01m, Company));
        await using var scope = run.Open(); var store = Costs(scope, reader, lineage: new PurchaseFirstCostLineageReaderV1(scope.CompanyContext));
        await store.AcceptAsync(reader.Envelope);
        Assert.Equal(51805, (await Assert.ThrowsAsync<SqlException>(() => run.Execute("UPDATE accounting.PurchaseCostAttributionCandidates SET SourceHash=SourceHash"))).Number);
        Assert.Equal(51806, (await Assert.ThrowsAsync<SqlException>(() => run.Execute("DELETE FROM accounting.PurchaseCostAttributionCandidates"))).Number);
        var proposal = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.PurchaseInvoiceDifference));
        await store.PrepareAsync(proposal.ProposalId, proposal.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        await store.ApproveAsync(proposal.ProposalId, proposal.Kind, 2, Checker, Fence, Metadata(Checker));
        var physical = reader.Physical()[0];
        await using var tx = await scope.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var consumerCompany = new MutableCompany(Company);
        var posted = new PurchasePostedCostAttributionStoreV1(scope.Db, consumerCompany);
        await Assert.ThrowsAsync<AccountingStoreException>(() => posted.ReadProvenSharesAsync(TestCompanyIds.CompanyB, physical.ShipmentId,
            physical.MovementId, physical.MovementLineNumber, physical.ProductId, physical.PhysicalQuantityKg, Fence));
        var movedFailure = await Assert.ThrowsAsync<AccountingStoreException>(() => posted.ReadProvenSharesAsync(Company, physical.ShipmentId,
            physical.MovementId, physical.MovementLineNumber, physical.ProductId, physical.PhysicalQuantityKg,
            _ => { consumerCompany.Set(TestCompanyIds.CompanyB); return Task.CompletedTask; }));
        Assert.Equal("company_context_mismatch", movedFailure.Code);
        Assert.Equal(TestCompanyIds.CompanyB, consumerCompany.CompanyId);
        Assert.Equal(Company, scope.CompanyContext.CompanyId);
        await tx.RollbackAsync();
        Assert.Equal(1, await run.Count("accounting.Journals"));
    }

    private static async Task<IReadOnlyList<PurchaseShipmentCostAttributionReadV1>> ReadPurchaseShares(Scope scope, ActualPurchaseCostReader source)
    {
        await using var tx = await scope.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var reader = new PurchasePostedCostAttributionStoreV1(scope.Db, scope.CompanyContext); var result = new List<PurchaseShipmentCostAttributionReadV1>();
        foreach (var shipment in source.Physical()) result.Add(await reader.ReadProvenSharesAsync(Company, shipment.ShipmentId,
            shipment.MovementId, shipment.MovementLineNumber, shipment.ProductId, shipment.PhysicalQuantityKg, Fence));
        await tx.CommitAsync(); return result;
    }

    private sealed class ActualPurchaseCostReader(InventoryPurchaseInvoiceCostApplicationResultV1 source) : IAccountingInventoryCostSourceReaderV1
    {
        public WorkInboxEventV1 Envelope { get; } = AccountingPurchaseInvoiceCostSourceTests.Envelope(source);
        public Task<AccountingInventoryCostSourceV1> ReadAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken = default) =>
            Task.FromResult(AccountingPurchaseInvoiceCostSourceV1.Read(envelope, source.CompanyId));
        public InventoryPurchaseFirstCostShipmentV1[] Physical() => source.InitialRecognitions!.Single().Receipts.SelectMany(x =>
            JsonSerializer.Deserialize<InventoryPurchaseFirstCostPhysicalSourceV1>(x.PhysicalSourceJson, Json)!.Shipments).ToArray();
    }
}
