using GoymenERP.Api.LogisticsRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Transfers;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    private const string TransferDocumentMigration = "20261015163000_AddTransferOutgoingDocuments";
    private const string PreviousTransferDocumentMigration = "20261003230000_AddCustomerReturns";

    [SqlServerFact]
    public async Task Transfer_outgoing_document_is_idempotent_company_scoped_and_effective_only_for_the_exact_typed_proof()
    {
        await using var run = await Run.StartAsync();
        var documentId = Guid.NewGuid();
        var transferId = Guid.NewGuid();
        var lines = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var beforeInventoryMovements = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements");

        await using (var owner = TransferStore(run, CompanyA))
        {
            var draft = TransferOutgoingDocument.Create(CompanyA, documentId, transferId, lines,
                "EDOC-REF-1", run.Actor, T0);
            var first = await owner.Store.StoreDraftAsync(run.Envelope("transfer-doc-create"), Hash("transfer-doc-create"),
                draft, _ => Task.CompletedTask);
            var replay = await owner.Store.StoreDraftAsync(run.Envelope("transfer-doc-create", correlation: "retry"),
                Hash("transfer-doc-create"), draft, _ => Task.CompletedTask);
            Assert.Equal((LogisticsWriteOutcome.Recorded, LogisticsWriteOutcome.Replayed, run.Correlation),
                (first.Outcome, replay.Outcome, replay.OriginalCorrelationId));

            var beforeDraftProofReads = await ProofReadEffectsAsync(run, documentId);
            await AssertRefusedProofAsync(owner.Adapter, InventoryTransferDocumentProofOutcomeV1.Draft,
                new(CompanyA, transferId, documentId, 1, lines));
            await AssertRefusedProofAsync(owner.Adapter, InventoryTransferDocumentProofOutcomeV1.BindingMismatch,
                new(CompanyA, Guid.NewGuid(), documentId, 1, lines));
            await AssertRefusedProofAsync(owner.Adapter, InventoryTransferDocumentProofOutcomeV1.BindingMismatch,
                new(CompanyA, transferId, documentId, 1, lines.Reverse()));
            await AssertRefusedProofAsync(owner.Adapter, InventoryTransferDocumentProofOutcomeV1.BindingMismatch,
                new(CompanyA, transferId, documentId, 1, lines.Take(1)));
            await AssertRefusedProofAsync(owner.Adapter, InventoryTransferDocumentProofOutcomeV1.BindingMismatch,
                new(CompanyB, transferId, documentId, 1, lines));
            Assert.Equal(beforeDraftProofReads, await ProofReadEffectsAsync(run, documentId));

            var effective = await owner.Store.MutateAsync(run.Envelope("transfer-doc-finalize"), Hash("transfer-doc-finalize"),
                LogisticsTransferOutgoingDocumentCommandsV1.Finalize, documentId,
                (item, now) => item.Finalize(1, run.Actor, now), _ => Task.CompletedTask);
            Assert.Equal((TransferOutgoingDocumentStatus.Effective, 2),
                (effective.Document.Status, effective.Document.Version));

            var beforeEffectiveProofReads = await ProofReadEffectsAsync(run, documentId);
            var exact = await owner.Adapter.ReadAsync(new InventoryTransferOutgoingDocumentProofQueryV1(
                CompanyA, transferId, documentId, 2, lines));
            Assert.Equal(InventoryTransferDocumentProofOutcomeV1.CurrentEffective, exact.Outcome);
            Assert.NotNull(exact.EffectiveBinding);

            var stale = await owner.Adapter.ReadAsync(new InventoryTransferOutgoingDocumentProofQueryV1(
                CompanyA, transferId, documentId, 1, lines));
            Assert.Equal(InventoryTransferDocumentProofOutcomeV1.Stale, stale.Outcome);
            Assert.Null(stale.EffectiveBinding);

            var reordered = await owner.Adapter.ReadAsync(new InventoryTransferOutgoingDocumentProofQueryV1(
                CompanyA, transferId, documentId, 2, lines.Reverse()));
            Assert.Equal(InventoryTransferDocumentProofOutcomeV1.BindingMismatch, reordered.Outcome);
            Assert.Null(reordered.EffectiveBinding);
            Assert.Equal(beforeEffectiveProofReads, await ProofReadEffectsAsync(run, documentId));
        }

        await using (var foreign = TransferStore(run, CompanyB))
        {
            var hidden = await foreign.Adapter.ReadAsync(new InventoryTransferOutgoingDocumentProofQueryV1(
                CompanyB, transferId, documentId, 2, lines));
            Assert.Equal(InventoryTransferDocumentProofOutcomeV1.Missing, hidden.Outcome);
        }

        Assert.Equal(beforeInventoryMovements, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM logistics.TransferOutgoingDocuments WHERE DocumentId = @id", ("@id", documentId)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM logistics.TransferOutgoingDocumentLines WHERE DocumentId = @id", ("@id", documentId)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM logistics.TransferOutgoingDocumentAttempts WHERE DocumentId = @id", ("@id", documentId)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id", ("@id", documentId.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Transfer_outgoing_document_version_race_has_one_winner_and_cancellation_revokes_the_proof()
    {
        await using var run = await Run.StartAsync();
        var documentId = Guid.NewGuid();
        var transferId = Guid.NewGuid();
        var lines = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await using (var creator = TransferStore(run, CompanyA))
        {
            await creator.Store.StoreDraftAsync(run.Envelope("race-create"), Hash("race-create"),
                TransferOutgoingDocument.Create(CompanyA, documentId, transferId, lines, null, run.Actor, T0),
                _ => Task.CompletedTask);
        }

        using var barrier = new Barrier(2);
        async Task<string> FinalizeAsync(string key)
        {
            await using var owner = TransferStore(run, CompanyA);
            try
            {
                await owner.Store.MutateAsync(run.Envelope(key), Hash(key),
                    LogisticsTransferOutgoingDocumentCommandsV1.Finalize, documentId,
                    (item, now) => item.Finalize(1, run.Actor, now),
                    _ => Task.Run(() => barrier.SignalAndWait(TimeSpan.FromSeconds(30))));
                return "winner";
            }
            catch (Exception exception) when (exception is LogisticsStoreException or TransferOutgoingDocumentRuleException)
            {
                return "loser";
            }
        }

        var results = await Task.WhenAll(FinalizeAsync("race-a"), FinalizeAsync("race-b"));
        Assert.Equal(1, results.Count(value => value == "winner"));
        Assert.Equal(1, results.Count(value => value == "loser"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT Version FROM logistics.TransferOutgoingDocuments WHERE DocumentId = @id", ("@id", documentId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM logistics.TransferOutgoingDocumentAttempts WHERE DocumentId = @id "
            + "AND CommandName = @command", ("@id", documentId),
            ("@command", LogisticsTransferOutgoingDocumentCommandsV1.Finalize)));

        await using (var owner = TransferStore(run, CompanyA))
        {
            await owner.Store.MutateAsync(run.Envelope("cancel-effective"), Hash("cancel-effective"),
                LogisticsTransferOutgoingDocumentCommandsV1.Cancel, documentId,
                (item, now) => item.Cancel(2, run.Actor, "wrong destination", now), _ => Task.CompletedTask);
            var beforeProofReads = await ProofReadEffectsAsync(run, documentId);
            await AssertRefusedProofAsync(owner.Adapter, InventoryTransferDocumentProofOutcomeV1.Cancelled,
                new(CompanyA, transferId, documentId, 3, lines));
            await AssertRefusedProofAsync(owner.Adapter, InventoryTransferDocumentProofOutcomeV1.BindingMismatch,
                new(CompanyA, Guid.NewGuid(), documentId, 3, lines));
            await AssertRefusedProofAsync(owner.Adapter, InventoryTransferDocumentProofOutcomeV1.BindingMismatch,
                new(CompanyA, transferId, documentId, 3, lines.Reverse()));
            await AssertRefusedProofAsync(owner.Adapter, InventoryTransferDocumentProofOutcomeV1.BindingMismatch,
                new(CompanyA, transferId, documentId, 3, lines.Take(1)));
            await AssertRefusedProofAsync(owner.Adapter, InventoryTransferDocumentProofOutcomeV1.BindingMismatch,
                new(CompanyB, transferId, documentId, 3, lines));
            Assert.Equal(beforeProofReads, await ProofReadEffectsAsync(run, documentId));
        }
    }

    [SqlServerFact]
    public async Task Transfer_outgoing_document_schema_enforces_one_document_per_transfer_and_refuses_lossy_rollback()
    {
        await using var run = await Run.StartAsync();
        var documentId = Guid.NewGuid();
        var transferId = Guid.NewGuid();
        await using (var owner = TransferStore(run, CompanyA))
        {
            await owner.Store.StoreDraftAsync(run.Envelope("schema-create"), Hash("schema-create"),
                TransferOutgoingDocument.Create(CompanyA, documentId, transferId, [Guid.NewGuid()], null,
                    run.Actor, T0), _ => Task.CompletedTask);
            var duplicate = await Assert.ThrowsAsync<LogisticsStoreException>(() => owner.Store.StoreDraftAsync(
                run.Envelope("schema-duplicate"), Hash("schema-duplicate"),
                TransferOutgoingDocument.Create(CompanyA, Guid.NewGuid(), transferId, [Guid.NewGuid()], null,
                    run.Actor, T0), _ => Task.CompletedTask));
            Assert.Equal(LogisticsTransferOutgoingDocumentCodesV1.TransferAlreadyBound, duplicate.Code);
        }

        var company = new CompanyContext();
        company.Set(CompanyA);
        await using var context = new LogisticsDbContext(TransferOptions(run.Factory), company);
        var migrator = context.GetService<IMigrator>();
        var refusal = await Assert.ThrowsAsync<SqlException>(() =>
            migrator.MigrateAsync(PreviousTransferDocumentMigration));
        Assert.Equal(51075, refusal.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM logistics.TransferOutgoingDocuments WHERE DocumentId = @id", ("@id", documentId)));

        await run.ExecuteAsync("DELETE FROM logistics.TransferOutgoingDocumentAttempts; "
            + "DELETE FROM logistics.TransferOutgoingDocumentLines; DELETE FROM logistics.TransferOutgoingDocuments;");
        await migrator.MigrateAsync(PreviousTransferDocumentMigration);
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'logistics') AND name = N'TransferOutgoingDocuments'"));
        await migrator.MigrateAsync(TransferDocumentMigration);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'logistics') AND name = N'TransferOutgoingDocuments'"));
    }

    private static TransferDocumentScope TransferStore(Run run, Guid companyId)
    {
        var company = new CompanyContext();
        company.Set(companyId);
        var collector = new AuditEventCollector();
        var options = new DbContextOptionsBuilder<LogisticsDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, company))
            .Options;
        var context = new LogisticsDbContext(options, company);
        var store = new LogisticsTransferOutgoingDocumentStore(context, company, collector, TimeProvider.System);
        return new(context, store, new InventoryTransferOutgoingDocumentProofAdapter(store));
    }

    private static DbContextOptions<LogisticsDbContext> TransferOptions(SqlServerRelationalApiFactory factory) =>
        new DbContextOptionsBuilder<LogisticsDbContext>()
            .UseSqlServer(factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
            .Options;

    private static async Task AssertRefusedProofAsync(InventoryTransferOutgoingDocumentProofAdapter adapter,
        InventoryTransferDocumentProofOutcomeV1 expected,
        InventoryTransferOutgoingDocumentProofQueryV1 query)
    {
        var proof = await adapter.ReadAsync(query);
        Assert.Equal(expected, proof.Outcome);
        Assert.Null(proof.EffectiveBinding);
    }

    private static async Task<(int Attempts, int Audits, int Movements, int Outbox)> ProofReadEffectsAsync(
        Run run,
        Guid documentId) =>
        (await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM logistics.TransferOutgoingDocumentAttempts WHERE DocumentId = @id",
                ("@id", documentId)),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id",
                ("@id", documentId.ToString("D"))),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages"));

    private sealed class TransferDocumentScope(
        LogisticsDbContext context,
        LogisticsTransferOutgoingDocumentStore store,
        InventoryTransferOutgoingDocumentProofAdapter adapter) : IAsyncDisposable
    {
        public LogisticsTransferOutgoingDocumentStore Store { get; } = store;
        public InventoryTransferOutgoingDocumentProofAdapter Adapter { get; } = adapter;
        public ValueTask DisposeAsync() => context.DisposeAsync();
    }
}
