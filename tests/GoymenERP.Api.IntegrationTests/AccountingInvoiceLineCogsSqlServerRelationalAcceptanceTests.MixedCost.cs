using GoymenERP.BuildingBlocks.Messaging;
using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_mixed_line_posts_only_its_original_cost_then_corrects_both_real_components_without_reposting_the_aggregate()
    {
        await using var run = await Run.Start(); var period = await run.Period();
        var physical = new AttributionLineage(run.Source.Facts);
        await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical);
        await PostAttributed(scope, physical, ledger, period);
        var originalAggregate = await scope.Db.Journals.AsNoTracking().SingleAsync();
        var reader = new MixedCostReader(new(run.Source.Facts, ledger, physical), 25.125m);
        var store = AttributedCosts(scope, reader, ledger);
        await store.AcceptAsync(reader.Envelope); await store.AcceptAsync(reader.Envelope);
        var received = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        Assert.Equal("AwaitingPreparation", received.Status);
        Assert.Equal("58.465", received.ExactTlAmount); Assert.Equal("25.125", received.UnpostedTlAmountText);
        Assert.Equal(new[] { 1, 2 }, await scope.Db.InvoiceCogsEconomicClaims.AsNoTracking().OrderBy(x => x.Origin).Select(x => x.Origin).ToArrayAsync());
        var prepared = await store.PrepareAsync(received.ProposalId, received.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        Assert.Equal("25.13", prepared.Preparation!.SignedAmount);
        Assert.Equal(new("-1", "200"), prepared.Preparation.Rounding!.ExactSignedResidue);
        var posted = await store.ApproveAsync(received.ProposalId, received.Kind, 2, Checker, Fence, Metadata(Checker));
        Assert.Equal(2, await run.Count("accounting.Journals"));
        var correction = AttributedCorrections(scope, ledger);
        var history = await correction.ReadAsync(run.Source.Facts);
        Assert.Equal("MixedPostedCogs", history.PostingEvidenceState);
        Assert.Equal(posted.JournalId, history.PostedEvidence!.JournalId);
        Assert.Equal("33.34", Assert.Single(history.AttributedEvidence!).Source.SignedAmount);
        var proposal = await correction.PrepareAsync(run.Source.Facts, Request(run.Source.Facts, period), Maker, Fence, Metadata(Maker));
        var approved = await correction.ApproveAsync(run.Source.Facts, proposal.ProposalId, 1, Checker, Fence, Metadata(Checker, true));
        Assert.NotNull(approved.ReplacementCorrectionJournalId); Assert.Single(approved.AttributedCorrections!);
        Assert.Equal(6, await run.Count("accounting.Journals"));
        var unchanged = await scope.Db.JournalLines.AsNoTracking().SingleAsync(x => x.JournalId == originalAggregate.Id && x.LineNumber == 1);
        Assert.Equal(100.01m, unchanged.Debit); Assert.Equal(Old, unchanged.AccountId);
        var latest = await correction.ReadAsync(run.Source.Facts);
        Assert.Equal("MixedPostedCogs", latest.PostingEvidenceState);
        Assert.Equal("25.13", latest.PostedEvidence!.Amount); Assert.Equal(Next, latest.PostedEvidence.CogsAccountId);
        Assert.Equal(Next, Assert.Single(latest.AttributedEvidence!).CurrentCogsAccountId);
        await store.AcceptAsync(reader.Envelope);
        Assert.Equal(6, await run.Count("accounting.Journals")); Assert.Equal(2, await run.Count("accounting.InvoiceCogsEconomicClaims"));
    }

    [SqlServerFact]
    public async Task A_mixed_line_cannot_claim_the_same_physical_cost_twice_or_commit_a_half_approved_correction()
    {
        await using var run = await Run.Start(); var period = await run.Period(); var physical = new AttributionLineage(run.Source.Facts);
        await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical);
        await PostAttributed(scope, physical, ledger, period);
        var reader = new MixedCostReader(new(run.Source.Facts, ledger, physical), 10m);
        var store = AttributedCosts(scope, reader, ledger); await store.AcceptAsync(reader.Envelope);
        var received = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        await Assert.ThrowsAsync<AccountingStoreException>(() => store.AcceptAsync(reader.Envelope with { EventId = Guid.NewGuid() }));
        Assert.Equal(1, await run.Count("accounting.InventoryCostSources")); Assert.Equal(2, await run.Count("accounting.InvoiceCogsEconomicClaims"));
        await store.PrepareAsync(received.ProposalId, received.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        await store.ApproveAsync(received.ProposalId, received.Kind, 2, Checker, Fence, Metadata(Checker));
        var corrections = AttributedCorrections(scope, ledger);
        var proposed = await corrections.PrepareAsync(run.Source.Facts, Request(run.Source.Facts, period), Maker, Fence, Metadata(Maker));
        var auditBefore = await run.Count("audit.AuditEvents");
        Task LostAfterSave(CancellationToken _) => scope.Db.Journals.AsNoTracking().Count() > 2
            ? throw new InvalidOperationException("SENTETIK lost authority after both components saved") : Task.CompletedTask;
        await Assert.ThrowsAsync<InvalidOperationException>(() => corrections.ApproveAsync(run.Source.Facts, proposed.ProposalId,
            1, Checker, LostAfterSave, Metadata(Checker, true)));
        Assert.Equal(2, await run.Count("accounting.Journals")); Assert.Equal(0, await run.Count("accounting.InvoiceLineCogsRevisions"));
        Assert.Equal(auditBefore, await run.Count("audit.AuditEvents"));
        await corrections.ApproveAsync(run.Source.Facts, proposed.ProposalId, 1, Checker, Fence, Metadata(Checker, true));
        Assert.Equal(6, await run.Count("accounting.Journals"));
    }

    [SqlServerFact]
    public async Task Zero_known_cost_in_a_mixed_line_keeps_disjoint_claims_without_another_initial_journal()
    {
        await using var run = await Run.Start(); var period = await run.Period(); var physical = new AttributionLineage(run.Source.Facts);
        await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical);
        await PostAttributed(scope, physical, ledger, period);
        var reader = new MixedCostReader(new(run.Source.Facts, ledger, physical), 0m);
        var store = AttributedCosts(scope, reader, ledger); await store.AcceptAsync(reader.Envelope);
        var received = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        Assert.Equal("AlreadyAccounted", received.Status); Assert.Equal("0", received.UnpostedTlAmountText);
        await Assert.ThrowsAsync<AccountingStoreException>(() => store.PrepareAsync(received.ProposalId, received.Kind,
            CostPreparation(period), Maker, Fence, Metadata(Maker)));
        Assert.Equal(1, await run.Count("accounting.Journals")); Assert.Equal(2, await run.Count("accounting.InvoiceCogsEconomicClaims"));
    }

    [SqlServerFact]
    public async Task A_mixed_preparation_without_its_unposted_rounding_proof_cannot_use_the_legacy_total_fallback()
    {
        await using var run = await Run.Start(); var period = await run.Period(); var physical = new AttributionLineage(run.Source.Facts);
        await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical);
        await PostAttributed(scope, physical, ledger, period);
        var reader = new MixedCostReader(new(run.Source.Facts, ledger, physical), 10m);
        var store = AttributedCosts(scope, reader, ledger); await store.AcceptAsync(reader.Envelope);
        var received = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        var prepared = await store.PrepareAsync(received.ProposalId, received.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        // An explicitly malformed appended owner record simulates loss of the new proof; no existing row or trigger is changed.
        var malformed = prepared with { Version = 3, Preparation = prepared.Preparation! with { Rounding = null, SignedAmount = prepared.ExactTlAmount } };
        scope.Db.InventoryCostProposals.Add(new() {
            CompanyId = malformed.CompanyId, Id = malformed.ProposalId, Version = malformed.Version,
            EventId = malformed.EventId, ComponentKey = malformed.Component.ComponentKey, Status = 2,
            PreparedBy = Maker, PeriodId = period, RecordedAtUtc = DateTimeOffset.UtcNow,
            ViewJson = JsonSerializer.Serialize(malformed, new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
        await scope.Db.SaveChangesAsync(); scope.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<AccountingStoreException>(() => store.ApproveAsync(received.ProposalId, received.Kind,
            3, Checker, Fence, Metadata(Checker)));
        Assert.Equal(1, await run.Count("accounting.Journals"));
    }

    // Synthetic immutable Inventory Known stamp + commercial source; actual SQL monetary, claim and correction owners.
    private sealed class MixedCostReader(AttributedCostReader attributed, decimal known) : IAccountingInventoryCostSourceReaderV1
    {
        private readonly Guid knownMovement = Guid.NewGuid();
        public WorkInboxEventV1 Envelope => attributed.Envelope;
        public async Task<AccountingInventoryCostSourceV1> ReadAsync(WorkInboxEventV1 envelope, CancellationToken token = default)
        {
            var source = await attributed.ReadAsync(envelope, token); var original = source.Components[0];
            var physical = original.SalesPhysicalClaims![0] with { MovementId = knownMovement, QuantityKg = 2m };
            var exact = AccountingInventoryCostAmountsV1.FromDecimal(known);
            return source with { Components = [original with {
                SignedTlAmount = AccountingInventoryCostAmountsV1.Add(original.SignedTlAmount, exact),
                UnpostedTlAmount = exact, SalesPhysicalClaims = [.. original.SalesPhysicalClaims, physical] }] };
        }
    }
}
