using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Inventory_cost_readonly_claim_succession_rechecks_authority_between_inner_history_queries()
    {
        await using var run = await Run.Start();
        var period = await run.Period();
        var facts = run.Source.Facts;
        var physical = new AttributionLineage(facts);
        var nextFacts = Reinvoice(facts);
        var nextReader = new KnownPhysicalCostReader(nextFacts, physical.Target, 100m);
        Guid proposalId;
        await using (var seed = run.Open())
        {
            var ledger = AttributionLedger(seed, physical);
            var previousReader = new KnownPhysicalCostReader(facts, physical.Target, 100m);
            await SuccessionCosts(seed, new CancellationCostReader(previousReader, facts), ledger).AcceptAsync(previousReader.Envelope);
            await CancelCostInvoice(seed, facts);
            var raw = SuccessionCosts(seed, new CancellationCostReader(nextReader, nextFacts), ledger);
            await raw.AcceptAsync(nextReader.Envelope);
            var pending = Assert.Single((await raw.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment))
                .Where(x => x.InvoiceId == nextFacts.InvoiceId));
            proposalId = pending.ProposalId;
            await raw.PrepareAsync(proposalId, pending.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
            // The existing raw caller has no decision-read scope: its optional history callback remains a no-op.
            Assert.NotNull(Assert.Single((await raw.ListAsync(pending.Kind)).Where(x => x.ProposalId == proposalId)).Component.ClaimSuccession);
        }
        var before = await InventoryApprovalCounts(run);
        var claims = await run.Count("accounting.InvoiceCogsEconomicClaims");
        var successions = await run.Count("accounting.InvoiceCogsEconomicClaimSuccessions");
        Assert.Equal(1, successions);
        InventoryApprovalSqlScope? owner = null;
        var armed = false;
        var anchors = 0;
        var successors = 0;
        var probe = new InventoryApprovalQuery(sql =>
        {
            if (!armed) return;
            if (sql.Contains("[InvoiceCogsEconomicClaims]", StringComparison.Ordinal))
            {
                anchors++;
                owner!.Authority.Granted = false; // Normal completion after the actual anchor read; no callback exception.
            }
            if (sql.Contains("[InvoiceCogsEconomicClaimSuccessions]", StringComparison.Ordinal)) successors++;
        });
        await using (owner = new InventoryApprovalSqlScope(run, nextReader, new MutableCostProfiles(), extra: probe))
        {
            var allowed = await owner.Service.ReadDecisionAsync(proposalId, AccountingInventoryCostKindV1.SalesInvoicedShipment,
                _ => Task.CompletedTask);
            Assert.Equal(AccountingApiStatus.Ok, allowed.Status);
            Assert.NotNull(allowed.Resource?.Proposal.Component.ClaimSuccession);
            owner.AssertClean();
            armed = true;
            var denied = await owner.Service.ReadDecisionAsync(proposalId, AccountingInventoryCostKindV1.SalesInvoicedShipment,
                _ => Task.CompletedTask);
            Assert.Equal(AccountingApiStatus.Denied, denied.Status);
            Assert.Null(denied.Resource);
            Assert.Equal(1, anchors);
            Assert.Equal(0, successors); // The next inner history query must never execute after revocation.
            Assert.Equal(0, owner.Writer.Writes);
            Assert.Equal(0, owner.Writer.Discards);
            owner.AssertClean();
            Assert.Equal(before, await InventoryApprovalCounts(run));
            Assert.Equal(claims, await run.Count("accounting.InvoiceCogsEconomicClaims"));
            Assert.Equal(successions, await run.Count("accounting.InvoiceCogsEconomicClaimSuccessions"));
        }
    }
}
