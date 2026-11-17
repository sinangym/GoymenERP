using System.Globalization;
using GoymenERP.Api.AccountingRuntime;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_purchase_posted_share_corrects_only_its_real_amount_and_repeated_classification_keeps_the_original_aggregate()
    {
        await using var run = await Run.Start(); var period = await run.Period(); await using var scope = run.Open();
        var fixture = await PreparePurchaseCorrection(scope, run.Source.Facts, period, 100.01m);
        var corrections = PurchaseCorrections(scope);
        var history = await corrections.ReadAsync(fixture.Facts);
        Assert.Equal("AttributedPostedCogs", history.PostingEvidenceState); Assert.Null(history.AttributedEvidence);
        var linked = Assert.Single(history.PurchaseAttributedEvidence!);
        Assert.Equal("50.01", linked.Source.SignedAmount); Assert.Equal("100.01", linked.CurrentJournalTotal);
        var proposal = await corrections.PrepareAsync(fixture.Facts, Request(fixture.Facts, period), Maker, Fence, Metadata(Maker));
        await Assert.ThrowsAsync<AccountingStoreException>(() => corrections.ApproveAsync(fixture.Facts, proposal.ProposalId, 1, Maker, Fence, Metadata(Maker, true)));
        var approved = await corrections.ApproveAsync(fixture.Facts, proposal.ProposalId, 1, Checker, Fence, Metadata(Checker, true));
        Assert.Null(approved.ReplacementCorrectionJournalId);
        var pair = Assert.Single(approved.AttributedCorrections!);
        Assert.Equal(3, await run.Count("accounting.Journals"));
        var original = await scope.Db.JournalLines.AsNoTracking().SingleAsync(x => x.JournalId == linked.CurrentJournalId && x.LineNumber == 1);
        Assert.Equal(100.01m, original.Debit); Assert.Equal(Old, original.AccountId);
        var replacement = await scope.Db.JournalLines.AsNoTracking().SingleAsync(x => x.JournalId == pair.ReplacementCorrectionJournalId && x.LineNumber == 1);
        Assert.Equal(50.01m, replacement.Debit); Assert.Equal(Next, replacement.AccountId);
        var latest = await corrections.ReadAsync(fixture.Facts);
        Assert.Equal(pair.ReplacementCorrectionJournalId, Assert.Single(latest.PurchaseAttributedEvidence!).CurrentJournalId);
        var again = await corrections.PrepareAsync(fixture.Facts, Request(fixture.Facts, period) with {
            ExpectedLineVersion = 1, CogsAccountId = Old }, Maker, Fence, Metadata(Maker));
        await corrections.ApproveAsync(fixture.Facts, again.ProposalId, 1, Checker, Fence, Metadata(Checker, true));
        Assert.Equal(5, await run.Count("accounting.Journals"));
        Assert.Equal(Old, Assert.Single((await corrections.ReadAsync(fixture.Facts)).PurchaseAttributedEvidence!).CurrentCogsAccountId);
        Assert.Equal(1, await run.Count("accounting.InvoiceCogsEconomicClaims"));
    }

    [SqlServerFact]
    public async Task A_purchase_rounded_zero_allows_a_different_approver_to_change_classification_without_inventing_a_journal()
    {
        await using var run = await Run.Start(); var period = await run.Period(); await using var scope = run.Open();
        var fixture = await PreparePurchaseCorrection(scope, run.Source.Facts, period, .004m);
        var corrections = PurchaseCorrections(scope);
        var history = await corrections.ReadAsync(fixture.Facts);
        Assert.Equal("AttributedPostedCogs", history.PostingEvidenceState);
        Assert.Null(Assert.Single(history.PurchaseAttributedEvidence!).CurrentJournalId);
        var prepared = await corrections.PrepareAsync(fixture.Facts, Request(fixture.Facts, period), Maker, Fence, Metadata(Maker));
        var approved = await corrections.ApproveAsync(fixture.Facts, prepared.ProposalId, 1, Checker, Fence, Metadata(Checker, true));
        Assert.Empty(approved.AttributedCorrections!); Assert.Null(approved.ReplacementCorrectionJournalId);
        Assert.Equal(0, await run.Count("accounting.Journals")); Assert.Equal(1, await run.Count("accounting.InvoiceLineCogsRevisions"));
        Assert.Equal("RoundedNoCost", Assert.Single(approved.PurchaseAttributedEvidence!).Source.Approval.Status);
    }

    [SqlServerFact]
    public async Task A_purchase_component_correction_rolls_back_both_new_journals_and_the_revision_when_authority_is_lost()
    {
        await using var run = await Run.Start(); var period = await run.Period(); await using var scope = run.Open();
        var fixture = await PreparePurchaseCorrection(scope, run.Source.Facts, period, 100.01m);
        var corrections = PurchaseCorrections(scope);
        var prepared = await corrections.PrepareAsync(fixture.Facts, Request(fixture.Facts, period), Maker, Fence, Metadata(Maker));
        var auditBefore = await run.Count("audit.AuditEvents");
        Task Lost(CancellationToken _) => scope.Db.Journals.AsNoTracking().Count() > 1
            ? throw new InvalidOperationException("SENTETIK final authority loss") : Task.CompletedTask;
        await Assert.ThrowsAsync<InvalidOperationException>(() => corrections.ApproveAsync(fixture.Facts, prepared.ProposalId, 1, Checker, Lost, Metadata(Checker, true)));
        Assert.Equal(1, await run.Count("accounting.Journals")); Assert.Equal(0, await run.Count("accounting.InvoiceLineCogsRevisions"));
        Assert.Equal(auditBefore, await run.Count("audit.AuditEvents"));
        await corrections.ApproveAsync(fixture.Facts, prepared.ProposalId, 1, Checker, Fence, Metadata(Checker, true));
        Assert.Equal(3, await run.Count("accounting.Journals"));
    }

    private static InvoiceLineCogsCorrectionStoreV1 PurchaseCorrections(Scope scope) => new(scope.Db,
        scope.CompanyContext, new Plan(), scope.Audit, scope.AuditWriter, TimeProvider.System,
        postedPurchasing: new PurchasePostedCostAttributionStoreV1(scope.Db, scope.CompanyContext));

    private static async Task<(FinalizedInvoiceLineSourceV1 Facts, AccountingInventoryCostProposalV1 Purchase)> PreparePurchaseCorrection(
        Scope scope, FinalizedInvoiceLineSourceV1 facts, Guid period, decimal exact)
    {
        var reader = new ActualPurchaseCostReader(AccountingPurchaseInvoiceCostSourceTests.FirstSource(exact, Company, sourceProductId: facts.ProductId));
        var store = Costs(scope, reader, lineage: new PurchaseFirstCostLineageReaderV1(scope.CompanyContext));
        await store.AcceptAsync(reader.Envelope);
        var source = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.PurchaseInvoiceDifference));
        await store.PrepareAsync(source.ProposalId, source.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        var approved = await store.ApproveAsync(source.ProposalId, source.Kind, 2, Checker, Fence, Metadata(Checker));
        var target = reader.Physical()[1]; facts = facts with { ShipmentId = target.ShipmentId };
        var sales = new PurchaseClaimCostReader(facts, target, new(scope.Db, scope.CompanyContext));
        await Costs(scope, sales).AcceptAsync(sales.Envelope);
        return (facts, approved);
    }

    // The physical/commercial fixture is synthetic; purchase approval, physical claim and correction stores are actual owners.
    private sealed class PurchaseClaimCostReader(FinalizedInvoiceLineSourceV1 facts, InventoryPurchaseFirstCostShipmentV1 target,
        PurchasePostedCostAttributionStoreV1 ledger) : IAccountingInventoryCostSourceReaderV1
    {
        public WorkInboxEventV1 Envelope { get; } = new(AccountingInventoryCostInboxV1.SalesSourceName, Guid.NewGuid(), Company,
            AccountingInventoryCostInboxV1.SalesEventType, 1, "{\"syntheticPurchaseClaim\":true}", At, "SENTETIK-purchase-claim", null);
        public async Task<AccountingInventoryCostSourceV1> ReadAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken = default)
        {
            var actual = await ledger.ReadProvenSharesAsync(Company, target.ShipmentId, target.MovementId,
                target.MovementLineNumber, target.ProductId, target.PhysicalQuantityKg, Fence, cancellationToken);
            if (actual.HasPendingCandidate) throw new AccountingStoreException("synthetic_pending", "SENTETIK pending purchase");
            const string proof = "{\"syntheticSalesOwner\":true}";
            var total = actual.Shares.Aggregate(AccountingInventoryCostAmountsV1.FromDecimal(0), (sum, share) =>
                AccountingInventoryCostAmountsV1.Add(sum, AccountingInventoryCostAmountsV1.FromDecimal(decimal.Parse(share.SignedAmount, CultureInfo.InvariantCulture))));
            return new(Company, envelope.EventId, AccountingInventoryCostKindV1.SalesInvoicedShipment, facts.InvoiceId, facts.FinalizedVersion,
                facts.FinalizationAdmissionId, facts.InvoiceDate, facts.OfficialInvoiceNumber, Maker, AccountingInventoryCostSourceValidationV1.Sha256(envelope.PayloadJson),
                [new(AccountingInventoryCostTraceV1.SalesComponent(facts.InvoiceLineId), facts.ProductId, facts.InvoiceLineId, facts.InvoiceLineNo,
                    null, total, proof, AccountingInventoryCostSourceValidationV1.Sha256(proof),
                    [new(target.ShipmentId, target.MovementId, target.MovementLineNumber, target.ProductId, target.PhysicalQuantityKg)],
                    AlreadyPostedPurchaseComponents: actual.Shares)]);
        }
    }
}
