using GoymenERP.Api.PurchasingRuntime;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>STK-S17: synthetic owner/lock faults; SQL lock exclusion is proved separately by real SQL facts.</summary>
public sealed class PurchaseGoodsReceiptPrelockTests
{
    private static readonly Guid Company = Id(100);
    private static readonly Guid Order = Id(101);
    private static readonly Guid Line = Id(102);
    private static readonly Guid General = Id(103);
    private static readonly PurchaseGoodsReceiptTargetV1 Intent = new(Order, Line, "3");

    [Fact]
    public async Task All_sales_locks_are_distinct_and_ascending_before_the_general_purchase_lock_and_second_read()
    {
        var world = new World([View(3, 203), View(1, 201), View(3, 204), View(2, 202)]);
        Assert.True(await world.PrepareAsync());
        Assert.Equal(["target", "read", "sales:1", "sales:2", "sales:3", "purchase:103", "target", "read"], world.Events);
    }

    [Fact]
    public async Task A_fresh_subset_can_drop_released_rows_without_taking_any_new_lock()
    {
        var world = new World([View(1, 201), View(2, 202)], [View(2, 202)]);
        Assert.True(await world.PrepareAsync());
        Assert.Equal(2, world.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_new_allocation_requires_a_fresh_prewrite_transaction_even_on_an_already_locked_sales_line(bool sameSalesLine)
    {
        var world = new World([View(2, 201)], [View(2, 201), View(sameSalesLine ? 2 : 1, 202)]);
        Assert.False(await world.PrepareAsync());
        Assert.DoesNotContain("sales:1", world.Events);
        Assert.Equal(1, world.Events.Count(value => value.StartsWith("purchase:", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("company")]
    [InlineData("order")]
    [InlineData("line")]
    [InlineData("general")]
    [InlineData("version")]
    public async Task A_malformed_owner_target_is_unavailable_before_any_lock(string fault)
    {
        var world = new World([]) { Target = fault switch
        {
            "company" => Target with { CompanyId = Id(999) },
            "order" => Target with { PurchaseOrderId = Id(999) },
            "line" => Target with { PurchaseOrderLineId = Id(999) },
            "general" => Target with { PurchaseOrderGeneralLineId = Guid.Empty },
            _ => Target with { ObservedOrderVersion = "03" },
        } };
        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.PrepareAsync());
        Assert.Equal((503, PurchaseGoodsReceiptCodesV1.SourceInvalid), (refused.Status, refused.Code));
        Assert.Equal(["target"], world.Events);
    }

    [Fact]
    public async Task A_moved_technical_parent_discards_preparation_without_taking_another_purchase_lock()
    {
        var world = new World([]) { MoveParentOnReread = true };
        Assert.False(await world.PrepareAsync());
        Assert.DoesNotContain("purchase:104", world.Events);
    }

    [Theory]
    [InlineData("foreign-company")]
    [InlineData("wrong-general")]
    [InlineData("empty-demand")]
    [InlineData("zero-remaining")]
    [InlineData("duplicate")]
    public async Task An_untrustworthy_active_owner_set_is_not_treated_as_empty(string fault)
    {
        var row = fault switch
        {
            "foreign-company" => View(1, 201) with { CompanyId = Id(999) },
            "wrong-general" => View(1, 201) with { PurchaseOrderLineId = Line },
            "empty-demand" => View(1, 201) with { SalesOrderLineId = Guid.Empty },
            "zero-remaining" => View(1, 201) with { RemainingQuantity = "0" },
            _ => View(1, 201),
        };
        var world = new World(fault == "duplicate" ? [row, row] : [row]);
        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.PrepareAsync());
        Assert.Equal(503, refused.Status);
        Assert.DoesNotContain(world.Events, value => value.StartsWith("sales:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cancellation_while_waiting_is_preserved_without_a_purchase_lock_or_a_second_owner_read()
    {
        using var cancel = new CancellationTokenSource();
        var world = new World([View(1, 201)]) { CancelAtSalesLock = cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.PrepareAsync(cancel.Token));
        Assert.Equal(1, world.Reads);
        Assert.DoesNotContain("purchase:103", world.Events);
    }

    private static PurchaseAcceptanceReceiptTargetV1 Target => new(Company, Order, Line, General, "4");
    private static Guid Id(int value) => new(value, 0, 0, new byte[8]);
    private static InventoryFutureAllocationViewV1 View(int sales, int allocation) =>
        new(Id(allocation), Company, "1", "Active", Id(200), Id(sales), Order, General, Id(300), "v1", Id(301),
            "10", "10", "0", "0", null, "SENTETIK", null, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);

    private sealed class World(IReadOnlyList<InventoryFutureAllocationViewV1> initial,
        IReadOnlyList<InventoryFutureAllocationViewV1>? fresh = null) : IInventoryFutureAllocationReleaseSessionV1,
        ISalesOrderLineSerializationLockV1, IPurchaseLineSerializationLockV1
    {
        public List<string> Events { get; } = [];
        public int Reads { get; private set; }
        public PurchaseAcceptanceReceiptTargetV1 Target { get; init; } = PurchaseGoodsReceiptPrelockTests.Target;
        public bool MoveParentOnReread { get; init; }
        public CancellationTokenSource? CancelAtSalesLock { get; init; }
        private int targetReads;
        public Task<bool> PrepareAsync(CancellationToken token = default) =>
            PurchaseGoodsReceiptPrelock.PrepareAsync(Company, Intent, _ =>
            {
                Events.Add("target"); targetReads++;
                return Task.FromResult<PurchaseAcceptanceReceiptTargetV1?>(MoveParentOnReread && targetReads > 1
                    ? Target with { PurchaseOrderGeneralLineId = Id(104) } : Target);
            }, this, this, this, token);
        public Task<IReadOnlyList<InventoryFutureAllocationViewV1>> ListActiveByPurchaseLineAsync(Guid line, CancellationToken token = default)
        {
            Assert.Equal(General, line); Events.Add("read"); Reads++;
            return Task.FromResult(Reads == 1 ? initial : fresh ?? initial);
        }
        Task ISalesOrderLineSerializationLockV1.AcquireAsync(Guid line, CancellationToken token)
        {
            Events.Add($"sales:{BitConverter.ToInt32(line.ToByteArray(), 0)}");
            CancelAtSalesLock?.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask;
        }
        Task IPurchaseLineSerializationLockV1.AcquireAsync(Guid line, CancellationToken token)
        { Events.Add($"purchase:{BitConverter.ToInt32(line.ToByteArray(), 0)}"); return Task.CompletedTask; }
        public Task AcquireAllAsync(IEnumerable<Guid> lines, CancellationToken token = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<InventoryFutureAllocationViewV1>> ListActiveByDemandLineAsync(Guid line, CancellationToken token = default) => throw new NotSupportedException();
        public Task<InventoryFutureAllocationViewV1> ReleaseAsync(InventoryFutureAllocationReleaseRequestV1 request,
            InventoryCommandMetadata metadata, CancellationToken token = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
