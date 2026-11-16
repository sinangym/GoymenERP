using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Returns;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Receipt_adjustment_and_Audit_owner_share_host_rollback_and_immutable_replay_survives_later_cancellation()
    {
        await using var run = await Run.StartAsync();
        var seed = await SeedApprovedAcceptanceOrderAsync(run);
        var acceptanceId = Guid.NewGuid();
        await StageAndCommitAsync(run, CompanyA, AcceptanceRequest(seed, acceptanceId, "4"), Envelope("adjustment-source", "source-correlation"));
        var key = "owner-adjustment-rollback";
        var envelope = Envelope(key, "adjustment-correlation");
        var hash = AcceptanceHash(key);
        var request = new PurchaseReceiptAdjustmentStageV1(CompanyA, Guid.NewGuid(), seed.OrderId, acceptanceId, "4", "2",
            false, false, "Incorrect receipt", AcceptanceTime.AddMinutes(10), PurchaseReceiptAdjustmentCodesV1.CommandName, hash);

        await using (var scope = run.Factory.Services.CreateAsyncScope())
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using (var owner = await PurchaseReceiptAdjustmentParticipant.EnlistAsync(connection, transaction, CompanyA,
                scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>()))
            {
                var result = await owner.StageAsync(request, envelope);
                Assert.Equal(("5", "2", "8"), (result.OrderVersion, result.NetOpenedQuantity, result.AllocatableQuantity));
                var supply = await owner.ReadSupplyAsync(seed.OrderId, seed.LineId);
                Assert.Equal(("5", "8"), (supply!.OrderVersion, supply.AllocatableQuantity));
            }
            Assert.NotNull(transaction.Connection);
            await transaction.RollbackAsync();
        }
        await using (var stored = run.Store(CompanyA))
        {
            var original = (await stored.Store.FindOrderAsync(seed.OrderId))!.Order;
            Assert.Equal(4, original.Version);
            Assert.Empty(original.ReceiptAdjustments);
            Assert.Equal(4m, Assert.Single(original.Lines).AcceptedQuantity.Value);
        }
        Assert.Equal(0, await ReceiptAdjustmentAuditCountAsync(run, seed.OrderId));

        await using (var scope = run.Factory.Services.CreateAsyncScope())
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using var owner = await PurchaseReceiptAdjustmentParticipant.EnlistAsync(connection, transaction, CompanyA,
                scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
            Assert.False((await owner.StageAsync(request, envelope)).IsReplay);
            await transaction.CommitAsync();
        }
        await using (var stored = run.Store(CompanyA))
        {
            var changed = (await stored.Store.FindOrderAsync(seed.OrderId))!.Order;
            Assert.Equal(5, changed.Version);
            Assert.Equal((4m, 2m), (Assert.Single(changed.Acceptances).Quantity.Value, Assert.Single(changed.Lines).AcceptedQuantity.Value));
            await stored.Store.MutateOrderAsync(Envelope("after-adjustment-cancel", "cancel-correlation"), AcceptanceHash("after-adjustment-cancel"),
                PurchasingCommands.OrderCancelV1, seed.OrderId,
                (order, now) => order.Cancel(order.Version, "canceller", "Later cancellation", now),
                new PurchasingMutationAudit("purchasing.purchase-order.cancelled.v1", "Cancel", "Later cancellation"));
        }
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using var owner = await PurchaseReceiptAdjustmentParticipant.EnlistAsync(connection, transaction, CompanyA,
                scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
            var replay = await owner.FindReplayAsync(request.CommandName, hash, envelope);
            Assert.Equal((true, "5", "8", "adjustment-correlation"),
                (replay!.IsReplay, replay.OrderVersion, replay.AllocatableQuantity, replay.OriginalCorrelationId));
            var current = await owner.ReadSupplyAsync(seed.OrderId, seed.LineId);
            Assert.Equal((false, "6", "0"), (current!.IsAllocatable, current.OrderVersion, current.AllocatableQuantity));
            var other = await Assert.ThrowsAsync<PurchasingServiceException>(() => owner.FindReplayAsync(request.CommandName, hash,
                envelope with { ActorId = "another-actor" }));
            Assert.Equal(409, other.Status);
            await transaction.RollbackAsync();
        }
        Assert.Equal(1, await ReceiptAdjustmentAuditCountAsync(run, seed.OrderId));
    }

    [SqlServerFact]
    public async Task Receipt_owner_replays_the_shared_terminal_attempt_before_any_missing_target_or_version_read()
    {
        await using var run = await Run.StartAsync();
        var envelope = Envelope("shared-terminal", "original-refusal");
        var hash = AcceptanceHash("shared-terminal");
        // A known owner command permits the generic refusal recorder; this fact exercises the shared immutable journal directly.
        var command = PurchasingCommands.OrderCancelV1;
        await using (var stored = run.Store(CompanyA))
            await stored.Store.RecordRefusalAsync(envelope, hash, command, new(422, "original-terminal", []));
        await using var scope = run.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var owner = await PurchaseReceiptAdjustmentParticipant.EnlistAsync(connection, transaction, CompanyA,
            scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => owner.FindReplayAsync(command, hash, envelope));
        Assert.Equal((422, "original-terminal", true, "original-refusal"),
            (refusal.Status, refusal.Code, refusal.IsReplay, refusal.OriginalCorrelationId));
        Assert.Null(await owner.ReadTargetAsync(Guid.NewGuid(), Guid.NewGuid()));
        await transaction.RollbackAsync();
    }

    [SqlServerFact]
    public async Task Receipt_adjustment_Down_preserves_recorded_cancellation_history_with_error_51949()
    {
        await using var run = await Run.StartAsync();
        var seed = await SeedApprovedAcceptanceOrderAsync(run);
        await using (var stored = run.Store(CompanyA))
            await stored.Store.MutateOrderAsync(Envelope("guard-cancel", "guard-correlation"), AcceptanceHash("guard-cancel"),
                PurchasingCommands.OrderCancelV1, seed.OrderId,
                (order, now) => order.Cancel(order.Version, "canceller", "History must remain", now),
                new PurchasingMutationAudit("purchasing.purchase-order.cancelled.v1", "Cancel", "History must remain"));
        var company = new CompanyContext(); company.Set(CompanyA);
        await using var database = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema)).Options, company);
        var migrations = database.Database.GetMigrations().ToArray();
        var index = Array.IndexOf(migrations, "20261116213347_AddPurchaseReceiptAdjustments");
        Assert.True(index > 0);
        var refusal = await Assert.ThrowsAsync<SqlException>(() => database.GetService<IMigrator>().MigrateAsync(migrations[index - 1]));
        Assert.Equal(51949, refusal.Number);
        Assert.Contains("20261116213347_AddPurchaseReceiptAdjustments", await database.Database.GetAppliedMigrationsAsync());
        await using var storedAfter = run.Store(CompanyA);
        Assert.Single((await storedAfter.Store.FindOrderAsync(seed.OrderId))!.Order.CancellationHistory);
    }

    [SqlServerFact]
    public async Task A_definitive_supplier_return_does_not_reappear_in_open_or_allocatable_purchase_choices()
    {
        await using var run = await Run.StartAsync();
        var seed = await SeedApprovedAcceptanceOrderAsync(run);
        var acceptance = Guid.NewGuid();
        await StageAndCommitAsync(run, CompanyA, AcceptanceRequest(seed, acceptance, "10"), Envelope("fully-received", "receipt"));
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using var owner = await PurchaseReceiptAdjustmentParticipant.EnlistAsync(connection, transaction, CompanyA,
                scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
            var result = await owner.StageAsync(new(CompanyA, Guid.NewGuid(), seed.OrderId, acceptance, "4", "2",
                false, true, "Definitive return without replacement", AcceptanceTime.AddMinutes(10),
                PurchaseReceiptAdjustmentCodesV1.CommandName, AcceptanceHash("definitive-return")), Envelope("definitive-return", "return"));
            Assert.Equal(("0", "0", false), (result.NetOpenedQuantity, result.AllocatableQuantity, result.Revived));
            await transaction.CommitAsync();
        }
        await using var readScope = run.Factory.Services.CreateAsyncScope();
        readScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        var supply = await readScope.ServiceProvider.GetRequiredService<IPurchaseFutureSupplyReadPortV1>()
            .GetAllocatableLineAsync(seed.OrderId, seed.LineId);
        Assert.Equal(("8", "2", "0"), (supply.Line!.AcceptedText, supply.Line.DefinitiveReturnedText, supply.Line.AllocatableText));
        var choices = await readScope.ServiceProvider.GetRequiredService<IPurchaseOpenLineReadPortV1>()
            .ListOpenApprovedLinesAsync(100, null);
        Assert.DoesNotContain(choices.Items, line => line.OrderId == seed.OrderId && line.LineId == seed.LineId);
    }

    [SqlServerFact]
    public async Task Supplier_return_creation_replay_preserves_its_original_pending_snapshot_after_a_later_rejection()
    {
        await using var run = await Run.StartAsync();
        var seed = await SeedApprovedAcceptanceOrderAsync(run);
        var acceptance = Guid.NewGuid();
        await StageAndCommitAsync(run, CompanyA, AcceptanceRequest(seed, acceptance, "4"), Envelope("return-snapshot-source", "return-source"));
        var returnId = Guid.NewGuid();
        var originalEnvelope = Envelope("return-original-key", "return-original-correlation");
        var originalHash = AcceptanceHash("return-original-key");
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            var owner = scope.ServiceProvider.GetRequiredService<IPurchaseSupplierReturnStore>();
            var draft = new PurchaseSupplierReturnDraft(returnId, Guid.NewGuid(), seed.OrderId, seed.LineId, acceptance, Guid.NewGuid(),
                PurchaseSupplierReturnStatus.PendingCostException, "Free", 1m, PurchaseSupplierReturnCostBasis.ManualCostWithException,
                2m, false, "Manual cost awaits approval", AcceptanceTime, 4, 1m);
            await owner.SaveNewAsync(draft, originalEnvelope, originalHash, PurchaseSupplierReturnCommandsV1.CreateV1);
        }
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            var owner = scope.ServiceProvider.GetRequiredService<IPurchaseSupplierReturnStore>();
            var recorded = (await owner.FindAsync(seed.OrderId, returnId))!;
            var decision = new PurchaseSupplierReturn(returnId, recorded.Return.Status, recorded.Version, recorded.RequestedBy);
            decision.Reject(1, "different-checker", "Cost rejected");
            await owner.SaveDecisionAsync(seed.OrderId, decision, 1,
                Envelope("return-reject-key", "return-reject-correlation") with { ActorId = "different-checker" },
                AcceptanceHash("return-reject-key"), PurchaseSupplierReturnCommandsV1.RejectCostExceptionV1);
        }
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            var owner = scope.ServiceProvider.GetRequiredService<IPurchaseSupplierReturnStore>();
            Assert.Equal(PurchaseSupplierReturnStatus.Rejected, (await owner.FindAsync(seed.OrderId, returnId))!.Return.Status);
            var replay = (await owner.FindReplayAsync(originalEnvelope, originalHash, PurchaseSupplierReturnCommandsV1.CreateV1))!;
            Assert.NotNull(replay.Original);
            Assert.Equal((1L, PurchaseSupplierReturnStatus.PendingCostException, "return-original-correlation"),
                (replay.Original.Version, replay.Original.Return.Status, replay.OriginalCorrelationId));
        }
        var company = new CompanyContext(); company.Set(CompanyA);
        await using var database = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema)).Options, company);
        var guard = await Assert.ThrowsAsync<SqlException>(() => database.GetService<IMigrator>()
            .MigrateAsync("20261116213347_AddPurchaseReceiptAdjustments"));
        Assert.Equal(51953, guard.Number);
        Assert.Contains("20261116215806_AddPurchaseSupplierReturnResponseSnapshots", await database.Database.GetAppliedMigrationsAsync());
    }

    private static async Task<int> ReceiptAdjustmentAuditCountAsync(Run run, Guid orderId)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CompanyId] = @company AND [EntityId] = @id AND [EventType] = 'purchasing.purchase-order.receipt-adjusted.v1'";
        command.Parameters.AddWithValue("@company", CompanyA);
        command.Parameters.AddWithValue("@id", orderId.ToString("D"));
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
