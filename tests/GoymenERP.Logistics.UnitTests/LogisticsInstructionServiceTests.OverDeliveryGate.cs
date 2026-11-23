using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// WP4 (SAT-D475): the Sales over-delivery gate of the definite shipment. The gate is asked on every execution with the order
/// line's shipped total before the shipment, the attempted quantity and the shipper; Held (a request waits) and Blocked are
/// recorded terminal 409s with no stock exit; a missing, failing or off-question port is a 503 that records nothing.
/// </summary>
public sealed partial class LogisticsInstructionServiceTests
{
    [Fact]
    public async Task A_shipment_in_the_approval_band_stops_with_a_recorded_refusal_and_no_stock_exit_until_the_request_is_approved()
    {
        var (world, runner, id, line) = await LoadedWorldAsync();
        world.Store.ExtraShipped = 3m;
        world.OverDeliveryGate = new FakeOverDeliveryGate { Result = SalesOverDeliveryGateResultV1.Held, RequestNo = 1 };

        var refused = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service(runner).RecordShipmentAsync(id, line, Shipment(), Metadata("ship-over-held")));

        Assert.Equal((409, LogisticsOverDeliveryGateCodesV1.ApprovalPending), (refused.Status, refused.Code));
        Assert.Single(world.Store.Refusals);
        Assert.Empty(runner.Stock.Requests);
        var question = Assert.Single(world.OverDeliveryGate.Requests);
        Assert.Equal((3m, 2m, id, Actor), (question.ShippedBefore, question.Quantity, question.LoadingInstructionId, question.Envelope.ActorId));
        Assert.NotEqual(Guid.Empty, question.ShipmentId);

        // The same key replays the recorded refusal without asking the gate again.
        await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service(runner).RecordShipmentAsync(id, line, Shipment(), Metadata("ship-over-held")));
        Assert.Single(world.OverDeliveryGate.Requests);

        // Approved (the Sales gate now passes): a new attempt ships.
        world.OverDeliveryGate.Result = SalesOverDeliveryGateResultV1.Pass;
        world.OverDeliveryGate.RequestNo = null;
        var shipped = await world.Service(runner).RecordShipmentAsync(id, line, Shipment(), Metadata("ship-over-approved"));
        Assert.Equal("2", shipped.Detail.Lines[0].Shipped);
        Assert.Single(runner.Stock.Requests);
    }

    [Fact]
    public async Task A_shipment_in_the_block_band_is_a_recorded_refusal()
    {
        var (world, runner, id, line) = await LoadedWorldAsync();
        world.OverDeliveryGate = new FakeOverDeliveryGate { Result = SalesOverDeliveryGateResultV1.Blocked };

        var refused = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service(runner).RecordShipmentAsync(id, line, Shipment(), Metadata("ship-over-blocked")));

        Assert.Equal((409, LogisticsOverDeliveryGateCodesV1.Blocked), (refused.Status, refused.Code));
        Assert.Single(world.Store.Refusals);
        Assert.Empty(runner.Stock.Requests);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("fault")]
    [InlineData("unavailable")]
    [InlineData("other-line")]
    [InlineData("held-without-number")]
    public async Task An_unusable_over_delivery_gate_is_unavailable_and_records_nothing(string mode)
    {
        var (world, runner, id, line) = await LoadedWorldAsync();
        world.OverDeliveryGate = mode switch
        {
            "missing" => null,
            "fault" => new FakeOverDeliveryGate { Fault = new InvalidOperationException("SENTETIK") },
            "unavailable" => new FakeOverDeliveryGate { Result = SalesOverDeliveryGateResultV1.Unavailable },
            "other-line" => new FakeOverDeliveryGate { AnswerLineId = Guid.NewGuid() },
            _ => new FakeOverDeliveryGate { Result = SalesOverDeliveryGateResultV1.Held },
        };

        var refused = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service(runner).RecordShipmentAsync(id, line, Shipment(), Metadata("ship-over-" + mode)));

        Assert.Equal((503, LogisticsOverDeliveryGateCodesV1.GateUnavailable), (refused.Status, refused.Code));
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(runner.Stock.Requests);
        Assert.Equal(0, runner.Commits);
    }

    /// <summary>The Sales owner's over-delivery gate answer for the asked line unless a test plants another.</summary>
    private sealed class FakeOverDeliveryGate : ISalesOverDeliveryGatePortV1
    {
        public SalesOverDeliveryGateResultV1 Result { get; set; } = SalesOverDeliveryGateResultV1.Pass;
        public int? RequestNo { get; set; }
        public Guid? AnswerLineId { get; set; }
        public Exception? Fault { get; set; }
        public List<SalesOverDeliveryGateRequestV1> Requests { get; } = [];

        public Task<SalesOverDeliveryGateReadV1> EvaluateAsync(SalesOverDeliveryGateRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Fault is { } fault) throw fault;
            return Task.FromResult(new SalesOverDeliveryGateReadV1(request.OrderId, AnswerLineId ?? request.LineId, Result, RequestNo,
                SalesOverDeliveryGateContractV1.Version));
        }
    }
}
