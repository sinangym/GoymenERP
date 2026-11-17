using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Inventory_cost_history_Down_is_empty_safe_and_protects_actual_sources_and_versions_from_update_delete_or_rollback()
    {
        await using var run = await Run.Start();
        var guard = Assert.IsType<SqlOperation>(new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddInventoryCostProposals().DownOperations[0]).Sql;
        await run.Execute(guard);
        var reader = new CostReader(run.Source.Facts);
        await using (var scope = run.Open()) await Costs(scope, reader).AcceptAsync(reader.Envelope);
        Assert.Equal(51930, (await Assert.ThrowsAsync<SqlException>(() => run.Execute(guard))).Number);
        Assert.Equal(51931, (await Assert.ThrowsAsync<SqlException>(() => run.Execute("UPDATE [accounting].[InventoryCostSources] SET [PayloadHash] = REPLICATE('a',64)"))).Number);
        Assert.Equal(51932, (await Assert.ThrowsAsync<SqlException>(() => run.Execute("DELETE FROM [accounting].[InventoryCostProposals]"))).Number);
        Assert.Equal(1, await run.Count("accounting.InventoryCostSources"));
        Assert.Equal(1, await run.Count("accounting.InventoryCostProposals"));
    }

    [SqlServerFact]
    public async Task Initial_shipment_cost_is_received_once_and_posts_the_exact_line_trace_before_a_later_classification_correction()
    {
        await using var run = await Run.Start(); var period = await run.Period();
        var reader = new CostReader(run.Source.Facts);
        Guid id;
        await using (var scope = run.Open())
        {
            var store = Costs(scope, reader);
            await store.AcceptAsync(reader.Envelope); await store.AcceptAsync(reader.Envelope);
            Assert.Equal(1, reader.Calls);
            var received = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment)); id = received.ProposalId;
            var prepared = await store.PrepareAsync(id, received.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
            Assert.Equal(Old, prepared.Preparation!.Classification.AccountId);
            var refused = await Assert.ThrowsAsync<AccountingStoreException>(() => store.ApproveAsync(id, received.Kind, 2, Maker, Fence, Metadata(Maker)));
            Assert.Equal("inventory_cost_approval_refused", refused.Code);
            var posted = await store.ApproveAsync(id, received.Kind, 2, Checker, Fence, Metadata(Checker));
            Assert.Equal("Posted", posted.Status);
            var journal = await scope.Db.Journals.AsNoTracking().SingleAsync();
            Assert.Equal(run.Source.Facts.InvoiceId, journal.SourceId);
            Assert.Equal(InvoiceLineCogsPostingTraceV1.For(run.Source.Facts.InvoiceLineId, run.Source.Facts.FinalizedVersion), journal.PostingPurpose);
            var lines = await scope.Db.JournalLines.AsNoTracking().OrderBy(x => x.LineNumber).ToListAsync();
            Assert.Equal(100.01m, lines[0].Debit); Assert.Equal(100.01m, lines[1].Credit);
            var correction = await scope.Store.PrepareAsync(run.Source.Facts, Request(run.Source.Facts, period), Maker, Fence, Metadata(Maker));
            Assert.Equal(posted.JournalId, correction.PostedEvidence!.JournalId);
            var approved = await scope.Store.ApproveAsync(run.Source.Facts, correction.ProposalId, 1, Checker, Fence, Metadata(Checker));
            Assert.NotNull(approved.ReplacementCorrectionJournalId);
        }
        Assert.Equal(1, await run.Count("accounting.InventoryCostSources"));
        Assert.Equal(3, await run.Count("accounting.InventoryCostProposals"));
        Assert.Equal(3, await run.Count("accounting.Journals"));
    }

    [SqlServerFact]
    public async Task A_classification_approved_before_the_first_cost_journal_overrides_the_product_default_and_is_fenced_at_approval()
    {
        await using var run = await Run.Start(); var period = await run.Period(); var reader = new CostReader(run.Source.Facts);
        await using var scope = run.Open(); var store = Costs(scope, reader);
        await store.AcceptAsync(reader.Envelope);
        var received = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        await store.PrepareAsync(received.ProposalId, received.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        var correction = await scope.Store.PrepareAsync(run.Source.Facts, Request(run.Source.Facts, period), Maker, Fence, Metadata(Maker));
        await scope.Store.ApproveAsync(run.Source.Facts, correction.ProposalId, 1, Checker, Fence, Metadata(Checker));
        var stale = await Assert.ThrowsAsync<AccountingStoreException>(() => store.ApproveAsync(received.ProposalId, received.Kind, 2, Checker, Fence, Metadata(Checker)));
        Assert.Equal("inventory_cost_preparation_stale", stale.Code);
        var reprepare = await store.PrepareAsync(received.ProposalId, received.Kind, CostPreparation(period) with { ExpectedVersion = 2 }, Maker, Fence, Metadata(Maker));
        Assert.Equal("InvoiceLine", reprepare.Preparation!.Classification.Origin);
        Assert.Equal(Next, reprepare.Preparation.Classification.AccountId);
        await store.ApproveAsync(received.ProposalId, received.Kind, 3, Checker, Fence, Metadata(Checker));
        Assert.Equal(Next, (await scope.Db.JournalLines.AsNoTracking().SingleAsync(x => x.LineNumber == 1)).AccountId);
    }

    [SqlServerFact]
    public async Task A_cancelled_invoice_fences_delayed_cost_preparation_and_keeps_the_original_source_receipt()
    {
        await using var run = await Run.Start(); var period = await run.Period(); var reader = new CostReader(run.Source.Facts);
        await using var scope = run.Open();
        var cancel = new AccountingSourceInvoiceCancellationStoreV1(scope.Db, scope.CompanyContext, scope.Audit, TimeProvider.System);
        var request = new AccountingSourceInvoiceCancellationV1(Company, Maker.ToString("D"), "SENTETIK-cancellation", null,
            Guid.NewGuid(), AccountingSourceDocumentKindsV1.SalesInvoice, run.Source.Facts.FinalizationAdmissionId,
            run.Source.Facts.InvoiceId, run.Source.Facts.FinalizedVersion, Maker, At, "SENTETIK external cancellation");
        await cancel.CancelAsync(request, Metadata(Maker) with { CorrelationId = request.CorrelationId }, Fence);
        var store = Costs(scope, reader); await store.AcceptAsync(reader.Envelope);
        var received = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        Assert.Equal("Cancelled", received.Status);
        var refused = await Assert.ThrowsAsync<AccountingStoreException>(() => store.PrepareAsync(received.ProposalId, received.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker)));
        Assert.Equal("inventory_cost_source_cancelled", refused.Code);
        Assert.Equal(0, await run.Count("accounting.Journals"));
        Assert.Equal(1, await run.Count("accounting.InventoryCostSources"));
    }

    [SqlServerFact]
    public async Task Revoked_authority_after_posting_work_rolls_back_the_journal_cost_version_and_audit_together()
    {
        await using var run = await Run.Start(); var period = await run.Period(); var reader = new CostReader(run.Source.Facts);
        await using var scope = run.Open(); var store = Costs(scope, reader); await store.AcceptAsync(reader.Envelope);
        var received = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        await store.PrepareAsync(received.ProposalId, received.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        var auditBefore = await run.Count("audit.AuditEvents");
        Task RevokeAfterJournal(CancellationToken _) => scope.Db.Journals.AsNoTracking().Any()
            ? throw new InvalidOperationException("SENTETIK authority revoked after journal save") : Task.CompletedTask;
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApproveAsync(received.ProposalId, received.Kind, 2, Checker, RevokeAfterJournal, Metadata(Checker)));
        Assert.Equal(0, await run.Count("accounting.Journals"));
        Assert.Equal(2, await run.Count("accounting.InventoryCostProposals"));
        Assert.Equal(auditBefore, await run.Count("audit.AuditEvents"));
    }

    [SqlServerFact]
    public async Task Cancellation_reversal_targets_the_latest_real_line_correction_instead_of_reversing_the_original_component_twice()
    {
        await using var run = await Run.Start(); var period = await run.Period(); var reader = new CostReader(run.Source.Facts);
        await using var scope = run.Open(); var store = Costs(scope, reader); await store.AcceptAsync(reader.Envelope);
        var received = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        await store.PrepareAsync(received.ProposalId, received.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        var posted = await store.ApproveAsync(received.ProposalId, received.Kind, 2, Checker, Fence, Metadata(Checker));
        var correction = await scope.Store.PrepareAsync(run.Source.Facts, Request(run.Source.Facts, period), Maker, Fence, Metadata(Maker));
        var corrected = await scope.Store.ApproveAsync(run.Source.Facts, correction.ProposalId, 1, Checker, Fence, Metadata(Checker));
        var cancel = new AccountingSourceInvoiceCancellationStoreV1(scope.Db, scope.CompanyContext, scope.Audit, TimeProvider.System);
        var request = new AccountingSourceInvoiceCancellationV1(Company, Maker.ToString("D"), "SENTETIK-cancellation", null,
            Guid.NewGuid(), AccountingSourceDocumentKindsV1.SalesInvoice, run.Source.Facts.FinalizationAdmissionId,
            run.Source.Facts.InvoiceId, run.Source.Facts.FinalizedVersion, Maker, At, "SENTETIK external cancellation");
        await cancel.CancelAsync(request, Metadata(Maker) with { CorrelationId = request.CorrelationId }, Fence);
        var pending = await store.ProposeReversalAsync(posted.ProposalId, new(3, Guid.NewGuid(), period,
            new(2026, 11, 17), new(2026, 11, 17), "SENTETIK selected open period cancellation reversal"), Maker, Fence, Metadata(Maker));
        Assert.Equal("CancellationReversalPending", pending.Status);
        Assert.Equal(corrected.ReplacementCorrectionJournalId, pending.ReversalProposal!.SourceJournalId);
        Assert.Equal(posted.JournalId, pending.JournalId);
        Assert.Equal(corrected.ReplacementCorrectionJournalId, pending.CurrentPostedJournalId);
        Assert.NotEqual(posted.JournalId, pending.ReversalProposal.SourceJournalId);
        var read = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        Assert.Equal(posted.JournalId, read.JournalId);
        Assert.Equal(corrected.ReplacementCorrectionJournalId, read.CurrentPostedJournalId);
        Assert.Equal(read.CurrentPostedJournalId, read.ReversalProposal!.SourceJournalId);
        Assert.Equal(3, await run.Count("accounting.Journals"));
        Assert.Equal(1, await run.Count("accounting.JournalReversalProposals"));
        var later = Request(run.Source.Facts, period) with { ExpectedLineVersion = 1 };
        var refused = await Assert.ThrowsAsync<AccountingStoreException>(() => scope.Store.PrepareAsync(run.Source.Facts, later, Maker, Fence, Metadata(Maker)));
        Assert.Equal(InvoiceLineCogsCorrectionCodesV1.SourceChanged, refused.Code);
        Assert.Equal(3, await run.Count("accounting.Journals"));
    }

    private static PrepareAccountingInventoryCostV1 CostPreparation(Guid period) => new(1, period, new(2026, 11, 17), new(2026, 11, 17), Counter, "SENTETIK actual source cost proposal");
    private static AccountingInventoryCostStoreV1 Costs(Scope scope, CostReader reader) => new(scope.Db, scope.Posting, reader,
        new CogsMapping(), new Plan(), new CostCurrencies(), new(new AccountingSqlServerRelationalAcceptanceTests.SyntheticMoneyProfiles(Company)),
        scope.CompanyContext, scope.Audit, scope.AuditWriter, TimeProvider.System);
    private sealed class CostCurrencies : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MoneyCurrencyCandidateV1>>([new(Currency, "TRY", "SENTETIK TL")]);
    }
    private sealed class CogsMapping : IAccountingCogsAccountResolverV1
    {
        public Task<AccountingCogsAccountReadV1> ResolveAsync(Guid companyId, Guid productId, DateOnly businessDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccountingCogsAccountReadV1("Available", new(companyId, productId,
                InvoiceLineCogsClassificationV1.TradeGoods, "SENTETIK-product-1", "SENTETIK-class-1", Old, 1)));
    }
    private sealed class CostReader(FinalizedInvoiceLineSourceV1 facts) : IAccountingInventoryCostSourceReaderV1
    {
        public int Calls { get; private set; }
        public WorkInboxEventV1 Envelope { get; } = new(AccountingInventoryCostInboxV1.SalesSourceName, Guid.NewGuid(), Company,
            AccountingInventoryCostInboxV1.SalesEventType, 1, "{\"synthetic\":true}", At, "SENTETIK-initial-cost", null);
        public Task<AccountingInventoryCostSourceV1> ReadAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken = default)
        {
            Calls++;
            const string proof = "{\"syntheticOwnerProof\":true}";
            return Task.FromResult(new AccountingInventoryCostSourceV1(Company, envelope.EventId, AccountingInventoryCostKindV1.SalesInvoicedShipment,
                facts.InvoiceId, facts.FinalizedVersion, facts.FinalizationAdmissionId, facts.InvoiceDate, facts.OfficialInvoiceNumber,
                Maker, AccountingInventoryCostSourceValidationV1.Sha256(envelope.PayloadJson),
                [new(AccountingInventoryCostTraceV1.SalesComponent(facts.InvoiceLineId), facts.ProductId, facts.InvoiceLineId,
                    facts.InvoiceLineNo, null, new("10001", "100"), proof, AccountingInventoryCostSourceValidationV1.Sha256(proof))]));
        }
    }
}
