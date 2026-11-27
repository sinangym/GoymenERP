using System.Text.Json;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Quantities;
using GoymenERP.Modules.Logistics.Domain.Returns;

namespace GoymenERP.Logistics.UnitTests;

public sealed partial class LogisticsCustomerReturnServiceTests
{
    [Fact]
    public async Task One_piece_without_owner_kilogram_provenance_never_stages_one_kilogram_or_records_an_approval()
    {
        var state = new FaultState(); var pending = PieceReturn(); var store = new FakeStore(state) { Pending = pending };
        var stock = new FakeStock(state); var service = CreateService(new FakeScope(state), store, new FakeRunner(state, stock), new FakeSource(pending));
        var error = await Assert.ThrowsAsync<LogisticsCustomerReturnServiceException>(() => service.ApproveAsync(pending.Id, new("1", Guid.NewGuid()), Metadata()));
        Assert.Equal((503, LogisticsCustomerReturnCodesV1.InventoryUnavailable), (error.Status, error.Code));
        Assert.Equal(0, stock.StageCalls); Assert.Equal(0, store.StoreDecisionCalls); Assert.Equal(0, store.RecordRefusalCalls);
        Assert.Equal((CustomerReturnStatus.Pending, 1L, 1m, QuantityUnit.Piece), (pending.Status, pending.Version, pending.Quantity.Value, pending.Quantity.Unit));
    }

    [Fact]
    public async Task Piece_return_read_and_rejection_keep_the_existing_native_paths()
    {
        var state = new FaultState(); var pending = PieceReturn(); var store = new FakeStore(state) { Pending = pending };
        var stock = new FakeStock(state); var service = CreateService(new FakeScope(state), store, new FakeRunner(state, stock), new FakeSource(pending));
        Assert.Equal("1", (await service.GetAsync(pending.Id))!.Quantity);
        // The existing fixture deliberately has no decision writer. Reaching that same seam proves
        // the new approval-only gate did not turn a native Piece rejection into Unavailable.
        await Assert.ThrowsAsync<NotSupportedException>(() => service.RejectAsync(pending.Id, new("1"), Metadata()));
        Assert.Equal(1, store.StoreDecisionCalls); Assert.Equal(0, stock.StageCalls);
        Assert.Equal(CustomerReturnStatus.Rejected, pending.Status);
    }

    [Fact]
    public async Task Provenance_is_internal_and_a_completed_native_request_preserves_the_old_three_field_wire()
    {
        var state = new FaultState(); var store = new FakeStore(state); var stock = new FakeStock(state);
        var source = Source(QuantityUnit.Kilogram);
        var service = CreateService(new FakeScope(state), store, new FakeRunner(state, stock), new FakeSource(response: source));
        var answer = await service.RequestAsync(source.LoadingInstructionId, source.LoadingInstructionLineId,
            new(Guid.NewGuid(), source.SourceDeliveryId, source.LoadingInstructionVersion.ToString(), "1", "SYNTHETIC", DateTimeOffset.UtcNow), Metadata());
        Assert.True(answer.CommandReceiptProven);
        Assert.False(new CustomerReturnResponseV1(answer.Return, false, answer.OriginalCorrelationId).CommandReceiptProven);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(answer, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(["return", "isReplay", "originalCorrelationId"], json.RootElement.EnumerateObject().Select(field => field.Name));
    }

    private static CustomerReturn PieceReturn() => CustomerReturn.Request(Guid.NewGuid(), Company, Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Quantity.Positive(1, QuantityUnit.Piece),
        Quantity.Positive(2, QuantityUnit.Piece), "SYNTHETIC piece return", "requester", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}
