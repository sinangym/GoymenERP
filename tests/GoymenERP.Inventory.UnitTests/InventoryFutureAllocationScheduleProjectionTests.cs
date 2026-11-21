using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryFutureAllocationScheduleProjectionTests
{
    private static readonly DateOnly Today = new(2026, 11, 17);

    [Fact]
    public async Task Only_an_active_allocation_with_a_past_owner_date_is_overdue_and_shared_sources_are_read_once()
    {
        var first = Allocation();
        var second = first with { AllocationId = Guid.NewGuid() };
        var inactive = first with { AllocationId = Guid.NewGuid(), Status = "Released" };
        var source = new Source { Date = Today.AddDays(-1) };
        var result = await InventoryFutureAllocationScheduleProjection.ReadAsync([first, second, inactive], Today, source);
        Assert.Equal(new("Scheduled", Today.AddDays(-1), true), result[first.AllocationId]);
        Assert.Equal(result[first.AllocationId], result[second.AllocationId]);
        Assert.Equal(new("Inactive", null, false), result[inactive.AllocationId]);
        Assert.Equal(1, source.Calls);
        Assert.Equal("Active", first.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Today_and_future_dates_are_not_overdue(int days)
    {
        var allocation = Allocation();
        var result = await InventoryFutureAllocationScheduleProjection.ReadAsync([allocation], Today, new Source { Date = Today.AddDays(days) });
        Assert.False(result[allocation.AllocationId].IsOverdue);
    }

    [Fact]
    public async Task A_readable_undated_source_is_distinct_from_failed_foreign_or_invalid_owner_answers()
    {
        var allocation = Allocation();
        var undated = await InventoryFutureAllocationScheduleProjection.ReadAsync([allocation], Today, new Source());
        Assert.Equal(new("Undated", null, false), undated[allocation.AllocationId]);
        foreach (var source in new[] { new Source { Readable = false }, new Source { Foreign = true },
                     new Source { Throw = true }, new Source { Date = default(DateOnly) } })
        {
            var result = await InventoryFutureAllocationScheduleProjection.ReadAsync([allocation], Today, source);
            Assert.Equal(new("Unavailable", null, null), result[allocation.AllocationId]);
        }
    }

    [Fact]
    public async Task Cancellation_is_not_presented_as_an_unavailable_schedule()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = new Source();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            InventoryFutureAllocationScheduleProjection.ReadAsync([Allocation()], Today, source, cancellation.Token));
        Assert.Equal(0, source.Calls);
    }

    private static InventoryFutureAllocationViewV1 Allocation()
    {
        var id = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
        return new(id, Guid.NewGuid(), "1", "Active", id, id, id, id, id, "1", id,
            "10", "10", "0", "0", null, "SENTETIK", null, null, null, at, at, null);
    }

    private sealed class Source : IInventoryFutureSupplyScheduleSourceV1
    {
        public DateOnly? Date { get; init; }
        public bool Readable { get; init; } = true;
        public bool Foreign { get; init; }
        public bool Throw { get; init; }
        public int Calls { get; private set; }
        public Task<InventoryFutureSupplyScheduleV1> GetLineScheduleAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Throw) throw new InvalidOperationException("SENTETIK owner unavailable");
            return Task.FromResult(new InventoryFutureSupplyScheduleV1(Readable, Foreign ? Guid.NewGuid() : orderId, lineId, Date));
        }
    }
}
