using System.Data.Common;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Outbox;
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
    public async Task Net_zero_receipt_batch_commits_one_version_rebuilds_once_and_replays_before_later_revival()
    {
        await using var run = await Run.StartAsync();
        var seed = await SeedApprovedAcceptanceOrderAsync(run);
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        await StageAndCommitAsync(run, CompanyA, AcceptanceRequest(seed, first, "4"), Envelope("batch-source-one", "source-one"));
        await StageAndCommitAsync(run, CompanyA, AcceptanceRequest(seed, second, "2") with { ExpectedPurchaseOrderVersion = "4" },
            Envelope("batch-source-two", "source-two"));
        await using (var stored = run.Store(CompanyA))
            await stored.Store.MutateOrderAsync(run.Envelope("batch-cancel", "cancel"), AcceptanceHash("batch-cancel"),
                PurchasingCommands.OrderCancelV1, seed.OrderId,
                (order, now) => order.Cancel(order.Version, "canceller", "Cancel open remainder", now),
                new PurchasingMutationAudit("purchasing.purchase-order.cancelled.v1", "Cancel", "Cancel open remainder"));
        var envelope = Envelope("one-net-batch", "original-batch-correlation");
        var hash = AcceptanceHash("one-net-batch");
        var request = new PurchaseReceiptAdjustmentBatchStageV1(CompanyA, Guid.NewGuid(), seed.OrderId, "6", "Net zero across two receipts",
            AcceptanceTime.AddMinutes(10), "purchasing.goods-receipt.reverse-batch.v1", hash,
            [new(Guid.NewGuid(), first, "1", false, false, Guid.NewGuid(), "2"),
             new(Guid.NewGuid(), second, "2", false, false, Guid.NewGuid(), "1")]);
        // First attempt deliberately rolls back the real owner, immutable result, outbox and Audit together.
        foreach (var commit in new[] { false, true })
        {
            await using var scope = run.Factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using var owner = await PurchaseReceiptAdjustmentParticipant.EnlistBatchAsync(connection, transaction, CompanyA,
                scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
            Assert.Null(await owner.FindBatchReplayAsync(request.CommandName, hash, envelope));
            var result = await owner.StageBatchAsync(request, envelope);
            Assert.False(result.IsReplay);
            Assert.Equal("7", result.OrderVersion);
            Assert.All(result.Lines, line => Assert.Equal(("7", "0", false), (line.OrderVersion, line.NetOpenedQuantity, line.Revived)));
            var supply = Assert.Single(result.Supplies);
            Assert.Equal((seed.LineId, "0", "0", false), (supply.GeneralLineId, supply.NetOpenedQuantity, supply.AllocatableQuantity, supply.Revived));
            if (commit) await transaction.CommitAsync(); else await transaction.RollbackAsync();
        }
        await using (var stored = run.Store(CompanyA))
        {
            var rebuilt = (await stored.Store.FindOrderAsync(seed.OrderId))!.Order;
            Assert.Equal((7L, PurchaseOrderStatus.Cancelled, 6m, 4m, 0m),
                (rebuilt.Version, rebuilt.Status, rebuilt.Lines[0].AcceptedQuantity.Value,
                    rebuilt.Lines[0].CancelledQuantity.Value, rebuilt.Lines[0].RemainingQuantity.Value));
            Assert.Equal(2, rebuilt.ReceiptAdjustments.Count);
            Assert.Equal(4, rebuilt.Acceptances.Count);
        }
        Assert.Empty(await ReadPurchasingOutboxAsync(run, CompanyA));
        var historicalSalesTarget = new PurchaseRevivalSalesTargetV1(Guid.NewGuid(), Guid.NewGuid());
        // Missing, foreign, stale and duplicate link snapshots cannot be treated as an empty recipient list.
        foreach (var snapshots in new IReadOnlyList<PurchaseRevivalNotificationSnapshotV1>?[] {
                     null, [new(CompanyB, seed.OrderId, seed.LineId, "7", [])],
                     [new(CompanyA, seed.OrderId, seed.LineId, "6", [])],
                     [new(CompanyA, seed.OrderId, seed.LineId, "7", [historicalSalesTarget, historicalSalesTarget])] })
        {
            await using var scope = run.Factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using var owner = await PurchaseReceiptAdjustmentParticipant.EnlistAsync(connection, transaction, CompanyA,
                scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
            var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => owner.StageAsync(new(CompanyA,
                Guid.NewGuid(), seed.OrderId, first, "7", "0.5", false, false, "Missing immutable targets",
                AcceptanceTime.AddMinutes(20), PurchaseReceiptAdjustmentCodesV1.CommandName, AcceptanceHash("snapshot-refused"),
                NotificationSnapshots: snapshots), Envelope("snapshot-refused", "snapshot-refused")));
            Assert.Equal(PurchaseReceiptAdjustmentCodesV1.Unavailable, refused.Code);
            await transaction.RollbackAsync();
        }
        // A different command genuinely revives the line later; replay must still return the original net-zero batch.
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using var owner = await PurchaseReceiptAdjustmentParticipant.EnlistAsync(connection, transaction, CompanyA,
                scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
            var changed = await owner.StageAsync(new(CompanyA, Guid.NewGuid(), seed.OrderId, first, "7", "0.5", false, false,
                "Later genuine revival", AcceptanceTime.AddMinutes(20), PurchaseReceiptAdjustmentCodesV1.CommandName,
                AcceptanceHash("later-single-undo"), NotificationSnapshots:
                [new(CompanyA, seed.OrderId, seed.LineId, "7", [historicalSalesTarget])]), Envelope("later-single-undo", "later-single"));
            Assert.Equal(("8", "0.5", true), (changed.OrderVersion, changed.AllocatableQuantity, changed.Revived));
            await transaction.CommitAsync();
        }
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using var owner = await PurchaseReceiptAdjustmentParticipant.EnlistBatchAsync(connection, transaction, CompanyA,
                scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
            var replay = await owner.StageBatchAsync(request, envelope);
            Assert.Equal((true, "7", "original-batch-correlation"), (replay.IsReplay, replay.OrderVersion, replay.OriginalCorrelationId));
            Assert.All(replay.Lines, line => Assert.True(line.IsReplay));
            Assert.Equal("0", Assert.Single(replay.Supplies).AllocatableQuantity);
            var current = (await owner.ReadSupplyAsync(seed.OrderId, seed.LineId))!;
            Assert.Equal(("8", "0.5"), (current.OrderVersion, current.AllocatableQuantity));
            await transaction.RollbackAsync();
        }
        var notification = Assert.Single(await ReadPurchasingOutboxAsync(run, CompanyA));
        Assert.Equal(PurchasingOrderLineRevivedEventV1.EventType, notification.EventType);
        var notificationPayload = JsonSerializer.Deserialize<PurchasingOrderLineRevivedPayloadV1>(notification.PayloadJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(historicalSalesTarget, Assert.Single(notificationPayload.SalesTargets!));
        Assert.Empty(await ReadPurchasingOutboxAsync(run, CompanyB));
        var company = new CompanyContext(); company.Set(CompanyA);
        await using var database = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema)).Options, company);
        var appliedBefore = (await database.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(database.Database.GetMigrations().ToArray(), appliedBefore);
        Assert.Contains("20261116222957_AddPurchaseReceiptAdjustmentBatches", appliedBefore);
        string[] proofQueries =
        [
            "SELECT (SELECT * FROM purchasing.PurchaseOrders ORDER BY Id FOR JSON PATH)",
            "SELECT (SELECT * FROM purchasing.PurchaseOrderLines ORDER BY CompanyId, OrderId, [LineNo] FOR JSON PATH)",
            "SELECT (SELECT * FROM purchasing.PurchaseAcceptanceShares ORDER BY AcceptanceId FOR JSON PATH)",
            "SELECT (SELECT * FROM purchasing.PurchaseReceiptAdjustmentBatches ORDER BY CompanyId, BatchId FOR JSON PATH)",
            "SELECT (SELECT * FROM purchasing.PurchaseReceiptAdjustments ORDER BY CompanyId, AdjustmentId FOR JSON PATH)",
            "SELECT (SELECT * FROM purchasing.PurchaseAttempts ORDER BY CompanyId, CommandName, IdempotencyKeyHash FOR JSON PATH)",
            "SELECT (SELECT * FROM purchasing.OutboxMessages ORDER BY Position, Id FOR JSON PATH)",
            "SELECT (SELECT * FROM settings.DocumentNumberFormats ORDER BY CompanyId, Series, Version FOR JSON PATH)",
            "SELECT (SELECT * FROM settings.DocumentNumberCounters ORDER BY CompanyId, Series, Year FOR JSON PATH)",
            "SELECT (SELECT * FROM settings.DocumentNumberReservations ORDER BY CompanyId, Series, DocumentId FOR JSON PATH)",
            "SELECT (SELECT * FROM settings.DocumentNumberCancellations ORDER BY CompanyId, Series, DocumentId FOR JSON PATH)",
            "SELECT (SELECT * FROM audit.AuditEvents ORDER BY Id FOR JSON PATH)",
            "SELECT (SELECT name, definition, is_disabled, is_not_trusted FROM sys.check_constraints WHERE schema_id = SCHEMA_ID(N'purchasing') ORDER BY name FOR JSON PATH)",
        ];
        var proofBefore = await Task.WhenAll(proofQueries.Select(query => run.ScalarAsync<string>(query)));
        // Target the batch/history guard itself; retain newer number reservations and cancellation evidence.
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(
            new GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations.AddPurchaseReceiptAdjustmentBatches().DownOperations[0]).Sql;
        var refusal = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51956, refusal.Number);
        Assert.Equal(appliedBefore, (await database.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Equal(proofBefore, await Task.WhenAll(proofQueries.Select(query => run.ScalarAsync<string>(query))));

        // A genuinely empty store still exercises all real Down/Up operations through IMigrator.
        await using var emptyRun = await Run.StartAsync();
        await using var emptyDatabase = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer(emptyRun.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema)).Options, company);
        var emptyApplied = (await emptyDatabase.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(emptyDatabase.Database.GetMigrations().ToArray(), emptyApplied);
        var migrator = emptyDatabase.GetService<IMigrator>();
        await migrator.MigrateAsync("20261116215806_AddPurchaseSupplierReturnResponseSnapshots");
        Assert.Equal(0, await emptyRun.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'purchasing') AND name = N'PurchaseReceiptAdjustmentBatches'"));
        await migrator.MigrateAsync();
        Assert.Equal(1, await emptyRun.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'purchasing') AND name = N'PurchaseReceiptAdjustmentBatches'"));
        Assert.Equal(emptyApplied, (await emptyDatabase.Database.GetAppliedMigrationsAsync()).ToArray());
    }

    private static async Task<IReadOnlyList<GoymenERP.BuildingBlocks.Messaging.WorkOutboxEventV1>> ReadPurchasingOutboxAsync(
        Run run, Guid companyId, string? after = null)
    {
        var company = new CompanyContext(); company.Set(companyId);
        await using var database = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString).Options, company);
        return await new PurchasingOutboxEventSource(database, company).ReadAfterAsync(after, 100, default);
    }
}
