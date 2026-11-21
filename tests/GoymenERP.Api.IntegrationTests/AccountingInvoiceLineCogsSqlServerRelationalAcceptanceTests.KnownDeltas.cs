using System.Data;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Known_initial_cost_and_a_later_manufacturing_delta_keep_one_initial_claim_and_correct_both_actual_posted_components()
    {
        await using var run = await Run.Start(); var period = await run.Period(); var physical = new AttributionLineage(run.Source.Facts);
        await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical);
        var reader = new KnownPhysicalCostReader(run.Source.Facts, physical.Target, 25.125m);
        var costs = AttributedCosts(scope, reader, ledger); await costs.AcceptAsync(reader.Envelope);
        var received = Assert.Single(await costs.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        await costs.PrepareAsync(received.ProposalId, received.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        var initial = await costs.ApproveAsync(received.ProposalId, received.Kind, 2, Checker, Fence, Metadata(Checker));
        await PostAttributed(scope, physical, ledger, period);
        var corrections = AttributedCorrections(scope, ledger);
        var history = await corrections.ReadAsync(run.Source.Facts);
        Assert.Equal("MixedPostedCogs", history.PostingEvidenceState);
        Assert.Equal(initial.JournalId, history.PostedEvidence!.JournalId); Assert.Equal("25.13", history.PostedEvidence.Amount);
        var delta = Assert.Single(history.AttributedEvidence!); Assert.Equal("33.34", delta.Source.SignedAmount);
        Assert.Equal(1, Assert.Single(await scope.Db.InvoiceCogsEconomicClaims.AsNoTracking().ToListAsync()).Origin);
        var prepared = await corrections.PrepareAsync(run.Source.Facts, Request(run.Source.Facts, period), Maker, Fence, Metadata(Maker));
        var approved = await corrections.ApproveAsync(run.Source.Facts, prepared.ProposalId, 1, Checker, Fence, Metadata(Checker, true));
        Assert.NotNull(approved.ReplacementCorrectionJournalId); Assert.Single(approved.AttributedCorrections!);
        Assert.Equal(6, await run.Count("accounting.Journals"));
        var original = await scope.Db.JournalLines.AsNoTracking().SingleAsync(x => x.JournalId == initial.JournalId && x.LineNumber == 1);
        var aggregate = await scope.Db.JournalLines.AsNoTracking().SingleAsync(x => x.JournalId == delta.Source.JournalId && x.LineNumber == 1);
        Assert.Equal(25.13m, original.Debit); Assert.Equal(100.01m, aggregate.Debit); Assert.Equal(Old, aggregate.AccountId);
        var latest = await corrections.ReadAsync(run.Source.Facts);
        Assert.Equal(Next, latest.PostedEvidence!.CogsAccountId); Assert.Equal(Next, Assert.Single(latest.AttributedEvidence!).CurrentCogsAccountId);
        await costs.AcceptAsync(reader.Envelope);
        Assert.Equal(6, await run.Count("accounting.Journals")); Assert.Equal(1, await run.Count("accounting.InvoiceCogsEconomicClaims"));
    }

    [SqlServerFact]
    public async Task A_negative_delta_requires_the_completed_known_initial_source_and_does_not_become_an_unknown_original_recognition()
    {
        await using var run = await Run.Start(); var period = await run.Period(); var basis = new AttributionLineage(run.Source.Facts);
        var physical = basis.Copy(basis.Source with { State = 4, Outputs = [basis.Source.Outputs[0] with {
            ConsumedTlDifference = -100.01m, OriginalTlAmount = -100.01m }] });
        await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical);
        var reader = new KnownPhysicalCostReader(run.Source.Facts, physical.Target, 50m);
        var costs = AttributedCosts(scope, reader, ledger); await costs.AcceptAsync(reader.Envelope);
        await PostAttributed(scope, physical, ledger, period, negative: true);
        var corrections = AttributedCorrections(scope, ledger);
        await Assert.ThrowsAsync<AccountingStoreException>(() => corrections.ReadAsync(run.Source.Facts));
        var received = Assert.Single(await costs.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        await costs.PrepareAsync(received.ProposalId, received.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        await costs.ApproveAsync(received.ProposalId, received.Kind, 2, Checker, Fence, Metadata(Checker));
        var history = await corrections.ReadAsync(run.Source.Facts);
        Assert.Equal("-33.34", Assert.Single(history.AttributedEvidence!).Source.SignedAmount);
        var prepared = await corrections.PrepareAsync(run.Source.Facts, Request(run.Source.Facts, period), Maker, Fence, Metadata(Maker));
        var auditBefore = await run.Count("audit.AuditEvents");
        Task LostAfterSave(CancellationToken _) => scope.Db.Journals.AsNoTracking().Count() > 2
            ? throw new InvalidOperationException("SENTETIK permission lost after known and delta writes") : Task.CompletedTask;
        await Assert.ThrowsAsync<InvalidOperationException>(() => corrections.ApproveAsync(run.Source.Facts, prepared.ProposalId,
            1, Checker, LostAfterSave, Metadata(Checker, true)));
        Assert.Equal(2, await run.Count("accounting.Journals")); Assert.Equal(auditBefore, await run.Count("audit.AuditEvents"));
        Assert.Equal(0, await run.Count("accounting.InvoiceLineCogsRevisions"));
        await corrections.ApproveAsync(run.Source.Facts, prepared.ProposalId, 1, Checker, Fence, Metadata(Checker, true));
        Assert.Equal(6, await run.Count("accounting.Journals"));
        await using var tx = await scope.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var missingRecognition = await Assert.ThrowsAsync<AccountingStoreException>(() => ledger.ReadProvenSharesAsync(Company,
            physical.Target.ShipmentId, physical.Target.MovementId, 1, physical.Target.ProductId, 1m, Fence));
        Assert.Equal("manufacturing_attribution_root_incomplete", missingRecognition.Code);
    }

    [SqlServerFact]
    public async Task Proven_zero_and_rounded_zero_known_sources_allow_only_the_later_actual_delta_without_an_invented_initial_journal()
    {
        foreach (var known in new[] { 0m, 0.004m })
        {
            await using var run = await Run.Start(); var period = await run.Period(); var physical = new AttributionLineage(run.Source.Facts);
            await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical);
            var reader = new KnownPhysicalCostReader(run.Source.Facts, physical.Target, known);
            var costs = AttributedCosts(scope, reader, ledger); await costs.AcceptAsync(reader.Envelope);
            var received = Assert.Single(await costs.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
            if (known != 0)
            {
                await costs.PrepareAsync(received.ProposalId, received.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
                received = await costs.ApproveAsync(received.ProposalId, received.Kind, 2, Checker, Fence, Metadata(Checker));
            }
            Assert.Equal(known == 0 ? "NoCost" : "RoundedNoCost", received.Status); Assert.Null(received.JournalId);
            Assert.Equal(0, await run.Count("accounting.Journals"));
            await PostAttributed(scope, physical, ledger, period);
            var corrections = AttributedCorrections(scope, ledger); var history = await corrections.ReadAsync(run.Source.Facts);
            Assert.Equal("AttributedPostedCogs", history.PostingEvidenceState); Assert.Null(history.PostedEvidence);
            Assert.Equal("33.34", Assert.Single(history.AttributedEvidence!).Source.SignedAmount);
            var prepared = await corrections.PrepareAsync(run.Source.Facts, Request(run.Source.Facts, period), Maker, Fence, Metadata(Maker));
            var approved = await corrections.ApproveAsync(run.Source.Facts, prepared.ProposalId, 1, Checker, Fence, Metadata(Checker, true));
            Assert.Null(approved.ReplacementCorrectionJournalId); Assert.Single(approved.AttributedCorrections!);
            Assert.Equal(3, await run.Count("accounting.Journals")); Assert.Equal(1, await run.Count("accounting.InvoiceCogsEconomicClaims"));
        }
    }

    // Immutable Inventory Known stamp and commercial identity are synthetic. Claim, proposal, journal and correction owners use real SQL.
    private sealed class KnownPhysicalCostReader(FinalizedInvoiceLineSourceV1 facts, ManufacturingAttributionShipmentV1 physical,
        decimal amount) : IAccountingInventoryCostSourceReaderV1
    {
        public WorkInboxEventV1 Envelope { get; } = new(AccountingInventoryCostInboxV1.SalesSourceName, Guid.NewGuid(), Company,
            AccountingInventoryCostInboxV1.SalesEventType, 1, "{\"synthetic\":true}", At, "SENTETIK-known-before-delta", null);
        public Task<AccountingInventoryCostSourceV1> ReadAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken = default)
        {
            const string proof = "{\"syntheticImmutableKnownStamp\":true}";
            return Task.FromResult(new AccountingInventoryCostSourceV1(Company, envelope.EventId, AccountingInventoryCostKindV1.SalesInvoicedShipment,
                facts.InvoiceId, facts.FinalizedVersion, facts.FinalizationAdmissionId, facts.InvoiceDate, facts.OfficialInvoiceNumber,
                Maker, AccountingInventoryCostSourceValidationV1.Sha256(envelope.PayloadJson),
                [new(AccountingInventoryCostTraceV1.SalesComponent(facts.InvoiceLineId), facts.ProductId, facts.InvoiceLineId,
                    facts.InvoiceLineNo, null, AccountingInventoryCostAmountsV1.FromDecimal(amount), proof,
                    AccountingInventoryCostSourceValidationV1.Sha256(proof),
                    [new(physical.ShipmentId, physical.MovementId, physical.MovementLineNumber, physical.ProductId, physical.PhysicalQuantityKg)])]));
        }
    }
}
