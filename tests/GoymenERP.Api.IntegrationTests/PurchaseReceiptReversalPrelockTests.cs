using GoymenERP.Api.PurchasingRuntime;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Explicitly synthetic owner/read-set/lock protocol; not SQL exclusion or the complete Purchasing undo journey.</summary>
public sealed class PurchaseReceiptReversalPrelockTests
{
    private static readonly Guid Company = Id(900);
    private static readonly Guid Order = Id(901);
    private static readonly Guid Acceptance = Id(902);
    private static readonly Guid General = Id(100);
    private static readonly Guid Product = Id(903);
    private static readonly Guid Unit = Id(904);
    private static readonly PurchaseReceiptReversalWriteV1 Write = new(Order, Acceptance,
        new("4", Id(905), Id(906), Id(907), "2", "1.9", "Free", "SENTETIK", DateTimeOffset.UnixEpoch));

    [Fact]
    public async Task Consumed_and_active_sales_are_locked_before_the_full_cross_purchase_union_in_ascending_order()
    {
        var world = new World();
        Assert.True(await world.PrepareAsync());
        Assert.Equal(["sales:1", "sales:3", "purchase:20", "purchase:90", "purchase:100"], world.Locks);
        Assert.Equal(2, world.TargetReads);
        Assert.Equal(2, world.InventoryReads);
        Assert.Equal(0, world.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_new_purchase_allocation_requires_a_prewrite_retry_even_on_an_already_locked_sales_line(bool sameSales)
    {
        var world = new World { GrowPurchaseSet = true, GrowOnExistingSales = sameSales };
        Assert.False(await world.PrepareAsync());
        Assert.DoesNotContain("sales:2", world.Locks);
        Assert.Equal(0, world.Writes);
    }

    [Fact]
    public async Task A_cross_purchase_line_added_to_the_fresh_demand_set_cannot_be_locked_after_the_purchase_union()
    {
        var world = new World { GrowDemandSet = true };
        Assert.False(await world.PrepareAsync());
        Assert.DoesNotContain("purchase:10", world.Locks);
        Assert.Equal(0, world.Writes);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("general")]
    [InlineData("version")]
    public async Task An_untrusted_purchase_target_is_refused_before_any_lock(string fault)
    {
        var world = new World { TargetFault = fault };
        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.PrepareAsync());
        Assert.Equal((503, PurchaseReceiptAdjustmentCodesV1.Unavailable), (refused.Status, refused.Code));
        Assert.Empty(world.Locks);
    }

    [Fact]
    public async Task Cancellation_before_the_last_sales_lock_never_takes_a_purchase_lock_or_writes()
    {
        using var cancellation = new CancellationTokenSource();
        var world = new World { CancelAtSales = cancellation };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.PrepareAsync(cancellation.Token));
        Assert.DoesNotContain(world.Locks, item => item.StartsWith("purchase:", StringComparison.Ordinal));
        Assert.Equal(0, world.Writes);
    }

    private static Guid Id(int value) => new(value, 0, 0, new byte[8]);
    private static InventoryFutureAllocationViewV1 View(int allocation, int sales, int purchase) =>
        new(Id(allocation), Company, "1", "Active", Id(500 + sales), Id(sales), purchase == 100 ? Order : Id(600 + purchase),
            Id(purchase), Product, "7", Unit, "2", "2", "0", "0", null, "SENTETIK", null, null, null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);

    private sealed class World : IPurchaseReceiptAdjustmentParticipantV1, IInventoryGoodsReceiptReversalParticipantV1,
        IInventoryFutureAllocationReleaseSessionV1, ISalesOrderLineSerializationLockV1, IPurchaseLineSerializationLockV1
    {
        public List<string> Locks { get; } = [];
        public int TargetReads { get; private set; }
        public int InventoryReads { get; private set; }
        public int Writes { get; private set; }
        public string? TargetFault { get; init; }
        public bool GrowPurchaseSet { get; init; }
        public bool GrowOnExistingSales { get; init; }
        public bool GrowDemandSet { get; init; }
        public CancellationTokenSource? CancelAtSales { get; init; }
        private int purchaseReads;
        private readonly Dictionary<Guid, int> demandReads = [];
        public Task<bool> PrepareAsync(CancellationToken token = default) => PurchaseReceiptReversalHost.PrepareLockedAsync(
            Company, Write, this, this, this, this, this, _ => Task.CompletedTask, token);

        public Task<PurchaseReceiptAdjustmentTargetV1?> ReadTargetAsync(Guid orderId, Guid acceptanceId, CancellationToken token = default)
        {
            TargetReads++;
            return Task.FromResult<PurchaseReceiptAdjustmentTargetV1?>(new(TargetFault == "company" ? Id(999) : Company,
                Order, Acceptance, General, TargetFault == "general" ? Guid.Empty : General, Product, "7", Unit, true,
                TargetFault == "version" ? "04" : "4", "4", "0"));
        }

        public Task<InventoryGoodsReceiptReversalTargetV1?> ReadTargetAsync(Guid acceptanceId, CancellationToken token = default)
        {
            InventoryReads++;
            var request = new InventoryGoodsReceiptRequestV1(Company, Acceptance, Order, General, "4", Product, "7", Unit,
                Id(910), Id(911), "SENTETIK", null, null, [], StockPartyKind.Company, Company, StockPartyKind.Company, Company,
                Id(912), BlockState.Free, Id(913), "4", DateTimeOffset.UnixEpoch);
            var result = new InventoryGoodsReceiptResultV1(Company, Acceptance, Order, General, "4", Product, "7", Unit,
                request.LotId, request.LabelId, "1", request.MovementId, "4", false, "SENTETIK");
            return Task.FromResult<InventoryGoodsReceiptReversalTargetV1?>(new(new(request, result), "1", "0",
                [new(Id(914), Id(915), Id(503), Id(3), General, "1", "2", "2", "0", Id(916))]));
        }

        public Task<IReadOnlyList<InventoryFutureAllocationViewV1>> ListActiveByPurchaseLineAsync(Guid line, CancellationToken token = default)
        {
            Assert.Equal(General, line); purchaseReads++;
            IReadOnlyList<InventoryFutureAllocationViewV1> rows = [View(201, 1, 100)];
            if (GrowPurchaseSet && purchaseReads > 1) rows = [.. rows, View(202, GrowOnExistingSales ? 1 : 2, 100)];
            return Task.FromResult(rows);
        }

        public Task<IReadOnlyList<InventoryFutureAllocationViewV1>> ListActiveByDemandLineAsync(Guid line, CancellationToken token = default)
        {
            demandReads[line] = demandReads.GetValueOrDefault(line) + 1;
            var number = BitConverter.ToInt32(line.ToByteArray(), 0);
            IReadOnlyList<InventoryFutureAllocationViewV1> rows = number == 1 ? [View(201, 1, 100), View(203, 1, 90)] : [View(204, 3, 20)];
            if (GrowDemandSet && demandReads[line] > 1) rows = [.. rows, View(205 + number, number, 10)];
            return Task.FromResult(rows);
        }

        Task ISalesOrderLineSerializationLockV1.AcquireAsync(Guid line, CancellationToken token)
        { Locks.Add($"sales:{BitConverter.ToInt32(line.ToByteArray(), 0)}"); CancelAtSales?.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        Task IPurchaseLineSerializationLockV1.AcquireAsync(Guid line, CancellationToken token)
        { Locks.Add($"purchase:{BitConverter.ToInt32(line.ToByteArray(), 0)}"); token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public Task AcquireAllAsync(IEnumerable<Guid> lines, CancellationToken token = default) => throw new NotSupportedException();
        public Task<PurchaseReceiptAdjustmentResultV1?> FindReplayAsync(string command, string hash, PurchasingWriteEnvelope envelope, CancellationToken token = default) => throw new NotSupportedException();
        public Task<PurchaseReceiptAdjustmentResultV1> StageAsync(PurchaseReceiptAdjustmentStageV1 request, PurchasingWriteEnvelope envelope, CancellationToken token = default)
        { Writes++; throw new NotSupportedException(); }
        public Task<PurchaseReceiptAdjustmentSupplyV1?> ReadSupplyAsync(Guid order, Guid line, CancellationToken token = default) => throw new NotSupportedException();
        public Task<InventoryGoodsReceiptReversalResultV1?> ReadReplayAsync(InventoryGoodsReceiptReversalRequestV1 request, InventoryCommandMetadata metadata, CancellationToken token = default) => throw new NotSupportedException();
        public Task<InventoryGoodsReceiptReversalResultV1> StageAsync(InventoryGoodsReceiptReversalRequestV1 request, InventoryGoodsReceiptReversalSupplyV1 supply,
            InventoryCommandMetadata metadata, CancellationToken token = default) { Writes++; throw new NotSupportedException(); }
        public Task<InventoryFutureAllocationViewV1> ReleaseAsync(InventoryFutureAllocationReleaseRequestV1 request, InventoryCommandMetadata metadata,
            CancellationToken token = default) { Writes++; throw new NotSupportedException(); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
