using System.Data.Common;
using System.Text.Json;
using GoymenERP.Api.PurchasingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Real Purchasing batch and Inventory stages; fixture Settings/authority remain explicitly synthetic. Runtime NOT_RUN.</summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_receipt_batch_commits_both_owners_once_and_replays_its_one_final_version_after_later_acceptance()
    {
        await using var run = await Run.StartAsync();
        var seed = await SeedCombinedBatchAsync(run); var write = BatchHostWrite(seed.First, seed.Second);
        var envelope = GoodsReceiptPurchaseEnvelope(run, "SENTETIK-real-batch", "SENTETIK-real-batch-original");
        var first = await RunCombinedBatchHostAsync(run, write, envelope);
        Assert.Equal("6", first.OrderVersion); Assert.All(first.Lines, line => Assert.Equal("6", line.Purchase.OrderVersion));
        Assert.Equal(("2", "6"), (Assert.Single(first.Supplies).NetOpenedQuantity, first.Supplies[0].AllocatableQuantity));
        Assert.Equal(4m, await CombinedAcceptedAsync(run, seed.Seed));
        Assert.Equal(3m, await LabelBlockKgAsync(run, seed.First.LabelId, BlockState.Free));
        Assert.Equal(1m, await LabelBlockKgAsync(run, seed.Second.LabelId, BlockState.Free));
        var laterSeed = seed.Seed with { Version = 6 };
        var later = Receipt(laterSeed, Guid.NewGuid(), "1", seed.First.LocationId) with { InitialBlock = BlockState.Free };
        await StageGoodsReceiptAndCommitAsync(run, laterSeed, later, "1", "SENTETIK-after-batch", "SENTETIK-after-batch");
        var replay = await RunCombinedBatchHostAsync(run, write, envelope with { CorrelationId = "SENTETIK-batch-replay" });
        Assert.True(replay.IsReplay); Assert.Equal(("6", envelope.CorrelationId), (replay.OrderVersion, replay.OriginalCorrelationId));
        Assert.All(replay.Lines, line => Assert.True(line.Stock.IsReplay)); Assert.Equal(5m, await CombinedAcceptedAsync(run, seed.Seed));
        await using var context = run.Context(CompanyA); Assert.Equal(2, await context.GoodsReceiptReversals.CountAsync());
    }

    [SqlServerFact]
    public async Task A_fresh_authority_refusal_after_the_first_batch_inventory_stage_rolls_back_every_owner_and_preserves_retry()
    {
        await using var run = await Run.StartAsync();
        var seed = await SeedCombinedBatchAsync(run); var write = BatchHostWrite(seed.First, seed.Second);
        var envelope = GoodsReceiptPurchaseEnvelope(run, "SENTETIK-batch-rollback", "SENTETIK-batch-rollback");
        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => RunCombinedBatchHostAsync(run, write, envelope, failAfterInventory: true));
        Assert.Equal(403, refused.Status); Assert.Equal(6m, await CombinedAcceptedAsync(run, seed.Seed));
        Assert.Equal(4m, await LabelBlockKgAsync(run, seed.First.LabelId, BlockState.Free));
        Assert.Equal(2m, await LabelBlockKgAsync(run, seed.Second.LabelId, BlockState.Free));
        await using (var context = run.Context(CompanyA))
        {
            Assert.Empty(await context.GoodsReceiptReversals.ToListAsync());
            Assert.False(await context.Commands.AnyAsync(row => row.CommandName == InventoryGoodsReceiptReversalCodesV1.CommandName));
            Assert.False(await context.OutboxMessages.AnyAsync(row => row.EventType == InventoryGoodsReceiptReversalCodesV1.EventType));
        }
        Assert.False((await RunCombinedBatchHostAsync(run, write, envelope)).IsReplay);
    }

    [SqlServerFact]
    public async Task A_real_batch_revival_freezes_its_historical_sales_recipients_in_the_event_transaction_and_replay_does_not_enrich_them()
    {
        await using var run = await Run.StartAsync(); var seed = await SeedCombinedBatchAsync(run);
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            var cancellation = GoodsReceiptPurchaseEnvelope(run, "SENTETIK-notification-cancel", "SENTETIK-notification-cancel");
            await scope.ServiceProvider.GetRequiredService<IPurchasingStore>().MutateOrderAsync(cancellation, new string('a', 64),
                PurchasingCommands.OrderCancelV1, seed.Seed.OrderId,
                (order, now) => order.Cancel(order.Version, cancellation.ActorId, "SENTETIK close the remaining share", now),
                new PurchasingMutationAudit("purchasing.purchase-order.cancelled.v1", "Cancel", "SENTETIK close the remaining share"));
        }
        var historical = FutureSupplyIds.New() with { PurchaseOrder = seed.Seed.OrderId, PurchaseLine = seed.Seed.LineId,
            Product = GoodsReceiptProduct, Unit = GoodsReceiptUnit };
        if (await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.FutureSupplyLines WHERE CompanyId=@company AND PurchaseOrderLineId=@line",
            ("@company", CompanyA), ("@line", historical.PurchaseLine)) == 0)
            await run.ExecuteAsync(InsertFutureSupplyLine, historical.Line(CompanyA, 0m, 0m));
        await InsertReleasedHistoryAsync(historical);
        var write = BatchHostWrite(seed.First, seed.Second); write = write with { Request = write.Request with { ExpectedPurchaseOrderVersion = "6" } };
        var envelope = GoodsReceiptPurchaseEnvelope(run, "SENTETIK-notification-batch", "SENTETIK-notification-batch");
        var result = await RunCombinedBatchHostAsync(run, write, envelope);
        Assert.True(Assert.Single(result.Supplies).Revived);
        var originalPayload = await PayloadAsync();
        using (var document = JsonDocument.Parse(originalPayload))
        {
            var target = Assert.Single(document.RootElement.GetProperty("salesTargets").EnumerateArray());
            Assert.Equal(historical.DemandOrder, target.GetProperty("salesOrderId").GetGuid());
            Assert.Equal(historical.DemandLine, target.GetProperty("salesOrderLineId").GetGuid());
        }
        await InsertReleasedHistoryAsync(historical with { DemandOrder = Guid.NewGuid(), DemandLine = Guid.NewGuid() });
        Assert.True((await RunCombinedBatchHostAsync(run, write, envelope)).IsReplay);
        Assert.Equal(originalPayload, await PayloadAsync());
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.OutboxMessages WHERE EventType=N'purchasing.order-line.revived.v1' AND CompanyId=@company", ("@company", CompanyA)));

        Task<string> PayloadAsync() => run.ScalarAsync<string>("SELECT PayloadJson FROM purchasing.OutboxMessages WHERE EventType=N'purchasing.order-line.revived.v1' AND CompanyId=@company", ("@company", CompanyA));
        async Task InsertReleasedHistoryAsync(FutureSupplyIds ids)
        {
            var id = Guid.NewGuid();
            await run.ExecuteAsync(InsertFutureSupplyAllocation, ids.Allocation(CompanyA, id, 5m, 5m, 1));
            await run.ExecuteAsync("""
                UPDATE inventory.FutureSupplyAllocations SET RemainingQuantity=0,ReleasedQuantity=5,Status=3,
                    ReleaseReasonCode='purchase-line-closed',ReleaseOrigin='purchasing',ReleaseReason=N'SENTETIK history',ReleasedAtUtc=@at
                WHERE CompanyId=@company AND Id=@id;
                """, ("@company", CompanyA), ("@id", id), ("@at", GoodsReceiptTime));
        }
    }

    private static async Task<(GoodsReceiptAcceptanceSeed Seed, InventoryGoodsReceiptRequestV1 First, InventoryGoodsReceiptRequestV1 Second)> SeedCombinedBatchAsync(Run run)
    {
        var (seed, first) = await SeedCombinedReversalReceiptAsync(run);
        var next = seed with { Version = 4 };
        var second = Receipt(next, Guid.NewGuid(), "2", first.LocationId) with { InitialBlock = BlockState.Free };
        second = await StageGoodsReceiptAndCommitAsync(run, next, second, "2", "SENTETIK-batch-second", "SENTETIK-batch-second");
        Assert.Equal("5", second.PurchaseOrderVersionAfter); return (seed, first, second);
    }
    private static PurchaseReceiptReversalBatchWriteV1 BatchHostWrite(InventoryGoodsReceiptRequestV1 first, InventoryGoodsReceiptRequestV1 second) =>
        new(first.PurchaseOrderId, new(Guid.NewGuid(), "5", "SENTETIK two receipt correction", GoodsReceiptTime.AddHours(1),
            [new(first.AcceptanceId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1", "1", "Free"),
             new(second.AcceptanceId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1", "1", "Free")]));

    private static async Task<PurchaseReceiptReversalBatchResponseV1> RunCombinedBatchHostAsync(Run run, PurchaseReceiptReversalBatchWriteV1 write,
        PurchasingWriteEnvelope envelope, bool failAfterInventory = false)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
        services.GetRequiredService<ICompanyContext>().Set(CompanyA);
        var primary = services.GetRequiredService<PurchasingDbContext>();
        var actualFactory = services.GetRequiredService<PurchaseReceiptAdjustmentBatchParticipantFactoryV1>();
        DbConnection? connection = null; DbTransaction? transaction = null;
        var host = new PurchaseReceiptReversalHost(primary, new CombinedReversalServices(services, GoodsReceiptSettings(run)),
            services.GetRequiredService<IOrganizationLocationStatusReaderV1>(), TimeProvider.System, batchOwnerFactory: async (opened, borrowed, company, token) =>
            { connection = opened; transaction = borrowed; return await actualFactory(opened, borrowed, company, token); });
        return await host.ExecuteBatchAsync(CompanyA, write, envelope, async token =>
        {
            if (!failAfterInventory || connection is null || transaction is null) return;
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT COUNT(*) FROM inventory.GoodsReceiptReversals WHERE CompanyId = @company AND Id = @id";
            var company = command.CreateParameter(); company.ParameterName = "@company"; company.Value = CompanyA; command.Parameters.Add(company);
            var id = command.CreateParameter(); id.ParameterName = "@id"; id.Value = write.Request.Lines[0].AdjustmentId; command.Parameters.Add(id);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) > 0) throw new PurchasingServiceException(403, "forbidden");
        });
    }
}
