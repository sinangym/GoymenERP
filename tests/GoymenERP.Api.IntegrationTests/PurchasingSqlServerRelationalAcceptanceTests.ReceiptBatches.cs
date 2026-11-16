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
            await stored.Store.MutateOrderAsync(Envelope("batch-cancel", "cancel"), AcceptanceHash("batch-cancel"),
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
        var refusal = await Assert.ThrowsAsync<SqlException>(() => database.GetService<IMigrator>()
            .MigrateAsync("20261116215806_AddPurchaseSupplierReturnResponseSnapshots"));
        Assert.Equal(51956, refusal.Number);
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
