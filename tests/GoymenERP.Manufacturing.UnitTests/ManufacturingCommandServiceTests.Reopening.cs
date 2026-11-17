using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed partial class ManufacturingCommandServiceTests
{
    [Fact]
    public async Task A_missing_transaction_bound_period_reader_is_unavailable_before_any_reopening_write()
    {
        var fixture = new Fixture(); var order = await fixture.BalancedOrderAsync(); order.Close(order.Version, Actor, At);
        var store = new ReopeningStore(fixture);
        var service = new ManufacturingCommandService(fixture.Store, fixture.Store, fixture.Store, new ManufacturingMassVerifier(fixture.Settings),
            fixture.Authority, fixture.CompanyContext, fixture.User, fixture.Correlation, new FixedTime(), orderReopenings: store,
            reopeningInventory: new ReopeningInventoryPort());
        var result = await service.ReopenOrderAsync(order.Id, new(order.Version, "Synthetic unavailable transaction-bound reader"),
            "reopen-missing-bound-reader", CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Unavailable, result.Status); Assert.Equal(0, store.Writes);
        Assert.Equal(OrderStatus.Closed, order.Status); Assert.Empty(order.Reopenings); Assert.Empty(fixture.Store.Commands);
    }

    [Theory]
    [InlineData(AccountingPeriodStatusOutcomeV1.Denied, ManufacturingApiStatus.Denied)]
    [InlineData(AccountingPeriodStatusOutcomeV1.None, ManufacturingApiStatus.Unprocessable)]
    [InlineData(AccountingPeriodStatusOutcomeV1.NotOpen, ManufacturingApiStatus.Unprocessable)]
    public async Task Reopening_distinguishes_the_period_owners_denial_from_missing_or_closed_periods_without_writing(
        AccountingPeriodStatusOutcomeV1 outcome, ManufacturingApiStatus expected)
    {
        var fixture = new Fixture(); var order = await fixture.BalancedOrderAsync(); order.Close(order.Version, Actor, At);
        var periods = new ReopenPeriodPort(outcome); var store = new ReopeningStore(fixture);
        var result = await ReopeningService(fixture, store, periods).ReopenOrderAsync(order.Id, new(order.Version, "Synthetic correction"), "reopen-period", CancellationToken.None);
        Assert.Equal(expected, result.Status); Assert.Equal(0, store.Writes); Assert.Empty(order.Reopenings); Assert.Empty(fixture.Store.Commands);
    }

    [Fact]
    public async Task Reopening_asks_for_the_close_business_day_and_replays_the_original_version_without_a_second_inventory_effect()
    {
        var fixture = new Fixture(); var order = await fixture.BalancedOrderAsync();
        var closedAt = new DateTimeOffset(2026, 11, 16, 21, 30, 0, TimeSpan.Zero); order.Close(order.Version, Actor, closedAt);
        var periods = new ReopenPeriodPort(AccountingPeriodStatusOutcomeV1.Open); var store = new ReopeningStore(fixture);
        var service = ReopeningService(fixture, store, periods); var request = new ReopenManufacturingOrderRequestV1(order.Version, "Synthetic correction");
        var first = await service.ReopenOrderAsync(order.Id, request, "reopen-exact", CancellationToken.None);
        var replay = await service.ReopenOrderAsync(order.Id, request, "reopen-exact", CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Ok, first.Status); Assert.True(replay.IsReplay);
        Assert.Equal(first.Resource!.Version, replay.Resource!.Version); Assert.Equal(1, store.Writes);
        Assert.All(periods.Dates, day => Assert.Equal(new DateOnly(2026, 11, 17), day));
        Assert.Single(first.Resource.Reopenings!); Assert.Contains(ErpPermissions.ManufacturingOrdersReopen, fixture.Authority.Permissions);
        Assert.DoesNotContain(ErpPermissions.ManufacturingOrdersClose, fixture.Authority.Permissions);
    }

    [Fact]
    public async Task Period_owner_failure_and_a_period_that_closes_at_the_final_transaction_fence_leave_the_order_closed()
    {
        foreach (var throws in new[] { true, false })
        {
            var fixture = new Fixture(); var order = await fixture.BalancedOrderAsync(); order.Close(order.Version, Actor, At);
            var periods = new ReopenPeriodPort(AccountingPeriodStatusOutcomeV1.Open) { Throw = throws, CloseOnSecondRead = !throws };
            var store = new ReopeningStore(fixture);
            var result = await ReopeningService(fixture, store, periods).ReopenOrderAsync(order.Id,
                new(order.Version, "Synthetic correction"), "reopen-final-period", CancellationToken.None);
            Assert.Equal(throws ? ManufacturingApiStatus.Unavailable : ManufacturingApiStatus.Unprocessable, result.Status);
            Assert.Equal(0, store.Writes); Assert.Equal(OrderStatus.Closed, order.Status); Assert.Empty(fixture.Store.Commands);
        }
    }

    private static ManufacturingCommandService ReopeningService(Fixture fixture, ReopeningStore store, ReopenPeriodPort periods) =>
        new(fixture.Store, fixture.Store, fixture.Store, new ManufacturingMassVerifier(fixture.Settings), fixture.Authority,
            fixture.CompanyContext, fixture.User, fixture.Correlation, new FixedTime(), orderReopenings: store,
            accountingPeriods: periods, reopeningInventory: new ReopeningInventoryPort());

    private sealed class ReopenPeriodPort(AccountingPeriodStatusOutcomeV1 outcome) : IManufacturingReopeningAccountingPeriodStatusPortV1
    {
        public List<DateOnly> Dates { get; } = [];
        public bool Throw { get; init; }
        public bool CloseOnSecondRead { get; init; }
        public Task<AccountingPeriodStatusAnswerV1> GetStatusAsync(DateOnly on, CancellationToken cancellationToken = default)
        {
            Dates.Add(on); if (Throw) throw new InvalidOperationException("Synthetic unavailable owner");
            var result = CloseOnSecondRead && Dates.Count > 1 ? AccountingPeriodStatusOutcomeV1.NotOpen : outcome;
            return Task.FromResult(new AccountingPeriodStatusAnswerV1(result,
                result is AccountingPeriodStatusOutcomeV1.Open or AccountingPeriodStatusOutcomeV1.NotOpen ? WorkCenter : null,
                result == AccountingPeriodStatusOutcomeV1.Open ? "Open" : result == AccountingPeriodStatusOutcomeV1.NotOpen ? "Closed" : null));
        }
    }
    private sealed class ReopeningInventoryPort : IManufacturingOrderReopeningInventoryPortV1
    {
        public Task<ProductionOrder> ReopenAsync(ManufacturingOrderReopeningCommandV1 command, Func<CancellationToken, Task<ProductionOrder>> writeReopening,
            ManufacturingCommandMetadata metadata, CancellationToken cancellationToken) => writeReopening(cancellationToken);
    }
    private sealed class ReopeningStore(Fixture fixture) : IManufacturingOrderReopeningStoreV1
    {
        public int Writes { get; private set; }
        public async Task<ProductionOrder> ReopenOrderAsync(Guid orderId, long expectedVersion, Guid actorId, DateTimeOffset reopenedAt,
            string reason, ManufacturingCommandMetadata metadata, Func<DateTimeOffset, CancellationToken, Task<ManufacturingApiResult<object>?>> finalFence,
            CancellationToken cancellationToken = default)
        {
            var order = fixture.Store.Orders[orderId];
            if (await finalFence(order.ClosedAtUtc!.Value, cancellationToken) is { } refusal) throw new ManufacturingOrderReopenFenceExceptionV1(refusal);
            order.Reopen(expectedVersion, actorId, reopenedAt, reason);
            if (metadata.Completion is { } completion) await fixture.Store.RecordCommandAsync(completion(order), cancellationToken);
            Writes++; return order;
        }
    }
}
