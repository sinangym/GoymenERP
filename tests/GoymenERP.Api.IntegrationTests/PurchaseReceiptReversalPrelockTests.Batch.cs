using GoymenERP.Api.PurchasingRuntime;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchaseReceiptReversalPrelockTests
{
    [Fact]
    public async Task A_receipt_batch_locks_one_full_sales_and_purchase_union_before_any_owner_write()
    {
        var world = new World();
        var write = BatchWrite();
        Assert.True(await PurchaseReceiptReversalHost.PrepareBatchLockedAsync(Company, write, world, world, world, world, world,
            _ => Task.CompletedTask, CancellationToken.None));
        Assert.Equal(["sales:1", "sales:3", "purchase:20", "purchase:90", "purchase:100"], world.Locks);
        Assert.Equal(4, world.TargetReads); Assert.Equal(4, world.InventoryReads); Assert.Equal(0, world.Writes);
    }

    [Fact]
    public async Task A_receipt_batch_reread_that_grows_the_locked_set_retries_without_staging_any_row()
    {
        var world = new World { GrowDemandSet = true };
        Assert.False(await PurchaseReceiptReversalHost.PrepareBatchLockedAsync(Company, BatchWrite(), world, world, world, world, world,
            _ => Task.CompletedTask, CancellationToken.None));
        Assert.DoesNotContain("purchase:10", world.Locks); Assert.Equal(0, world.Writes);
    }

    [Fact]
    public void Batch_child_keys_are_distinct_per_adjustment_and_stable_across_replays()
    {
        Assert.NotEqual(PurchaseReceiptReversalHost.BatchChildKey("synthetic-key", Id(1)), PurchaseReceiptReversalHost.BatchChildKey("synthetic-key", Id(2)));
        Assert.Equal(PurchaseReceiptReversalHost.BatchChildKey("synthetic-key", Id(1)), PurchaseReceiptReversalHost.BatchChildKey("synthetic-key", Id(1)));
    }

    private static PurchaseReceiptReversalBatchWriteV1 BatchWrite() => new(Order, new(Id(920), "4", "SENTETIK batch", DateTimeOffset.UnixEpoch,
        [new(Acceptance, Id(921), Id(922), Id(923), "1", "1", "Free"), new(Id(924), Id(925), Id(926), Id(927), "1", "1", "Free")]));
}
