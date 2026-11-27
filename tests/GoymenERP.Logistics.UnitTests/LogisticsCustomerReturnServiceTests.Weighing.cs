using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.Quantities;
using GoymenERP.Modules.Logistics.Domain.Returns;

namespace GoymenERP.Logistics.UnitTests;

public sealed partial class LogisticsCustomerReturnServiceTests
{
    [Fact]
    public async Task Actual_weighing_advances_pending_and_piece_approval_stages_actual_kilograms_and_whole_pieces_without_conversion()
    {
        var state = new FaultState(); var pending = PieceReturn(); var store = new FakeStore(state) { Pending = pending };
        var stock = new FakeStock(state); var service = CreateService(new FakeScope(state), store, new FakeRunner(state, stock), new FakeSource(pending));
        var weightId = Guid.NewGuid();
        var answer = await service.RecordWeighingAsync(pending.Id, new("1", weightId, "GrossFirst", "1013.125", "1000"), Metadata());
        Assert.True(answer.CommandReceiptProven); Assert.Equal(("Pending", "2"), (answer.Return.Status, answer.Return.Version));
        Assert.Equal((weightId, "13.125"), (answer.Return.Weighing!.WeighingId, answer.Return.Weighing.NetKg));
        Assert.Equal(0, stock.StageCalls); Assert.Equal(0, store.StoreDecisionCalls);
        // This existing fixture deliberately has no stock writer; observe the exact native boundary, not a fake stock success.
        await Assert.ThrowsAsync<NotSupportedException>(() => service.ApproveAsync(pending.Id, new("2", Guid.NewGuid()), Metadata()));
        Assert.Equal((1m, (decimal?)13.125m, (int?)1), (stock.CapturedRequest!.Quantity, stock.CapturedRequest.QuantityKg, stock.CapturedRequest.PieceCount));
        Assert.Equal(CustomerReturnStatus.Pending, pending.Status);
    }

    [Theory]
    [InlineData("TareFirst", "1000", "1013.125")]
    [InlineData("GrossFirst", "1013.125", "1000")]
    public void Recorded_net_uses_the_existing_weighbridge_calculator_and_a_stale_unweighed_decision_cannot_proceed(string direction, string first, string second)
    {
        var pending = PieceReturn(); var at = DateTimeOffset.UtcNow;
        pending.RecordWeighing(1, Guid.NewGuid(), Enum.Parse<WeighingDirection>(direction),
            Quantity.Positive(decimal.Parse(first, System.Globalization.CultureInfo.InvariantCulture), QuantityUnit.Kilogram),
            Quantity.Positive(decimal.Parse(second, System.Globalization.CultureInfo.InvariantCulture), QuantityUnit.Kilogram), "operator", at);
        Assert.Equal(13.125m, pending.Weighing!.Net.Value); Assert.Equal(2, pending.Version);
        Assert.Throws<CustomerReturnVersionConflictException>(() => pending.RequireDecision(1, "checker"));
        pending.RequireDecision(2, "checker");
        Assert.Throws<CustomerReturnRuleException>(() => pending.RecordWeighing(2, Guid.NewGuid(), WeighingDirection.TareFirst,
            Quantity.Positive(1000, QuantityUnit.Kilogram), Quantity.Positive(1001, QuantityUnit.Kilogram), "operator", at));
        pending.Reject(2, "checker", at); Assert.Equal((CustomerReturnStatus.Rejected, 3L), (pending.Status, pending.Version));
    }

    [Theory]
    [InlineData("GrossFirst", "1000", "1013.125")]
    [InlineData("TareFirst", "1000", "1000")]
    [InlineData("Unknown", "1000", "1013.125")]
    public async Task Inconsistent_or_unknown_manual_weighing_never_stages_stock_or_changes_pending(string direction, string first, string second)
    {
        var state = new FaultState(); var pending = PieceReturn(); var store = new FakeStore(state) { Pending = pending };
        var stock = new FakeStock(state); var service = CreateService(new FakeScope(state), store, new FakeRunner(state, stock), new FakeSource(pending));
        Assert.Equal(422, (await Assert.ThrowsAsync<LogisticsCustomerReturnServiceException>(() =>
            service.RecordWeighingAsync(pending.Id, new("1", Guid.NewGuid(), direction, first, second), Metadata()))).Status);
        Assert.Null(pending.Weighing); Assert.Equal(1, pending.Version); Assert.Equal(0, stock.StageCalls);
    }
}
