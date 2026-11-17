using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Application.Inbox;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Cancellation_before_initial_cost_acceptance_joins_later_actual_posting_only_after_the_matching_owner_receipt_in_both_event_orders()
    {
        foreach (var receiptFirst in new[] { true, false })
        {
            await using var run = await Run.Start(); var period = await run.Period(); var physical = new AttributionLineage(run.Source.Facts);
            await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical);
            var actual = new AttributedCostReader(run.Source.Facts, ledger, physical);
            var cancellation = HistoricalCancellationRequest(run.Source.Facts);
            var sourceReader = new HistoricalCostReader(new CancellationCostReader(actual, run.Source.Facts), cancellation);
            var store = AttributedCosts(scope, sourceReader, ledger);
            await Assert.ThrowsAsync<AccountingStoreException>(() => store.AcceptAsync(actual.Envelope));
            Assert.Equal(0, await run.Count("accounting.InventoryCostSources"));
            if (receiptFirst) await RecordHistoricalCancellation(scope, cancellation);
            await PostAttributed(scope, physical, ledger, period);
            if (!receiptFirst)
            {
                var retry = await Assert.ThrowsAsync<AccountingInvoiceJournalRetryException>(() => store.AcceptAsync(actual.Envelope));
                Assert.Equal("inventory_cost_cancellation_receipt_pending", retry.Reason);
                Assert.Equal(0, await run.Count("accounting.InventoryCostSources"));
                Assert.Equal(0, await run.Count("accounting.InvoiceCogsEconomicClaims"));
                await RecordHistoricalCancellation(scope, cancellation);
            }
            await store.AcceptAsync(actual.Envelope);
            var item = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
            Assert.Equal("ComponentCancellationPending", item.Status); Assert.Null(item.JournalId);
            Assert.Equal(1, await run.Count("accounting.Journals")); // The real manufacturing aggregate, never a second initial SMM.
            await Assert.ThrowsAsync<AccountingStoreException>(() => store.PrepareAsync(item.ProposalId, item.Kind,
                CostPreparation(period), Maker, Fence, Metadata(Maker)));
            await store.ProposeReversalAsync(item.ProposalId, CancelRequest(item, period), Maker, Fence, Metadata(Maker));
            var approved = await store.ApproveCancellationAsync(item.ProposalId, new(item.Version, 1), Checker, Fence, Metadata(Checker));
            Assert.Equal("33.34", Assert.Single(approved.ComponentCancellation!.Components).SignedAmount);
            Assert.Equal("CancelledByComponentReversal", Assert.Single(await store.ListAsync(item.Kind)).Status);
            await store.AcceptAsync(actual.Envelope);
            Assert.Equal(2, await run.Count("accounting.Journals"));
            Assert.Equal(1, await run.Count("accounting.InventoryCostSources"));
            Assert.Equal(1, await run.Count("accounting.InventoryCostCancellationClaims"));
            Assert.Equal(1, await run.Count("accounting.InvoiceCogsEconomicClaims"));
        }
    }

    [SqlServerFact]
    public async Task Deferred_cancellation_source_refuses_a_different_real_receipt_without_accepting_or_claiming_its_posted_cost()
    {
        await using var run = await Run.Start(); var period = await run.Period(); var physical = new AttributionLineage(run.Source.Facts);
        await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical);
        await PostAttributed(scope, physical, ledger, period);
        var actual = new AttributedCostReader(run.Source.Facts, ledger, physical);
        var cancellation = HistoricalCancellationRequest(run.Source.Facts); await RecordHistoricalCancellation(scope, cancellation);
        var invalid = new HistoricalCostReader(new CancellationCostReader(actual, run.Source.Facts),
            cancellation with { CancellationId = Guid.NewGuid() });
        var store = AttributedCosts(scope, invalid, ledger);
        var error = await Assert.ThrowsAsync<AccountingStoreException>(() => store.AcceptAsync(actual.Envelope));
        Assert.Equal("inventory_cost_cancellation_proof_invalid", error.Code);
        Assert.Equal(0, await run.Count("accounting.InventoryCostSources"));
        Assert.Equal(0, await run.Count("accounting.InvoiceCogsEconomicClaims"));
        Assert.Equal(1, await run.Count("accounting.Journals"));
    }

    private static AccountingSourceInvoiceCancellationV1 HistoricalCancellationRequest(FinalizedInvoiceLineSourceV1 facts) =>
        new(Company, Maker.ToString("D"), "SENTETIK-historical-cancellation", null, Guid.NewGuid(),
            AccountingSourceDocumentKindsV1.SalesInvoice, facts.FinalizationAdmissionId, facts.InvoiceId,
            facts.FinalizedVersion, Maker, At, "SENTETIK cost arrived after official cancellation");
    private static Task<AccountingSourceInvoiceCancellationAnswerV1> RecordHistoricalCancellation(Scope scope,
        AccountingSourceInvoiceCancellationV1 request) =>
        new AccountingSourceInvoiceCancellationStoreV1(scope.Db, scope.CompanyContext, scope.Audit, TimeProvider.System)
            .CancelAsync(request, Metadata(Maker) with { CorrelationId = request.CorrelationId }, Fence);
    private sealed class HistoricalCostReader(IAccountingInventoryCostSourceReaderV1 inner, AccountingSourceInvoiceCancellationV1 cancellation)
        : IAccountingInventoryCostSourceReaderV1
    {
        public async Task<AccountingInventoryCostSourceV1> ReadAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken = default) =>
            (await inner.ReadAsync(envelope, cancellationToken)) with { HistoricalCancellation = new(cancellation.CancellationId,
                cancellation.CancelledBy, cancellation.CancelledAtUtc, cancellation.Reason, new string('A', 64)) };
    }
}
