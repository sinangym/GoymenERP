using System.Data;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Attribution_Down_is_empty_safe_and_posted_evidence_cannot_be_updated_deleted_or_dropped()
    {
        await using var run = await Run.Start();
        var guard = Assert.IsType<SqlOperation>(new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddPostedManufacturingCostAttribution().DownOperations[0]).Sql;
        await run.Execute(guard);
        var period = await run.Period(); var physical = new AttributionLineage(run.Source.Facts);
        await using (var scope = run.Open()) await PostAttributed(scope, physical, AttributionLedger(scope, physical), period);
        Assert.Equal(51800, (await Assert.ThrowsAsync<SqlException>(() => run.Execute(guard))).Number);
        Assert.Equal(51801, (await Assert.ThrowsAsync<SqlException>(() => run.Execute("UPDATE [accounting].[ManufacturingCostAttributions] SET [SourceHash] = REPLICATE('A',64)"))).Number);
        Assert.Equal(51802, (await Assert.ThrowsAsync<SqlException>(() => run.Execute("DELETE FROM [accounting].[ManufacturingCostAttributionShares]"))).Number);
        Assert.Equal(1, await run.Count("accounting.ManufacturingCostAttributions"));
        Assert.Equal(2, await run.Count("accounting.ManufacturingCostAttributionShares"));
    }
    [SqlServerFact]
    public async Task Already_posted_manufacturing_money_is_attributed_once_and_only_the_invoice_share_is_corrected()
    {
        await using var run = await Run.Start(); var period = await run.Period();
        var physical = new AttributionLineage(run.Source.Facts);
        await using var scope = run.Open();
        var ledger = AttributionLedger(scope, physical);
        var manufacturing = ManufacturingCosts(scope, ledger);
        await manufacturing.AcceptAsync(physical.Envelope, physical.Source);
        await manufacturing.PrepareAsync(new(physical.Envelope.EventId, 1, period, new(2026, 11, 17), new(2026, 11, 17),
            Old, Counter, "SENTETIK actual posted aggregate"), Maker, Fence, Metadata(Maker));
        var posted = await manufacturing.ApproveAsync(physical.Envelope.EventId, 2, Checker, Fence, Metadata(Checker));
        Assert.Equal(1, await run.Count("accounting.Journals"));
        Assert.Equal(1, await run.Count("accounting.ManufacturingCostAttributions"));
        Assert.Equal(2, await run.Count("accounting.ManufacturingCostAttributionShares"));
        var reader = new AttributedCostReader(run.Source.Facts, ledger, physical);
        var costs = AttributedCosts(scope, reader, ledger);
        await costs.AcceptAsync(reader.Envelope); await costs.AcceptAsync(reader.Envelope);
        var received = Assert.Single(await costs.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        Assert.Equal("AlreadyAccounted", received.Status);
        Assert.Equal("33.34", Assert.Single(received.Component.AlreadyPostedComponents!).SignedAmount);
        await Assert.ThrowsAsync<AccountingStoreException>(() => costs.PrepareAsync(received.ProposalId, received.Kind,
            CostPreparation(period), Maker, Fence, Metadata(Maker)));
        Assert.Equal(1, await run.Count("accounting.Journals"));
        Assert.Equal(1, await run.Count("accounting.InvoiceCogsEconomicClaims"));

        var correction = AttributedCorrections(scope, ledger);
        var prepared = await correction.PrepareAsync(run.Source.Facts, Request(run.Source.Facts, period), Maker, Fence, Metadata(Maker));
        Assert.Null(prepared.PostedEvidence);
        Assert.Equal(posted.JournalId, Assert.Single(prepared.AttributedEvidence!).CurrentJournalId);
        var approved = await correction.ApproveAsync(run.Source.Facts, prepared.ProposalId, 1, Checker, Fence, Metadata(Checker, true));
        var changed = Assert.Single(approved.AttributedCorrections!);
        Assert.Equal(3, await run.Count("accounting.Journals"));
        var original = await scope.Db.JournalLines.AsNoTracking().Where(x => x.JournalId == posted.JournalId).OrderBy(x => x.LineNumber).ToListAsync();
        Assert.Equal(100.01m, original[0].Debit); Assert.Equal(Old, original[0].AccountId);
        var replacement = await scope.Db.JournalLines.AsNoTracking().Where(x => x.JournalId == changed.ReplacementCorrectionJournalId).OrderBy(x => x.LineNumber).ToListAsync();
        Assert.Equal(33.34m, replacement[0].Debit); Assert.Equal(Next, replacement[0].AccountId);
        Assert.Equal(Counter, replacement[1].AccountId);
        var history = await correction.ReadAsync(run.Source.Facts);
        Assert.Equal("AttributedPostedCogs", history.PostingEvidenceState);
        Assert.Equal(changed.ReplacementCorrectionJournalId, Assert.Single(history.AttributedEvidence!).CurrentJournalId);
        Assert.Equal(Next, Assert.Single(history.AttributedEvidence!).CurrentCogsAccountId);
    }

    [SqlServerFact]
    public async Task A_delayed_invoice_waits_for_actual_posting_then_retries_without_a_second_money_event()
    {
        await using var run = await Run.Start(); var period = await run.Period();
        var physical = new AttributionLineage(run.Source.Facts);
        await using (var scope = run.Open())
        {
            var ledger = AttributionLedger(scope, physical); var reader = new AttributedCostReader(run.Source.Facts, ledger, physical);
            await Assert.ThrowsAsync<AccountingStoreException>(() => AttributedCosts(scope, reader, ledger).AcceptAsync(reader.Envelope));
        }
        Assert.Equal(0, await run.Count("accounting.InventoryCostSources"));
        await using (var scope = run.Open())
        {
            var ledger = AttributionLedger(scope, physical); var manufacturing = ManufacturingCosts(scope, ledger);
            await manufacturing.AcceptAsync(physical.Envelope, physical.Source);
            await manufacturing.PrepareAsync(new(physical.Envelope.EventId, 1, period, new(2026, 11, 17), new(2026, 11, 17), Old, Counter,
                "SENTETIK delayed source"), Maker, Fence, Metadata(Maker));
            await manufacturing.ApproveAsync(physical.Envelope.EventId, 2, Checker, Fence, Metadata(Checker));
            var reader = new AttributedCostReader(run.Source.Facts, ledger, physical); var store = AttributedCosts(scope, reader, ledger);
            await store.AcceptAsync(reader.Envelope);
            Assert.Equal("AlreadyAccounted", Assert.Single(await store.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment)).Status);
        }
        Assert.Equal(1, await run.Count("accounting.Journals"));
        Assert.Equal(1, await run.Count("accounting.InvoiceCogsEconomicClaims"));
    }

    [SqlServerFact]
    public async Task Attribution_and_journal_are_rolled_back_together_when_authority_is_lost_after_the_actual_posting()
    {
        await using var run = await Run.Start(); var period = await run.Period(); var physical = new AttributionLineage(run.Source.Facts);
        await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical); var store = ManufacturingCosts(scope, ledger);
        await store.AcceptAsync(physical.Envelope, physical.Source);
        await store.PrepareAsync(new(physical.Envelope.EventId, 1, period, new(2026, 11, 17), new(2026, 11, 17), Old, Counter,
            "SENTETIK rollback"), Maker, Fence, Metadata(Maker));
        Task Revoked(CancellationToken _) => scope.Db.ManufacturingCostAttributionShares.Any()
            ? throw new InvalidOperationException("SENTETIK revoked after attribution save") : Task.CompletedTask;
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApproveAsync(physical.Envelope.EventId, 2, Checker, Revoked, Metadata(Checker)));
        Assert.Equal(0, await run.Count("accounting.Journals"));
        Assert.Equal(0, await run.Count("accounting.ManufacturingCostAttributions"));
        Assert.Equal(0, await run.Count("accounting.ManufacturingCostAttributionShares"));
    }

    [SqlServerFact]
    public async Task Attribution_requires_the_own_serializable_transaction_and_rejects_foreign_company_queries()
    {
        await using var run = await Run.Start(); var physical = new AttributionLineage(run.Source.Facts);
        await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical);
        var absent = await Assert.ThrowsAsync<AccountingStoreException>(() => ledger.ReadAsync(Company, physical.Target.ShipmentId,
            physical.Target.MovementId, 1, physical.Target.ProductId, 1m, Fence));
        Assert.Equal("manufacturing_attribution_transaction_required", absent.Code);
        await using var tx = await scope.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var foreign = await Assert.ThrowsAsync<AccountingStoreException>(() => ledger.ReadAsync(Guid.NewGuid(), physical.Target.ShipmentId,
            physical.Target.MovementId, 1, physical.Target.ProductId, 1m, Fence));
        Assert.Equal("manufacturing_attribution_query_invalid", foreign.Code);
    }

    [SqlServerFact]
    public async Task A_zero_rounded_reversal_share_cannot_witness_a_missing_original_recognition()
    {
        await using var run = await Run.Start(); var period = await run.Period();
        var physical = new AttributionLineage(run.Source.Facts, zeroShareRecognition: false);
        await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical);
        await PostAttributed(scope, physical, ledger, period, negative: true);
        await using var tx = await scope.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        Assert.Contains(await scope.Db.ManufacturingCostAttributionShares.AsNoTracking().Select(x => x.ViewJson).ToListAsync(),
            json => json.Contains("\"signedAmount\":\"0\"", StringComparison.Ordinal));
        var refusal = await Assert.ThrowsAsync<AccountingStoreException>(() => ledger.ReadAsync(Company, physical.Target.ShipmentId,
            physical.Target.MovementId, 1, physical.Target.ProductId, 1m, Fence));
        Assert.Equal("manufacturing_attribution_root_incomplete", refusal.Code);
    }

    [SqlServerFact]
    public async Task A_real_recognition_is_valid_even_when_its_exact_positive_share_rounds_to_zero()
    {
        await using var run = await Run.Start(); var period = await run.Period();
        var physical = new AttributionLineage(run.Source.Facts, zeroShareRecognition: true);
        await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical);
        await PostAttributed(scope, physical, ledger, period);
        await using var tx = await scope.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actual = await ledger.ReadAsync(Company, physical.Target.ShipmentId, physical.Target.MovementId, 1, physical.Target.ProductId, 1m, Fence);
        Assert.Equal("0", Assert.Single(actual!.Shares).SignedAmount);
        Assert.Equal(new("3", "500"), Assert.Single(actual.Shares).ExactSignedAmount);
    }

    [SqlServerFact]
    public async Task A_legacy_positive_posting_missing_attribution_is_not_hidden_by_a_later_reversal_and_new_recognition()
    {
        await using var run = await Run.Start(); var period = await run.Period();
        var basis = new AttributionLineage(run.Source.Facts);
        var legacy = basis.Copy(basis.Source with { ConsumedLineageReference = null });
        await using var scope = run.Open();
        await PostAttributed(scope, legacy, AttributionLedger(scope, legacy), period);
        Assert.Equal(0, await run.Count("accounting.ManufacturingCostAttributions"));
        var reverse = basis.Copy(basis.Source with { GenerationVersion = "2", State = 4, CostDifferenceEventId = Guid.NewGuid(),
            ConsumedLineageReference = basis.Source.ConsumedLineageReference! with { GenerationVersion = "2" },
            Outputs = [basis.Source.Outputs[0] with { ConsumedTlDifference = -100.01m, OriginalTlAmount = -100.01m }] });
        await PostAttributed(scope, reverse, AttributionLedger(scope, reverse), period, negative: true);
        var generation = Guid.NewGuid();
        var next = basis.Copy(basis.Source with { GenerationId = generation, CostDifferenceEventId = Guid.NewGuid(),
            ConsumedLineageReference = basis.Source.ConsumedLineageReference! with { GenerationId = generation },
            Outputs = [basis.Source.Outputs[0] with { ConsumedTlDifference = 120m, OriginalTlAmount = 120m, TlUnitCostPerKg = 40m }] });
        var ledger = AttributionLedger(scope, next); await PostAttributed(scope, next, ledger, period);
        await using var tx = await scope.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var refusal = await Assert.ThrowsAsync<AccountingStoreException>(() => ledger.ReadAsync(Company, next.Target.ShipmentId,
            next.Target.MovementId, 1, next.Target.ProductId, 1m, Fence));
        Assert.Equal("manufacturing_attribution_history_incomplete", refusal.Code);
        Assert.Equal(3, await run.Count("accounting.Journals"));
    }

    private static async Task PostAttributed(Scope scope, AttributionLineage physical, ManufacturingPostedCostAttributionStoreV1 ledger,
        Guid period, bool negative = false)
    {
        var store = ManufacturingCosts(scope, ledger);
        await store.AcceptAsync(physical.Envelope, physical.Source);
        await store.PrepareAsync(new(physical.Envelope.EventId, 1, period, new(2026, 11, 17), new(2026, 11, 17),
            negative ? Counter : Old, negative ? Old : Counter, "SENTETIK frozen source"), Maker, Fence, Metadata(Maker));
        await store.ApproveAsync(physical.Envelope.EventId, 2, Checker, Fence, Metadata(Checker));
    }

    private static ManufacturingPostedCostAttributionStoreV1 AttributionLedger(Scope scope, AttributionLineage physical) => new(scope.Db,
        scope.CompanyContext, physical, new(new AccountingSqlServerRelationalAcceptanceTests.SyntheticMoneyProfiles(Company)), TimeProvider.System);
    private static ManufacturingCostDifferenceStoreV1 ManufacturingCosts(Scope scope, ManufacturingPostedCostAttributionStoreV1 ledger) => new(scope.Db,
        scope.Posting, scope.CompanyContext, new Plan(), new CostCurrencies(), scope.Audit, TimeProvider.System, ledger);
    private static AccountingInventoryCostStoreV1 AttributedCosts(Scope scope, AttributedCostReader reader, ManufacturingPostedCostAttributionStoreV1 ledger) => new(scope.Db,
        scope.Posting, reader, new CogsMapping(), new Plan(), new CostCurrencies(),
        new(new AccountingSqlServerRelationalAcceptanceTests.SyntheticMoneyProfiles(Company)), scope.CompanyContext, scope.Audit, scope.AuditWriter, TimeProvider.System, ledger);
    private static InvoiceLineCogsCorrectionStoreV1 AttributedCorrections(Scope scope, ManufacturingPostedCostAttributionStoreV1 ledger) => new(scope.Db,
        scope.CompanyContext, new Plan(), scope.Audit, scope.AuditWriter, TimeProvider.System, ledger);

    // Only physical owner answers and commercial identity are synthetic; all money/source/journal/claim writes use real SQL owners.
    private sealed class AttributionLineage : IManufacturingAttributionLineageReaderV1
    {
        public ManufacturingAttributionShipmentV1 Target { get; }
        public ManufacturingCostDifferenceEventV1 Source { get; private set; }
        public WorkInboxEventV1 Envelope { get; private set; }
        public ManufacturingAttributionLineageV1 Value { get; private set; }
        private AttributionLineage(AttributionLineage basis, ManufacturingCostDifferenceEventV1 source)
        {
            Target = basis.Target; Source = source;
            Envelope = basis.Envelope with { EventId = source.CostDifferenceEventId!.Value, PayloadJson = JsonSerializer.Serialize(source, Json) };
            Value = basis.Value with { SourceEventId = source.CostDifferenceEventId.Value, GenerationId = source.GenerationId,
                GenerationVersion = source.GenerationVersion };
        }
        public AttributionLineage Copy(ManufacturingCostDifferenceEventV1 source) => new(this, source);
        public AttributionLineage(FinalizedInvoiceLineSourceV1 facts, bool? zeroShareRecognition = null)
        {
            var generation = Guid.NewGuid(); var output = Guid.NewGuid(); var receipt = Guid.NewGuid(); var label = Guid.NewGuid(); var id = Guid.NewGuid();
            Target = new(facts.ShipmentId, Guid.NewGuid(), 1, Guid.NewGuid(), facts.ProductId, label, 1m, new("1", "1"), new("1", "1"), At, null, false, "{\"syntheticPhysicalOwner\":true}");
            var other = Target with { ShipmentId = Guid.NewGuid(), MovementId = Guid.NewGuid(), PhysicalQuantityKg = 2m,
                NetRootQuantityKg = new("2", "1"), RecordedAtUtc = At.AddSeconds(1) };
            Source = new(Company, Guid.NewGuid(), generation, "1", "1", 3, "SENTETIK-input", [new(output, receipt, label, 3m, 0m, 3m,
                33.336666666666666666666666667m, 0m, 100.01m, null, [], 100.01m)], id, false, "SENTETIK-attribution",
                new("inventory.manufacturing-consumed-cost-lineage.v1", generation, "1", new string('a', 64)));
            Envelope = new(ManufacturingCostDifferenceInboxV1.SourceName, id, Company, ManufacturingCostDifferenceInboxV1.EventType, 1,
                JsonSerializer.Serialize(Source, Json), At, "SENTETIK-attribution", null);
            Value = new(Company, id, generation, "1", new string('a', 64), [new(output, receipt, label, 3m, new("3", "1"), new string('b', 64), [Target, other])]);
            if (zeroShareRecognition.HasValue)
            {
                var signed = zeroShareRecognition.Value ? 0.60m : -0.60m;
                Source = Source with { State = zeroShareRecognition.Value ? 3 : 4, Outputs = [new(output, receipt, label, 100m, 0m, 100m,
                    signed / 100m, 0m, signed, null, [], signed)] };
                var shipments = Enumerable.Range(0, 100).Select(i => i == 0 ? Target : Target with {
                    ShipmentId = Guid.NewGuid(), MovementId = Guid.NewGuid(), RecordedAtUtc = At.AddSeconds(i) }).ToArray();
                Value = Value with { Outputs = [new(output, receipt, label, 100m, new("100", "1"), new string('b', 64), shipments)] };
                Envelope = Envelope with { PayloadJson = JsonSerializer.Serialize(Source, Json) };
            }
        }
        public async Task<ManufacturingAttributionLineageV1> ReadAsync(ManufacturingCostDifferenceEventV1 source, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { await fence(token); Assert.Equal(JsonSerializer.Serialize(Source, Json), JsonSerializer.Serialize(source, Json)); return Value; }
    }
    private sealed class AttributedCostReader(FinalizedInvoiceLineSourceV1 facts, ManufacturingPostedCostAttributionStoreV1 ledger, AttributionLineage physical)
        : IAccountingInventoryCostSourceReaderV1
    {
        public WorkInboxEventV1 Envelope { get; } = new(AccountingInventoryCostInboxV1.SalesSourceName, Guid.NewGuid(), Company,
            AccountingInventoryCostInboxV1.SalesEventType, 1, "{\"synthetic\":true}", At, "SENTETIK-attributed-invoice", null);
        public async Task<AccountingInventoryCostSourceV1> ReadAsync(WorkInboxEventV1 envelope, CancellationToken token = default)
        {
            var target = physical.Target;
            var actual = await ledger.ReadAsync(Company, target.ShipmentId, target.MovementId, 1, target.ProductId, 1m, Fence, token)
                ?? throw new AccountingStoreException("manufacturing_attribution_missing", "SENTETIK pending actual posting");
            const string proof = "{\"syntheticOwnerProof\":true}";
            return new(Company, envelope.EventId, AccountingInventoryCostKindV1.SalesInvoicedShipment, facts.InvoiceId, facts.FinalizedVersion,
                facts.FinalizationAdmissionId, facts.InvoiceDate, facts.OfficialInvoiceNumber, Maker,
                AccountingInventoryCostSourceValidationV1.Sha256(envelope.PayloadJson),
                [new(AccountingInventoryCostTraceV1.SalesComponent(facts.InvoiceLineId), facts.ProductId, facts.InvoiceLineId, facts.InvoiceLineNo,
                    null, new("1667", "50"), proof, AccountingInventoryCostSourceValidationV1.Sha256(proof),
                    [new(target.ShipmentId, target.MovementId, 1, target.ProductId, 1m)], actual.Shares)]);
        }
    }
}
