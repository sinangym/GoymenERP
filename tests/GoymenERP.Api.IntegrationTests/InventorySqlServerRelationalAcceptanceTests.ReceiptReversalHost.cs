using GoymenERP.Api.Endpoints;
using GoymenERP.Api.PurchasingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F017-e/API44: genuine Purchasing acceptance + adjustment and genuine Inventory receipt/reversal on real SQL.
/// These facts isolate the host transaction: authority callback is explicitly synthetic; no Sales conversion is seeded here.
/// Conversion caps/prior releases have separate owner facts. Runtime NOT_RUN.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Receipt_undo_host_commits_both_real_owners_and_replays_the_immutable_result_after_a_later_acceptance()
    {
        await using var run = await Run.StartAsync();
        var (seed, receipt) = await SeedCombinedReversalReceiptAsync(run);
        var write = CombinedReversalWrite(receipt);
        var envelope = GoodsReceiptPurchaseEnvelope(run, "SENTETIK-combined-undo", "SENTETIK-combined-original");
        var first = await RunCombinedReversalHostAsync(run, write, envelope);
        Assert.Equal(("5", "2", "8", false),
            (first.Purchase.OrderVersion, first.Purchase.NetOpenedQuantity, first.Purchase.AllocatableQuantity, first.Purchase.IsReplay));
        Assert.Equal(2m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Free));
        Assert.Equal(2m, await CombinedAcceptedAsync(run, seed));

        var laterSeed = seed with { Version = 5 };
        var later = Receipt(laterSeed, Guid.NewGuid(), "1", receipt.LocationId) with { InitialBlock = BlockState.Free };
        await StageGoodsReceiptAndCommitAsync(run, laterSeed, later, "1", "SENTETIK-later-acceptance", "SENTETIK-later");
        var replay = await RunCombinedReversalHostAsync(run, write, envelope with { CorrelationId = "SENTETIK-replay" });
        Assert.True(replay.Purchase.IsReplay);
        Assert.True(replay.Stock.IsReplay);
        Assert.Equal(("5", envelope.CorrelationId, first.Stock.InventoryMovementId),
            (replay.Purchase.OrderVersion, replay.Purchase.OriginalCorrelationId, replay.Stock.InventoryMovementId));
        Assert.Equal(3m, await CombinedAcceptedAsync(run, seed));
        await using var inventory = run.Context(CompanyA);
        Assert.Single(await inventory.GoodsReceiptReversals.Where(row => row.AcceptanceId == receipt.AcceptanceId).ToListAsync());
    }

    [SqlServerFact]
    public async Task Receipt_undo_host_callback_failure_rolls_back_both_real_owners_and_preserves_the_key_for_retry()
    {
        await using var run = await Run.StartAsync();
        var (seed, receipt) = await SeedCombinedReversalReceiptAsync(run);
        var write = CombinedReversalWrite(receipt);
        var envelope = GoodsReceiptPurchaseEnvelope(run, "SENTETIK-rollback-undo", "SENTETIK-rollback");
        var callbacks = 0;
        await Assert.ThrowsAsync<CombinedReversalCallbackException>(() => RunCombinedReversalHostAsync(run, write, envelope,
            () => { callbacks++; throw new CombinedReversalCallbackException(); }));
        Assert.Equal(1, callbacks);
        Assert.Equal(4m, await CombinedAcceptedAsync(run, seed));
        Assert.Equal(4m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Free));
        await using (var inventory = run.Context(CompanyA))
        {
            Assert.Empty(await inventory.GoodsReceiptReversals.ToListAsync());
            Assert.False(await inventory.Commands.AnyAsync(row => row.CommandName == InventoryGoodsReceiptReversalCodesV1.CommandName));
        }
        Assert.False((await RunCombinedReversalHostAsync(run, write, envelope)).Purchase.IsReplay);
        Assert.Equal(2m, await CombinedAcceptedAsync(run, seed));
    }

    [SqlServerFact]
    public async Task Receipt_undo_host_fresh_authority_failure_after_owner_staging_rolls_back_both_owners()
    {
        await using var run = await Run.StartAsync();
        var (seed, receipt) = await SeedCombinedReversalReceiptAsync(run);
        var write = CombinedReversalWrite(receipt);
        var envelope = GoodsReceiptPurchaseEnvelope(run, "SENTETIK-authority-undo", "SENTETIK-authority");
        var returnCallbackReached = false;
        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => RunCombinedReversalHostAsync(run, write, envelope,
            () => returnCallbackReached = true,
            _ => returnCallbackReached ? Task.FromException(new PurchasingServiceException(403, "forbidden")) : Task.CompletedTask));
        Assert.Equal(403, refused.Status);
        Assert.True(returnCallbackReached);
        Assert.Equal(4m, await CombinedAcceptedAsync(run, seed));
        Assert.Equal(4m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Free));
        Assert.False((await RunCombinedReversalHostAsync(run, write, envelope)).Purchase.IsReplay);
    }

    [SqlServerFact]
    public async Task Receipt_undo_host_uses_the_same_purchase_serialization_resource_and_busy_keeps_the_key_retryable()
    {
        await using var run = await Run.StartAsync();
        var (seed, receipt) = await SeedCombinedReversalReceiptAsync(run);
        var write = CombinedReversalWrite(receipt);
        var envelope = GoodsReceiptPurchaseEnvelope(run, "SENTETIK-busy-undo", "SENTETIK-busy");
        await using (var blocker = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await blocker.OpenAsync();
            await using var held = await blocker.BeginTransactionAsync();
            await new PurchaseLineSerializationLockHostBinder(Company(CompanyA)).Bind(blocker, held).AcquireAsync(seed.LineId);
            var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => RunCombinedReversalHostAsync(run, write, envelope));
            Assert.Equal((409, PurchasingStoreCodes.LineBusy), (refused.Status, refused.Code));
            Assert.Equal(4m, await CombinedAcceptedAsync(run, seed));
            Assert.Equal(4m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Free));
            await held.RollbackAsync();
        }
        Assert.False((await RunCombinedReversalHostAsync(run, write, envelope)).Purchase.IsReplay);
    }

    private static async Task<(GoodsReceiptAcceptanceSeed Seed, InventoryGoodsReceiptRequestV1 Receipt)> SeedCombinedReversalReceiptAsync(Run run)
    {
        var seed = await SeedGoodsReceiptPurchaseOrderAsync(run);
        var receipt = Receipt(seed, Guid.NewGuid(), "4", await run.LocationOfAsync(CompanyA)) with { InitialBlock = BlockState.Free };
        receipt = await StageGoodsReceiptAndCommitAsync(run, seed, receipt, "4", "SENTETIK-reversal-seed", "SENTETIK-reversal-seed");
        return (seed, receipt);
    }

    private static PurchaseReceiptReversalWriteV1 CombinedReversalWrite(InventoryGoodsReceiptRequestV1 receipt) => new(
        receipt.PurchaseOrderId, receipt.AcceptanceId,
        new(receipt.PurchaseOrderVersionAfter, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "2", "2", "Free",
            "SENTETIK combined receipt undo", GoodsReceiptTime.AddHours(1)));

    private static async Task<decimal> CombinedAcceptedAsync(Run run, GoodsReceiptAcceptanceSeed seed)
    {
        await using var context = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString).Options, Company(CompanyA));
        return await context.OrderLines.Where(row => row.CompanyId == CompanyA && row.OrderId == seed.OrderId && row.LineId == seed.LineId)
            .Select(row => row.Accepted).SingleAsync();
    }

    private static async Task<PurchaseReceiptReversalResponseV1> RunCombinedReversalHostAsync(Run run,
        PurchaseReceiptReversalWriteV1 write, PurchasingWriteEnvelope envelope, Action? afterInventory = null,
        Func<CancellationToken, Task>? freshAuthority = null)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<ICompanyContext>().Set(CompanyA);
        var primary = services.GetRequiredService<PurchasingDbContext>();
        var host = new PurchaseReceiptReversalHost(primary, new CombinedReversalServices(services, GoodsReceiptSettings(run)),
            services.GetRequiredService<IOrganizationLocationStatusReaderV1>(), TimeProvider.System,
            services.GetRequiredService<PurchaseReceiptAdjustmentParticipantFactoryV1>());
        return await host.ExecuteWithWriteAsync(CompanyA, write, envelope, (response, _) =>
        {
            afterInventory?.Invoke();
            return Task.FromResult(response);
        }, freshAuthority ?? (_ => Task.CompletedTask));
    }

    private sealed class CombinedReversalServices(IServiceProvider scoped, ISettingsPrerequisiteProvider settings) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(ISettingsPrerequisiteProvider) ? settings : scoped.GetService(type);
    }
    private sealed class CombinedReversalCallbackException : Exception { }
}
