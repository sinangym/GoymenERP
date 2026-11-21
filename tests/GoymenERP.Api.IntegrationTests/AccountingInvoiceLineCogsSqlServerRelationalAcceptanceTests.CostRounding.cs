using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Initial_cost_rounds_one_exact_component_total_once_and_keeps_the_residue_in_the_approved_source_trace()
    {
        await using var run = await Run.Start(); var period = await run.Period();
        var reader = new RoundedCostReader(run.Source.Facts, new("1", "3"));
        await using var scope = run.Open(); var store = Costs(scope, reader);
        await store.AcceptAsync(reader.Envelope);
        var received = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        var prepared = await store.PrepareAsync(received.ProposalId, received.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        Assert.Equal("1 / 3", prepared.ExactTlAmount);
        Assert.Equal("0.33", prepared.Preparation!.SignedAmount);
        Assert.Equal(new("1", "300"), prepared.Preparation.Rounding!.ExactSignedResidue);
        var posted = await store.ApproveAsync(received.ProposalId, received.Kind, 2, Checker, Fence, Metadata(Checker));
        Assert.Equal("Posted", posted.Status);
        var lines = await scope.Db.JournalLines.AsNoTracking().OrderBy(x => x.LineNumber).ToArrayAsync();
        Assert.Equal(0.33m, lines[0].Debit); Assert.Equal(0.33m, lines[1].Credit);
        Assert.Equal(prepared.Preparation.Rounding, posted.Preparation!.Rounding);
        Assert.Equal(1, await run.Count("accounting.Journals"));
        Assert.Equal(new("1", "3"), (Assert.Single(await store.ListAsync(received.Kind))).Component.SignedTlAmount);
    }

    [SqlServerFact]
    public async Task A_negative_late_purchase_component_keeps_its_signed_residue_and_posts_only_the_rounded_amount_in_the_opposite_direction()
    {
        await using var run = await Run.Start(); var period = await run.Period();
        var reader = new RoundedCostReader(run.Source.Facts, new("-1", "3"), purchase: true);
        await using var scope = run.Open(); var store = Costs(scope, reader);
        await store.AcceptAsync(reader.Envelope);
        var received = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.PurchaseInvoiceDifference));
        var prepared = await store.PrepareAsync(received.ProposalId, received.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        Assert.Equal("-0.33", prepared.Preparation!.SignedAmount);
        Assert.Equal(new("-1", "300"), prepared.Preparation.Rounding!.ExactSignedResidue);
        await store.ApproveAsync(received.ProposalId, received.Kind, 2, Checker, Fence, Metadata(Checker));
        var cogs = await scope.Db.JournalLines.AsNoTracking().SingleAsync(x => x.AccountId == Old);
        var counter = await scope.Db.JournalLines.AsNoTracking().SingleAsync(x => x.AccountId == Counter);
        Assert.Equal(0.33m, cogs.Credit); Assert.Equal(0.33m, counter.Debit);
        Assert.Equal(1, await run.Count("accounting.Journals"));
    }

    [SqlServerFact]
    public async Task A_nonzero_source_rounded_to_zero_requires_a_different_approver_and_preserves_cancellation_without_a_zero_journal()
    {
        await using var run = await Run.Start(); var period = await run.Period();
        var reader = new RoundedCostReader(run.Source.Facts, new("1", "250"));
        await using var scope = run.Open(); var store = Costs(scope, reader); await store.AcceptAsync(reader.Envelope);
        var source = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        await store.PrepareAsync(source.ProposalId, source.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        Assert.Equal("inventory_cost_approval_refused", (await Assert.ThrowsAsync<AccountingStoreException>(() =>
            store.ApproveAsync(source.ProposalId, source.Kind, 2, Maker, Fence, Metadata(Maker)))).Code);
        var approved = await store.ApproveAsync(source.ProposalId, source.Kind, 2, Checker, Fence, Metadata(Checker));
        Assert.Equal("RoundedNoCost", approved.Status); Assert.Null(approved.JournalId);
        Assert.Equal("0.004", approved.ExactTlAmount); Assert.Equal("0", approved.Preparation!.SignedAmount);
        Assert.Equal(new("1", "250"), approved.Preparation.Rounding!.ExactSignedResidue);
        Assert.Equal(0, await run.Count("accounting.Journals"));
        var cancel = new AccountingSourceInvoiceCancellationStoreV1(scope.Db, scope.CompanyContext, scope.Audit, TimeProvider.System);
        var request = new AccountingSourceInvoiceCancellationV1(Company, Maker.ToString("D"), "SENTETIK-rounded-cancel", null,
            Guid.NewGuid(), AccountingSourceDocumentKindsV1.SalesInvoice, run.Source.Facts.FinalizationAdmissionId,
            run.Source.Facts.InvoiceId, run.Source.Facts.FinalizedVersion, Maker, At, "SENTETIK external cancellation");
        await cancel.CancelAsync(request, Metadata(Maker) with { CorrelationId = request.CorrelationId }, Fence);
        var cancelled = Assert.Single(await store.ListAsync(source.Kind));
        Assert.Equal("RoundedNoCostCancelled", cancelled.Status); Assert.Equal(request.CancellationId, cancelled.CancellationId);
        Assert.Equal(Checker, cancelled.ApprovedBy); Assert.Equal(0, await run.Count("accounting.Journals"));
        Assert.Equal(3, await run.Count("accounting.InventoryCostProposals"));
    }

    [SqlServerFact]
    public async Task Revoked_authority_rolls_back_a_rounded_zero_approval_and_a_changed_money_profile_requires_a_new_preparation()
    {
        await using var run = await Run.Start(); var period = await run.Period(); var profiles = new MutableCostProfiles();
        var reader = new RoundedCostReader(run.Source.Facts, new("1", "250"));
        await using var scope = run.Open(); var store = Costs(scope, reader, profiles); await store.AcceptAsync(reader.Envelope);
        var source = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        await store.PrepareAsync(source.ProposalId, source.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        profiles.Version = 2;
        Assert.Equal("inventory_cost_preparation_stale", (await Assert.ThrowsAsync<AccountingStoreException>(() =>
            store.ApproveAsync(source.ProposalId, source.Kind, 2, Checker, Fence, Metadata(Checker)))).Code);
        profiles.Version = 1;
        var auditBefore = await run.Count("audit.AuditEvents");
        Task RevokeAfterAppend(CancellationToken _) => scope.Db.ChangeTracker.Entries<AccountingInventoryCostProposalRow>().Any(x => x.Entity.Status == 6)
            ? throw new InvalidOperationException("SENTETIK checker authority lost after append") : Task.CompletedTask;
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApproveAsync(source.ProposalId, source.Kind, 2, Checker, RevokeAfterAppend, Metadata(Checker)));
        Assert.Equal(2, await run.Count("accounting.InventoryCostProposals"));
        Assert.Equal(auditBefore, await run.Count("audit.AuditEvents")); Assert.Equal(0, await run.Count("accounting.Journals"));
    }

    [SqlServerFact]
    public async Task A_first_receipt_rounding_snapshot_is_not_revalued_by_a_different_current_profile_even_when_its_amount_is_zero()
    {
        await using var run = await Run.Start(); var period = await run.Period(); var profiles = new MutableCostProfiles();
        Assert.True(AccountingInventoryCostRoundingV1Rules.TryRound(new("1", "250"), Currency, 2, out _, out var rounding));
        var reader = new RoundedCostReader(run.Source.Facts, new("0", "1"), purchase: true,
            recorded: new(Currency, MutableCostProfiles.ProfileId, 1, 2, rounding!));
        await using var scope = run.Open(); var store = Costs(scope, reader, profiles); await store.AcceptAsync(reader.Envelope);
        var source = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.PurchaseInvoiceDifference));
        Assert.Equal("AwaitingPreparation", source.Status);
        profiles.Version = 2; profiles.CurrentId = Guid.NewGuid(); profiles.Scale = 3;
        var prepared = await store.PrepareAsync(source.ProposalId, source.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        Assert.Equal(rounding, prepared.Preparation!.Rounding);
        Assert.Equal(1, prepared.Component.RecordedRounding!.MoneyProfileVersion);
        Assert.Equal(MutableCostProfiles.ProfileId, prepared.Component.RecordedRounding.MoneyProfileId);
        Assert.Equal(2, prepared.Component.RecordedRounding.MoneyScale);
        Assert.Equal(2, prepared.Preparation.MoneyProfileVersion);
        Assert.Equal(profiles.CurrentId, prepared.Preparation.MoneyProfileId); Assert.Equal(3, prepared.Preparation.MoneyScale);
        profiles.Version = 3;
        Assert.Equal("inventory_cost_preparation_stale", (await Assert.ThrowsAsync<AccountingStoreException>(() =>
            store.ApproveAsync(source.ProposalId, source.Kind, 2, Checker, Fence, Metadata(Checker)))).Code);
        profiles.Version = 2;
        var approved = await store.ApproveAsync(source.ProposalId, source.Kind, 2, Checker, Fence, Metadata(Checker));
        Assert.Equal("RoundedNoCost", approved.Status); Assert.Equal(0, await run.Count("accounting.Journals"));
    }

    [SqlServerFact]
    public async Task An_owner_rounded_amount_uses_the_open_period_profile_without_revaluation_and_refuses_an_incompatible_scale()
    {
        await using var run = await Run.Start(); var period = await run.Period(); var profiles = new MutableCostProfiles();
        Assert.True(AccountingInventoryCostRoundingV1Rules.TryRound(new("1", "3"), Currency, 2, out var amount, out var rounding));
        var reader = new RoundedCostReader(run.Source.Facts, AccountingInventoryCostAmountsV1.FromDecimal(amount), purchase: true,
            recorded: new(Currency, MutableCostProfiles.ProfileId, 1, 2, rounding!));
        await using var scope = run.Open(); var store = Costs(scope, reader, profiles); await store.AcceptAsync(reader.Envelope);
        var source = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.PurchaseInvoiceDifference));
        profiles.Version = 2; profiles.CurrentId = Guid.NewGuid(); profiles.Scale = 1;
        Assert.Equal("inventory_cost_source_money_profile_changed", (await Assert.ThrowsAsync<AccountingStoreException>(() =>
            store.PrepareAsync(source.ProposalId, source.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker)))).Code);
        Assert.Equal(1, await run.Count("accounting.InventoryCostProposals"));
        profiles.Scale = 3;
        var prepared = await store.PrepareAsync(source.ProposalId, source.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        Assert.Equal("0.33", prepared.Preparation!.SignedAmount); Assert.Equal(rounding, prepared.Preparation.Rounding);
        Assert.Equal(2, prepared.Component.RecordedRounding!.MoneyScale); Assert.Equal(3, prepared.Preparation.MoneyScale);
        await store.ApproveAsync(source.ProposalId, source.Kind, 2, Checker, Fence, Metadata(Checker));
        var lines = await scope.Db.JournalLines.AsNoTracking().OrderBy(x => x.LineNumber).ToArrayAsync();
        Assert.Equal(0.33m, lines[0].Debit); Assert.Equal(0.33m, lines[1].Credit);
        Assert.Equal(1, await run.Count("accounting.Journals"));
    }

    [SqlServerFact]
    public async Task Cost_rounding_Down_is_empty_safe_and_refuses_once_an_exact_rounding_proof_would_be_lost()
    {
        await using var run = await Run.Start(); var period = await run.Period();
        var guard = Assert.IsType<SqlOperation>(new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddInventoryCostComponentRounding().DownOperations[0]).Sql;
        await run.Execute(guard);
        var reader = new RoundedCostReader(run.Source.Facts, new("1", "250"));
        await using var scope = run.Open(); var store = Costs(scope, reader); await store.AcceptAsync(reader.Envelope);
        var source = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        await run.Execute(guard);
        await store.PrepareAsync(source.ProposalId, source.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        Assert.Equal(51803, (await Assert.ThrowsAsync<SqlException>(() => run.Execute(guard))).Number);
        Assert.Equal(2, await run.Count("accounting.InventoryCostProposals"));
        await store.ApproveAsync(source.ProposalId, source.Kind, 2, Checker, Fence, Metadata(Checker));
        Assert.Equal(51803, (await Assert.ThrowsAsync<SqlException>(() => run.Execute(guard))).Number);
        Assert.Equal(3, await run.Count("accounting.InventoryCostProposals"));
    }

    [SqlServerFact]
    public async Task A_first_purchase_line_aggregate_replays_one_component_and_posts_one_owner_rounded_total_without_a_single_location()
    {
        await using var run = await Run.Start(); var period = await run.Period();
        // Actual adapter and immutable two-receipt lineage; only source facts are synthetic.
        var reader = new ActualPurchaseCostReader(AccountingPurchaseInvoiceCostSourceTests.FirstSource(.01m, Company,
            run.Source.Facts.InvoiceLineId, run.Source.Facts.ProductId));
        await using var scope = run.Open(); var store = Costs(scope, reader,
            lineage: new GoymenERP.Api.AccountingRuntime.PurchaseFirstCostLineageReaderV1(scope.CompanyContext));
        await store.AcceptAsync(reader.Envelope); await store.AcceptAsync(reader.Envelope);
        var source = Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.PurchaseInvoiceDifference));
        Assert.Equal(AccountingInventoryCostTraceV1.PurchaseFirstLineComponent(run.Source.Facts.InvoiceLineId), source.Component.ComponentKey);
        Assert.Null(source.Component.LocationId); Assert.Null(source.Component.InvoiceLineId);
        Assert.Equal(1, await run.Count("accounting.InventoryCostSources")); Assert.Equal(1, await run.Count("accounting.InventoryCostProposals"));
        await store.PrepareAsync(source.ProposalId, source.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        var approved = await store.ApproveAsync(source.ProposalId, source.Kind, 2, Checker, Fence, Metadata(Checker));
        Assert.Equal("0.01", approved.Preparation!.SignedAmount); Assert.Equal(1, await run.Count("accounting.Journals"));
        var lines = await scope.Db.JournalLines.AsNoTracking().OrderBy(x => x.LineNumber).ToArrayAsync();
        Assert.Equal(0.01m, lines[0].Debit); Assert.Equal(0.01m, lines[1].Credit);
    }

    private sealed class RoundedCostReader(FinalizedInvoiceLineSourceV1 facts, AccountingInventoryCostRatioV1 amount,
        bool purchase = false, AccountingInventoryCostRecordedRoundingV1? recorded = null, bool aggregate = false) : IAccountingInventoryCostSourceReaderV1
    {
        private readonly CostReader inner = new(facts);
        private readonly Guid location = Guid.NewGuid();
        private readonly Guid acceptance = Guid.NewGuid();
        public WorkInboxEventV1 Envelope => purchase ? inner.Envelope with { SourceName = AccountingInventoryCostInboxV1.InventorySourceName,
            EventType = AccountingInventoryCostInboxV1.PurchaseDifferenceEventType } : inner.Envelope;
        public async Task<AccountingInventoryCostSourceV1> ReadAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken = default)
        {
            var source = await inner.ReadAsync(envelope, cancellationToken);
            var component = source.Components[0] with { SignedTlAmount = amount };
            if (purchase) component = component with { InvoiceLineId = null, InvoiceLineNumber = null, LocationId = aggregate ? null : location,
                ComponentKey = aggregate ? AccountingInventoryCostTraceV1.PurchaseFirstLineComponent(facts.InvoiceLineId)
                    : recorded is null ? AccountingInventoryCostTraceV1.PurchaseComponent(component.ProductId, location)
                    : AccountingInventoryCostTraceV1.PurchaseFirstComponent(acceptance, facts.InvoiceLineId), RecordedRounding = recorded };
            return source with { Kind = purchase ? AccountingInventoryCostKindV1.PurchaseInvoiceDifference : source.Kind, Components = [component] };
        }
    }

    private sealed class MutableCostProfiles : ISettingsPrerequisiteProvider
    {
        public static readonly Guid ProfileId = new("5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a99");
        public Guid CurrentId { get; set; } = ProfileId;
        public long Version { get; set; } = 1;
        public int Scale { get; set; } = 2;
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MoneyProfileV1(Company, currencyId, "SYN", operation, new(CurrentId, Version), Scale, true, new(2026, 1, 1), null));
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
