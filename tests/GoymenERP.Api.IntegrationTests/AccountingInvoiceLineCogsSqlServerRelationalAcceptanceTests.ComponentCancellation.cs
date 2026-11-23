using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Component_cancellation_Down_is_empty_safe_and_preserves_prepared_approved_and_claim_history_against_mutation()
    {
        await using var run = await Run.Start();
        var guard = Assert.IsType<SqlOperation>(new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddInventoryCostComponentCancellations().DownOperations[0]).Sql;
        await run.Execute(guard);
        var period = await run.Period(); var physical = new AttributionLineage(run.Source.Facts);
        await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical);
        await PostAttributed(scope, physical, ledger, period);
        var actual = new AttributedCostReader(run.Source.Facts, ledger, physical);
        var store = AttributedCosts(scope, new CancellationCostReader(actual, run.Source.Facts), ledger);
        await store.AcceptAsync(actual.Envelope); await CancelCostInvoice(scope, run.Source.Facts);
        var item = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        await store.ProposeReversalAsync(item.ProposalId, CancelRequest(item, period), Maker, Fence, Metadata(Maker));
        Assert.Equal(51812, (await Assert.ThrowsAsync<SqlException>(() => run.Execute(guard))).Number);
        Assert.Equal(51814, (await Assert.ThrowsAsync<SqlException>(() => run.Execute("DELETE FROM [accounting].[InventoryCostCancellationVersions]"))).Number);
        await store.ApproveCancellationAsync(item.ProposalId, new(item.Version, 1), Checker, Fence, Metadata(Checker));
        foreach (var table in new[] { "InventoryCostCancellationVersions", "InventoryCostCancellationClaims" })
        {
            Assert.Equal(51813, (await Assert.ThrowsAsync<SqlException>(() => run.Execute($"UPDATE [accounting].[{table}] SET [Sha256]=[Sha256]"))).Number);
        }
        Assert.Equal(51814, (await Assert.ThrowsAsync<SqlException>(() => run.Execute("DELETE FROM [accounting].[InventoryCostCancellationClaims]"))).Number);
        Assert.Equal(51812, (await Assert.ThrowsAsync<SqlException>(() => run.Execute(guard))).Number);
        Assert.Equal(2, await run.Count("accounting.InventoryCostCancellationVersions"));
        Assert.Equal(1, await run.Count("accounting.InventoryCostCancellationClaims"));
        Assert.Equal(2, await run.Count("accounting.Journals"));
    }

    [SqlServerFact]
    public async Task Concurrent_component_cancellation_approvals_write_one_actual_share_and_company_scoped_claim()
    {
        await using var run = await Run.Start(); var period = await run.Period(); var physical = new AttributionLineage(run.Source.Facts);
        AccountingInventoryCostProposalV1 item;
        await using (var scope = run.Open())
        {
            var ledger = AttributionLedger(scope, physical); await PostAttributed(scope, physical, ledger, period);
            var actual = new AttributedCostReader(run.Source.Facts, ledger, physical);
            var store = AttributedCosts(scope, new CancellationCostReader(actual, run.Source.Facts), ledger); await store.AcceptAsync(actual.Envelope);
            await CancelCostInvoice(scope, run.Source.Facts);
            item = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
            await store.ProposeReversalAsync(item.ProposalId, CancelRequest(item, period), Maker, Fence, Metadata(Maker));
        }
        async Task<bool> Approve()
        {
            await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical);
            var store = AttributedCosts(scope, new CostReader(run.Source.Facts), ledger);
            try { await store.ApproveCancellationAsync(item.ProposalId, new(item.Version, 1), Checker, Fence, Metadata(Checker)); return true; }
            catch (Exception e) when (e is AccountingVersionConflictException or AccountingStoreException) { return false; }
        }
        var results = await Task.WhenAll(Approve(), Approve()); Assert.Single(results, x => x);
        Assert.Equal(2, await run.Count("accounting.Journals")); Assert.Equal(1, await run.Count("accounting.InventoryCostCancellationClaims"));
        await using var foreign = run.Open(Guid.NewGuid());
        Assert.Empty(await foreign.Db.InventoryCostCancellationClaims.AsNoTracking().ToListAsync());
        Assert.Empty(await foreign.Db.InventoryCostCancellationVersions.AsNoTracking().ToListAsync());
    }
    [SqlServerFact]
    public async Task Official_cancellation_reverses_the_current_known_and_later_manufacturing_shares_once_without_reversing_the_aggregate()
    {
        await using var run = await Run.Start(); var period = await run.Period(); var physical = new AttributionLineage(run.Source.Facts);
        await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical);
        var originalReader = new KnownPhysicalCostReader(run.Source.Facts, physical.Target, 25.125m);
        var reader = new CancellationCostReader(originalReader, run.Source.Facts);
        var store = AttributedCosts(scope, reader, ledger); await store.AcceptAsync(originalReader.Envelope);
        var item = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        await store.PrepareAsync(item.ProposalId, item.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        await store.ApproveAsync(item.ProposalId, item.Kind, 2, Checker, Fence, Metadata(Checker));
        await PostAttributed(scope, physical, ledger, period);
        var corrections = AttributedCorrections(scope, ledger);
        var edit = await corrections.PrepareAsync(run.Source.Facts, Request(run.Source.Facts, period), Maker, Fence, Metadata(Maker));
        await corrections.ApproveAsync(run.Source.Facts, edit.ProposalId, 1, Checker, Fence, Metadata(Checker, true));
        await CancelCostInvoice(scope, run.Source.Facts);
        item = Assert.Single(await store.ListAsync(item.Kind)); Assert.Equal("ComponentCancellationPending", item.Status);
        var prepared = await store.ProposeReversalAsync(item.ProposalId, CancelRequest(item, period), Maker, Fence, Metadata(Maker));
        Assert.Equal(new[] { "25.13", "33.34" }, prepared.ComponentCancellation!.Components.Select(x => x.SignedAmount).Order());
        Assert.All(prepared.ComponentCancellation.Components, x => Assert.Equal(Next, x.CogsAccountId));
        var own = await Assert.ThrowsAsync<AccountingStoreException>(() => store.ApproveCancellationAsync(item.ProposalId,
            new(item.Version, 1), Maker, Fence, Metadata(Maker)));
        Assert.Equal("inventory_cost_approval_refused", own.Code);
        var approved = await store.ApproveCancellationAsync(item.ProposalId, new(item.Version, 1), Checker, Fence, Metadata(Checker));
        Assert.Equal("CancelledByComponentReversal", approved.Status); Assert.Equal(2, approved.ComponentCancellation!.Components.Count);
        Assert.Equal(8, await run.Count("accounting.Journals")); Assert.Equal(2, await run.Count("accounting.InventoryCostCancellationClaims"));
        Assert.Equal("CancelledByComponentReversal", Assert.Single(await store.ListAsync(item.Kind)).Status);
        await Assert.ThrowsAsync<AccountingVersionConflictException>(() => store.ApproveCancellationAsync(item.ProposalId,
            new(item.Version, 1), Checker, Fence, Metadata(Checker)));
        await store.AcceptAsync(originalReader.Envelope);
        Assert.Equal(8, await run.Count("accounting.Journals")); Assert.Equal(1, await run.Count("accounting.InvoiceCogsEconomicClaims"));
        Assert.Contains(await scope.Db.JournalLines.AsNoTracking().ToListAsync(), x => x.AccountId == Old && x.Debit == 100.01m);
    }

    [SqlServerFact]
    public async Task Component_cancellation_rolls_back_every_claim_journal_and_audit_after_final_authority_loss_and_refuses_a_closed_period()
    {
        await using var run = await Run.Start(); var period = await run.Period(); var physical = new AttributionLineage(run.Source.Facts);
        await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical); await PostAttributed(scope, physical, ledger, period);
        var actual = new AttributedCostReader(run.Source.Facts, ledger, physical);
        var store = AttributedCosts(scope, new CancellationCostReader(actual, run.Source.Facts), ledger); await store.AcceptAsync(actual.Envelope);
        await CancelCostInvoice(scope, run.Source.Facts);
        var item = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        await store.ProposeReversalAsync(item.ProposalId, CancelRequest(item, period), Maker, Fence, Metadata(Maker));
        var audits = await run.Count("audit.AuditEvents");
        Task Lost(CancellationToken _) => scope.Db.InventoryCostCancellationClaims.AsNoTracking().Any()
            ? throw new InvalidOperationException("SENTETIK permission lost after actual cancellation writes") : Task.CompletedTask;
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApproveCancellationAsync(item.ProposalId, new(item.Version, 1), Checker, Lost, Metadata(Checker)));
        Assert.Equal(1, await run.Count("accounting.Journals")); Assert.Equal(0, await run.Count("accounting.InventoryCostCancellationClaims"));
        Assert.Equal(audits, await run.Count("audit.AuditEvents"));
        await AccountingPeriodCarrySqlFixture.CloseAsync(run.Factory, Company, period,
            (await scope.Posting.FindPeriodAsync(period))!.Version, Checker, Metadata(Checker));
        scope.Db.ChangeTracker.Clear();
        var refused = await Assert.ThrowsAsync<AccountingStoreException>(() => store.ApproveCancellationAsync(item.ProposalId,
            new(item.Version, 1), Checker, Fence, Metadata(Checker)));
        Assert.Equal("inventory_cost_open_period_required", refused.Code);
        Assert.Equal(0, await run.Count("accounting.InventoryCostCancellationClaims"));
    }

    [SqlServerFact]
    public async Task A_real_zero_manufacturing_share_cancellation_records_only_its_actual_approval_witness_and_never_a_zero_journal()
    {
        await using var run = await Run.Start(); var period = await run.Period(); var physical = new AttributionLineage(run.Source.Facts, true);
        await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical); await PostAttributed(scope, physical, ledger, period);
        var actual = new AttributedCostReader(run.Source.Facts, ledger, physical);
        var store = AttributedCosts(scope, new CancellationCostReader(actual, run.Source.Facts), ledger); await store.AcceptAsync(actual.Envelope);
        await CancelCostInvoice(scope, run.Source.Facts);
        var item = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        var prepared = await store.ProposeReversalAsync(item.ProposalId, CancelRequest(item, period), Maker, Fence, Metadata(Maker));
        Assert.Equal("0", Assert.Single(prepared.ComponentCancellation!.Components).SignedAmount);
        var approved = await store.ApproveCancellationAsync(item.ProposalId, new(item.Version, 1), Checker, Fence, Metadata(Checker));
        Assert.Null(Assert.Single(approved.ComponentCancellation!.Components).ReversingJournalId);
        Assert.Equal(1, await run.Count("accounting.Journals")); Assert.Equal(1, await run.Count("accounting.InventoryCostCancellationClaims"));
        Assert.Equal("CancelledByComponentReversal", Assert.Single(await store.ListAsync(item.Kind)).Status);
    }

    [SqlServerFact]
    public async Task Purchase_attributed_cancellation_preserves_the_other_invoice_share_and_acknowledges_rounded_zero_without_a_new_journal()
    {
        foreach (var amount in new[] { 100.01m, .004m })
        {
            await using var run = await Run.Start(); var period = await run.Period(); await using var scope = run.Open();
            var fixture = await PreparePurchaseCorrection(scope, run.Source.Facts, period, amount);
            var store = Costs(scope, new CostReader(fixture.Facts));
            await CancelCostInvoice(scope, fixture.Facts);
            var item = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
            await store.ProposeReversalAsync(item.ProposalId, CancelRequest(item, period), Maker, Fence, Metadata(Maker));
            var approved = await store.ApproveCancellationAsync(item.ProposalId, new(item.Version, 1), Checker, Fence, Metadata(Checker));
            var part = Assert.Single(approved.ComponentCancellation!.Components);
            Assert.Equal("Purchase", part.Origin); Assert.Equal(amount == .004m ? "0" : "50.01", part.SignedAmount);
            Assert.Equal(amount == .004m ? 0 : 2, await run.Count("accounting.Journals"));
            Assert.Equal(1, await run.Count("accounting.InventoryCostCancellationClaims"));
            Assert.Equal("CancelledByComponentReversal", Assert.Single(await store.ListAsync(item.Kind)).Status);
        }
    }

    private static ProposeAccountingInventoryCostReversalV1 CancelRequest(AccountingInventoryCostProposalV1 item, Guid period) =>
        new(item.Version, Guid.NewGuid(), period, new(2026, 11, 18), new(2026, 11, 18), "SENTETIK exact economic share cancellation");
    private static async Task CancelCostInvoice(Scope scope, FinalizedInvoiceLineSourceV1 source)
    {
        var store = new AccountingSourceInvoiceCancellationStoreV1(scope.Db, scope.CompanyContext, scope.Audit, TimeProvider.System);
        var request = new AccountingSourceInvoiceCancellationV1(Company, Maker.ToString("D"), "SENTETIK-component-cancellation", null,
            Guid.NewGuid(), AccountingSourceDocumentKindsV1.SalesInvoice, source.FinalizationAdmissionId, source.InvoiceId,
            source.FinalizedVersion, Maker, At, "SENTETIK official cancellation");
        await store.CancelAsync(request, Metadata(Maker) with { CorrelationId = request.CorrelationId }, Fence);
    }
    private static string CancellationProof(FinalizedInvoiceLineSourceV1 source) => JsonSerializer.Serialize(new {
        source.CompanyId, source.InvoiceId, Version = source.FinalizedVersion, AdmissionId = source.FinalizationAdmissionId,
        LineId = source.InvoiceLineId, Claim = new { source.ShipmentId, source.DispatchNoteId, source.ProductId }, Synthetic = true }, Json);
    private sealed class CancellationCostReader(IAccountingInventoryCostSourceReaderV1 inner, FinalizedInvoiceLineSourceV1 facts)
        : IAccountingInventoryCostSourceReaderV1
    {
        public async Task<AccountingInventoryCostSourceV1> ReadAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken = default)
        {
            var source = await inner.ReadAsync(envelope, cancellationToken); var proof = CancellationProof(facts);
            return source with { Components = source.Components.Select(x => x with { SourceProofJson = proof,
                SourceProofSha256 = AccountingInventoryCostSourceValidationV1.Sha256(proof) }).ToArray() };
        }
    }
}
