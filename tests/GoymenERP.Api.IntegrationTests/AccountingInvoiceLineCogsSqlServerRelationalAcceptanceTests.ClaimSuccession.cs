using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Inbox;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Reinvoice_requires_the_actual_complete_prior_reversal_then_restores_only_its_amount_with_a_different_checker()
    {
        await using var run = await Run.Start(); var period = await run.Period(); var facts = run.Source.Facts;
        var physical = new AttributionLineage(facts); await using var scope = run.Open();
        var ledger = AttributionLedger(scope, physical);
        var a = new KnownPhysicalCostReader(facts, physical.Target, 25.125m);
        var store = SuccessionCosts(scope, new CancellationCostReader(a, facts), ledger);
        await store.AcceptAsync(a.Envelope);
        var original = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        await PostSuccession(store, original, period);
        var anchor = Assert.Single(await scope.Db.InvoiceCogsEconomicClaims.AsNoTracking().ToListAsync());
        var bFacts = Reinvoice(facts); var b = new KnownPhysicalCostReader(bFacts, physical.Target, 25.125m);
        var next = SuccessionCosts(scope, new CancellationCostReader(b, bFacts), ledger);
        await Assert.ThrowsAsync<AccountingInvoiceJournalRetryException>(() => next.AcceptAsync(b.Envelope));
        await CancelCostInvoice(scope, facts);
        await Assert.ThrowsAsync<AccountingInvoiceJournalRetryException>(() => next.AcceptAsync(b.Envelope));
        original = (await store.ListAsync(original.Kind)).Single(x => x.InvoiceId == facts.InvoiceId);
        await store.ProposeReversalAsync(original.ProposalId, CancelRequest(original, period), Maker, Fence, Metadata(Maker));
        await Assert.ThrowsAsync<AccountingInvoiceJournalRetryException>(() => next.AcceptAsync(b.Envelope));
        Assert.Equal(1, await run.Count("accounting.InventoryCostSources"));
        await store.ApproveCancellationAsync(original.ProposalId, new(original.Version, 1), Checker, Fence, Metadata(Checker));
        await next.AcceptAsync(b.Envelope); await next.AcceptAsync(b.Envelope);
        var received = (await next.ListAsync(original.Kind)).Single(x => x.InvoiceId == bFacts.InvoiceId);
        Assert.Equal("AwaitingPreparation", received.Status); Assert.False(received.Component.HasPostedComponents);
        Assert.Equal(new("201", "8"), received.Component.SignedTlAmount); // Original exact stamp, not its old rounded journal.
        var proof = Assert.IsType<InventoryCostClaimSuccessionV1>(received.Component.ClaimSuccession);
        Assert.Equal(facts.InvoiceId, proof.PreviousInvoiceId); Assert.Equal(1, Assert.Single(proof.Claims).PreviousGeneration);
        Assert.Equal("25.13", Assert.Single(proof.ReversedComponents).SignedAmount);
        await next.PrepareAsync(received.ProposalId, received.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        await Assert.ThrowsAsync<AccountingStoreException>(() => next.ApproveAsync(received.ProposalId, received.Kind, 2, Maker, Fence, Metadata(Maker)));
        var posted = await next.ApproveAsync(received.ProposalId, received.Kind, 2, Checker, Fence, Metadata(Checker));
        Assert.Equal("25.13", posted.Preparation!.SignedAmount);
        Assert.Equal(3, await run.Count("accounting.Journals"));
        Assert.Equal(1, await run.Count("accounting.InvoiceCogsEconomicClaimSuccessions"));
        Assert.Equal(anchor.ViewJson, Assert.Single(await scope.Db.InvoiceCogsEconomicClaims.AsNoTracking().ToListAsync()).ViewJson);
        Assert.Equal("CancelledByComponentReversal", (await next.ListAsync(original.Kind)).Single(x => x.InvoiceId == facts.InvoiceId).Status);
    }

    [SqlServerFact]
    public async Task Claim_succession_Down_is_empty_safe_and_rejects_retained_history_while_update_and_delete_cannot_change_the_generation()
    {
        await using var run = await Run.Start(); var facts = run.Source.Facts; var physical = new AttributionLineage(facts);
        var guard = Assert.IsType<SqlOperation>(new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddInvoiceCogsClaimSuccessions().DownOperations[0]).Sql;
        await run.Execute(guard);
        await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical);
        var a = new KnownPhysicalCostReader(facts, physical.Target, 100m);
        await SuccessionCosts(scope, new CancellationCostReader(a, facts), ledger).AcceptAsync(a.Envelope); await CancelCostInvoice(scope, facts);
        var bFacts = Reinvoice(facts); var b = new KnownPhysicalCostReader(bFacts, physical.Target, 100m);
        await SuccessionCosts(scope, new CancellationCostReader(b, bFacts), ledger).AcceptAsync(b.Envelope);
        var immutable = Assert.Single(await scope.Db.InvoiceCogsEconomicClaimSuccessions.AsNoTracking().ToListAsync());
        Assert.Equal(51840, (await Assert.ThrowsAsync<SqlException>(() => run.Execute(guard))).Number);
        Assert.Equal(51842, (await Assert.ThrowsAsync<SqlException>(() => run.Execute("UPDATE accounting.InvoiceCogsEconomicClaimSuccessions SET SuccessionSha256=SuccessionSha256"))).Number);
        Assert.Equal(51843, (await Assert.ThrowsAsync<SqlException>(() => run.Execute("DELETE FROM accounting.InvoiceCogsEconomicClaimSuccessions"))).Number);
        Assert.Equal(immutable.SuccessionJson, Assert.Single(await scope.Db.InvoiceCogsEconomicClaimSuccessions.AsNoTracking().ToListAsync()).SuccessionJson);
        Assert.Equal(1, await run.Count("accounting.InvoiceCogsEconomicClaims"));
        Assert.Equal(0, await run.Count("accounting.Journals"));
    }

    [SqlServerFact]
    public async Task Cancelled_before_initial_acceptance_can_pass_the_claim_to_two_successive_invoices_without_fabricating_a_prior_journal()
    {
        await using var run = await Run.Start(); var period = await run.Period(); var facts = run.Source.Facts;
        var physical = new AttributionLineage(facts); await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical);
        var cancellation = HistoricalCancellationRequest(facts); await RecordHistoricalCancellation(scope, cancellation);
        var a = new KnownPhysicalCostReader(facts, physical.Target, 100m);
        await SuccessionCosts(scope, new HistoricalCostReader(new CancellationCostReader(a, facts), cancellation), ledger).AcceptAsync(a.Envelope);
        var bFacts = Reinvoice(facts); var b = new KnownPhysicalCostReader(bFacts, physical.Target, 100m);
        var bStore = SuccessionCosts(scope, new CancellationCostReader(b, bFacts), ledger); await bStore.AcceptAsync(b.Envelope);
        var bItem = (await bStore.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment)).Single(x => x.InvoiceId == bFacts.InvoiceId);
        Assert.Empty(bItem.Component.ClaimSuccession!.ReversedComponents);
        Assert.Equal(0, await run.Count("accounting.Journals"));
        await PostSuccession(bStore, bItem, period); await CancelCostInvoice(scope, bFacts);
        bItem = (await bStore.ListAsync(bItem.Kind)).Single(x => x.InvoiceId == bFacts.InvoiceId);
        await bStore.ProposeReversalAsync(bItem.ProposalId, CancelRequest(bItem, period), Maker, Fence, Metadata(Maker));
        await bStore.ApproveCancellationAsync(bItem.ProposalId, new(bItem.Version, 1), Checker, Fence, Metadata(Checker));
        var cFacts = Reinvoice(facts); var c = new KnownPhysicalCostReader(cFacts, physical.Target, 100m);
        var cStore = SuccessionCosts(scope, new CancellationCostReader(c, cFacts), ledger); await cStore.AcceptAsync(c.Envelope);
        var cItem = (await cStore.ListAsync(bItem.Kind)).Single(x => x.InvoiceId == cFacts.InvoiceId);
        Assert.Equal(2, Assert.Single(cItem.Component.ClaimSuccession!.Claims).PreviousGeneration);
        await PostSuccession(cStore, cItem, period);
        Assert.Equal(3, await run.Count("accounting.Journals"));
        Assert.Equal(new long[] { 2, 3 }, await scope.Db.InvoiceCogsEconomicClaimSuccessions.OrderBy(x => x.Generation).Select(x => x.Generation).ToArrayAsync());
        Assert.Equal(1, await run.Count("accounting.InvoiceCogsEconomicClaims"));
        Assert.Equal("Cancelled", (await cStore.ListAsync(bItem.Kind)).Single(x => x.InvoiceId == facts.InvoiceId).Status);
        var corrections = new InvoiceLineCogsCorrectionStoreV1(scope.Db, scope.CompanyContext, new Plan(), scope.Audit,
            scope.AuditWriter, TimeProvider.System, ledger, new PurchasePostedCostAttributionStoreV1(scope.Db, scope.CompanyContext));
        await Assert.ThrowsAsync<AccountingStoreException>(() => corrections.ReadAsync(bFacts));
    }

    [SqlServerFact]
    public async Task Reinvoice_of_an_attributed_manufacturing_share_restores_the_reversed_share_without_a_second_global_manufacturing_journal()
    {
        await using var run = await Run.Start(); var period = await run.Period(); var facts = run.Source.Facts;
        var physical = new AttributionLineage(facts); await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical);
        await PostAttributed(scope, physical, ledger, period);
        var a = new AttributedCostReader(facts, ledger, physical);
        var aStore = SuccessionCosts(scope, new CancellationCostReader(a, facts), ledger); await aStore.AcceptAsync(a.Envelope);
        await CancelCostInvoice(scope, facts);
        var original = Assert.Single(await aStore.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        await aStore.ProposeReversalAsync(original.ProposalId, CancelRequest(original, period), Maker, Fence, Metadata(Maker));
        await aStore.ApproveCancellationAsync(original.ProposalId, new(original.Version, 1), Checker, Fence, Metadata(Checker));
        var bFacts = Reinvoice(facts); var b = new AttributedCostReader(bFacts, ledger, physical);
        var bStore = SuccessionCosts(scope, new CancellationCostReader(b, bFacts), ledger); await bStore.AcceptAsync(b.Envelope);
        var received = (await bStore.ListAsync(original.Kind)).Single(x => x.InvoiceId == bFacts.InvoiceId);
        Assert.Equal("AwaitingPreparation", received.Status); Assert.False(received.Component.HasPostedComponents);
        Assert.Equal(new("1667", "50"), received.Component.SignedTlAmount);
        Assert.Single(received.Component.ClaimSuccession!.CapturedCostFingerprints);
        await PostSuccession(bStore, received, period);
        var corrections = new InvoiceLineCogsCorrectionStoreV1(scope.Db, scope.CompanyContext, new Plan(), scope.Audit,
            scope.AuditWriter, TimeProvider.System, ledger, new PurchasePostedCostAttributionStoreV1(scope.Db, scope.CompanyContext));
        var current = await corrections.ReadAsync(bFacts);
        Assert.Equal("33.34", current.PostedEvidence!.Amount); Assert.Null(current.AttributedEvidence);
        Assert.Null(current.PurchaseAttributedEvidence);
        Assert.Equal(3, await run.Count("accounting.Journals"));
        Assert.Single(await scope.Db.JournalLines.AsNoTracking().Where(x => x.Debit == 100.01m).ToArrayAsync());
        await aStore.AcceptAsync(a.Envelope); await bStore.AcceptAsync(b.Envelope);
        Assert.Equal(3, await run.Count("accounting.Journals"));
    }

    [SqlServerFact]
    public async Task Reinvoice_of_a_purchase_share_retains_its_rounded_zero_witness_and_never_reposts_the_purchase_aggregate()
    {
        foreach (var exact in new[] { 100.01m, .004m })
        {
            await using var run = await Run.Start(); var period = await run.Period(); await using var scope = run.Open();
            var fixture = await PreparePurchaseCorrection(scope, run.Source.Facts, period, exact);
            var physical = new AttributionLineage(fixture.Facts); var ledger = AttributionLedger(scope, physical);
            var aStore = SuccessionCosts(scope, new CostReader(fixture.Facts), ledger);
            await CancelCostInvoice(scope, fixture.Facts);
            var original = Assert.Single(await aStore.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
            await aStore.ProposeReversalAsync(original.ProposalId, CancelRequest(original, period), Maker, Fence, Metadata(Maker));
            await aStore.ApproveCancellationAsync(original.ProposalId, new(original.Version, 1), Checker, Fence, Metadata(Checker));
            var header = await ReadCostReceipt(run, original.EventId); var bFacts = Reinvoice(fixture.Facts);
            var b = new FrozenReinvoiceCostReader(header, bFacts);
            var bStore = SuccessionCosts(scope, b, ledger); await bStore.AcceptAsync(b.Envelope);
            var current = (await bStore.ListAsync(original.Kind)).Single(x => x.InvoiceId == bFacts.InvoiceId);
            Assert.False(current.Component.HasPostedComponents);
            Assert.Equal(exact == .004m ? "NoCost" : "AwaitingPreparation", current.Status);
            Assert.Equal("Purchase", Assert.Single(current.Component.ClaimSuccession!.ReversedComponents).Origin);
            if (exact != .004m) await PostSuccession(bStore, current, period);
            Assert.Equal(exact == .004m ? 0 : 3, await run.Count("accounting.Journals"));
            Assert.Equal(1, await run.Count("accounting.InvoiceCogsEconomicClaimSuccessions"));
        }
    }

    [SqlServerFact]
    public async Task Concurrent_reinvoices_have_one_successor_and_replay_does_not_replace_the_original_claim_or_create_a_journal()
    {
        await using var run = await Run.Start(); var facts = run.Source.Facts; var physical = new AttributionLineage(facts);
        await using (var scope = run.Open())
        {
            var a = new KnownPhysicalCostReader(facts, physical.Target, 100m);
            await SuccessionCosts(scope, new CancellationCostReader(a, facts), AttributionLedger(scope, physical)).AcceptAsync(a.Envelope);
            await CancelCostInvoice(scope, facts);
        }
        var bFacts = Reinvoice(facts); var cFacts = Reinvoice(facts);
        var b = new KnownPhysicalCostReader(bFacts, physical.Target, 100m); var c = new KnownPhysicalCostReader(cFacts, physical.Target, 100m);
        async Task<bool> Accept(KnownPhysicalCostReader reader, FinalizedInvoiceLineSourceV1 value)
        {
            await using var scope = run.Open();
            try { await SuccessionCosts(scope, new CancellationCostReader(reader, value), AttributionLedger(scope, physical)).AcceptAsync(reader.Envelope); return true; }
            catch (Exception error) when (error is AccountingStoreException or AccountingInvoiceJournalRetryException) { return false; }
        }
        var results = await Task.WhenAll(Accept(b, bFacts), Accept(c, cFacts)); Assert.Single(results, x => x);
        Assert.True(await Accept(results[0] ? b : c, results[0] ? bFacts : cFacts));
        Assert.Equal(2, await run.Count("accounting.InventoryCostSources"));
        Assert.Equal(1, await run.Count("accounting.InvoiceCogsEconomicClaims"));
        Assert.Equal(1, await run.Count("accounting.InvoiceCogsEconomicClaimSuccessions"));
        Assert.Equal(0, await run.Count("accounting.Journals"));
        await using var foreign = run.Open(Guid.NewGuid());
        Assert.Empty(await foreign.Db.InvoiceCogsEconomicClaimSuccessions.AsNoTracking().ToListAsync());
    }

    [SqlServerFact]
    public async Task Known_cost_plus_prior_manufacturing_delta_is_restored_once_and_only_the_successor_observes_a_later_real_reverse_delta()
    {
        await using var run = await Run.Start(); var period = await run.Period(); var facts = run.Source.Facts;
        var physical = new AttributionLineage(facts); await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical);
        var a = new KnownPhysicalCostReader(facts, physical.Target, 25.125m);
        var aStore = SuccessionCosts(scope, new CancellationCostReader(a, facts), ledger); await aStore.AcceptAsync(a.Envelope);
        var original = Assert.Single(await aStore.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        await PostSuccession(aStore, original, period); await PostAttributed(scope, physical, ledger, period);
        await CancelCostInvoice(scope, facts);
        original = Assert.Single(await aStore.ListAsync(original.Kind));
        await aStore.ProposeReversalAsync(original.ProposalId, CancelRequest(original, period), Maker, Fence, Metadata(Maker));
        await aStore.ApproveCancellationAsync(original.ProposalId, new(original.Version, 1), Checker, Fence, Metadata(Checker));
        var bFacts = Reinvoice(facts); var b = new KnownPhysicalCostReader(bFacts, physical.Target, 25.125m);
        var bStore = SuccessionCosts(scope, new CancellationCostReader(b, bFacts), ledger); await bStore.AcceptAsync(b.Envelope);
        var received = (await bStore.ListAsync(original.Kind)).Single(x => x.InvoiceId == bFacts.InvoiceId);
        Assert.Equal(new("11693", "200"), received.Component.SignedTlAmount); // 25.125 exact known + 33.34 actual attributed.
        Assert.Equal(new("201", "8"), received.Component.ClaimSuccession!.OriginalSourceAmount);
        await PostSuccession(bStore, received, period);
        var reverse = physical.Copy(physical.Source with { GenerationVersion = "2", State = 4, CostDifferenceEventId = Guid.NewGuid(),
            ConsumedLineageReference = physical.Source.ConsumedLineageReference! with { GenerationVersion = "2" },
            Outputs = [physical.Source.Outputs[0] with { ConsumedTlDifference = -100.01m, OriginalTlAmount = -100.01m }] });
        var reverseLedger = AttributionLedger(scope, reverse); await PostAttributed(scope, reverse, reverseLedger, period, negative: true);
        var corrections = new InvoiceLineCogsCorrectionStoreV1(scope.Db, scope.CompanyContext, new Plan(), scope.Audit,
            scope.AuditWriter, TimeProvider.System, reverseLedger, new PurchasePostedCostAttributionStoreV1(scope.Db, scope.CompanyContext));
        var current = await corrections.ReadAsync(bFacts);
        Assert.Equal("58.47", current.PostedEvidence!.Amount);
        Assert.Equal("-33.34", Assert.Single(current.AttributedEvidence!).Source.SignedAmount);
        Assert.Equal("CancelledByComponentReversal", (await bStore.ListAsync(original.Kind)).Single(x => x.InvoiceId == facts.InvoiceId).Status);
        Assert.Equal(2, await run.Count("accounting.InventoryCostCancellationClaims"));
        Assert.Equal(6, await run.Count("accounting.Journals"));
    }

    [SqlServerFact]
    public async Task A_changed_actual_owner_or_company_during_successor_revalidation_rolls_back_the_source_and_append_only_generation()
    {
        await using var run = await Run.Start(); var facts = run.Source.Facts; var physical = new AttributionLineage(facts);
        await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical);
        var a = new KnownPhysicalCostReader(facts, physical.Target, 100m);
        await SuccessionCosts(scope, new CancellationCostReader(a, facts), ledger).AcceptAsync(a.Envelope); await CancelCostInvoice(scope, facts);
        var bFacts = Reinvoice(facts); var b = new KnownPhysicalCostReader(bFacts, physical.Target, 100m);
        var changed = new ChangedSuccessionOwner(new CancellationCostReader(b, bFacts));
        var failed = await Assert.ThrowsAsync<AccountingStoreException>(() => SuccessionCosts(scope, changed, ledger).AcceptAsync(b.Envelope));
        Assert.Equal("inventory_cost_source_changed", failed.Code);
        Assert.Equal(1, await run.Count("accounting.InventoryCostSources"));
        Assert.Equal(0, await run.Count("accounting.InvoiceCogsEconomicClaimSuccessions"));
        var consumerCompany = new MutableCompany(Company); var foreignCompany = Guid.NewGuid();
        var moved = new ChangedSuccessionOwner(new CancellationCostReader(b, bFacts), () => consumerCompany.Set(foreignCompany));
        var movedFailure = await Assert.ThrowsAsync<AccountingStoreException>(() => SuccessionCosts(scope, moved, ledger, consumerCompany).AcceptAsync(b.Envelope));
        Assert.Equal(AccountingStoreCodes.CompanyContextMismatch, movedFailure.Code);
        Assert.Equal(foreignCompany, consumerCompany.CompanyId);
        Assert.Equal(Company, scope.CompanyContext.CompanyId);
        Assert.Equal(1, await run.Count("accounting.InventoryCostSources"));
        Assert.Equal(0, await run.Count("accounting.InvoiceCogsEconomicClaimSuccessions"));
        await SuccessionCosts(scope, new CancellationCostReader(b, bFacts), ledger).AcceptAsync(b.Envelope);
        Assert.Equal(1, await run.Count("accounting.InvoiceCogsEconomicClaimSuccessions"));
    }

    private sealed class ChangedSuccessionOwner(IAccountingInventoryCostSourceReaderV1 inner, Action? change = null)
        : IAccountingInventoryCostSourceReaderV1
    {
        private int reads;
        public async Task<AccountingInventoryCostSourceV1> ReadAsync(WorkInboxEventV1 envelope, CancellationToken token = default)
        {
            var source = await inner.ReadAsync(envelope, token);
            if (++reads == 1) return source;
            if (change is not null) { change(); return source; }
            return source with { OfficialInvoiceNumber = source.OfficialInvoiceNumber + "-CHANGED" };
        }
    }

    private static FinalizedInvoiceLineSourceV1 Reinvoice(FinalizedInvoiceLineSourceV1 source) => source with {
        InvoiceId = Guid.NewGuid(), InvoiceLineId = Guid.NewGuid(), FinalizationAdmissionId = Guid.NewGuid() };
    private static AccountingInventoryCostStoreV1 SuccessionCosts(Scope scope, IAccountingInventoryCostSourceReaderV1 reader,
        ManufacturingPostedCostAttributionStoreV1 ledger, ICompanyContext? consumerCompany = null) => new(scope.Db, scope.Posting, reader, new CogsMapping(), new Plan(),
        new CostCurrencies(), new(new AccountingSqlServerRelationalAcceptanceTests.SyntheticMoneyProfiles(Company)), consumerCompany ?? scope.CompanyContext,
        scope.Audit, scope.AuditWriter, TimeProvider.System, ledger, postedPurchasing: new PurchasePostedCostAttributionStoreV1(scope.Db, scope.CompanyContext));
    private static async Task PostSuccession(AccountingInventoryCostStoreV1 store, AccountingInventoryCostProposalV1 item, Guid period)
    {
        await store.PrepareAsync(item.ProposalId, item.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        await store.ApproveAsync(item.ProposalId, item.Kind, 2, Checker, Fence, Metadata(Checker));
    }
    private sealed class FrozenReinvoiceCostReader(AccountingInventoryCostSourceV1 previous, FinalizedInvoiceLineSourceV1 facts)
        : IAccountingInventoryCostSourceReaderV1
    {
        public WorkInboxEventV1 Envelope { get; } = new(AccountingInventoryCostInboxV1.SalesSourceName, Guid.NewGuid(), Company,
            AccountingInventoryCostInboxV1.SalesEventType, 1, "{\"syntheticReinvoice\":true}", At, "SENTETIK-reinvoice", null);
        public Task<AccountingInventoryCostSourceV1> ReadAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken = default)
        {
            var proof = CancellationProof(facts); var old = Assert.Single(previous.Components);
            return Task.FromResult(previous with { EventId = envelope.EventId, InvoiceId = facts.InvoiceId,
                AdmissionId = facts.FinalizationAdmissionId, SourcePayloadSha256 = AccountingInventoryCostSourceValidationV1.Sha256(envelope.PayloadJson),
                Components = [old with { ComponentKey = AccountingInventoryCostTraceV1.SalesComponent(facts.InvoiceLineId),
                    InvoiceLineId = facts.InvoiceLineId, SourceProofJson = proof, SourceProofSha256 = AccountingInventoryCostSourceValidationV1.Sha256(proof) }] });
        }
    }
}
