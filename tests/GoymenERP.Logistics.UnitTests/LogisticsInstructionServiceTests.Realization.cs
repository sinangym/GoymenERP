using System.Text.Json;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Logistics.UnitTests;

// SENTETIK / NOT_ENV: these tests exercise the owner protocol, not real SQL atomicity or user acceptance.
public sealed partial class LogisticsInstructionServiceTests
{
    private static readonly Guid Allocation = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Label = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset EventTime = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Shipment_uses_server_allocation_label_and_exact_versions_and_commits_after_final_fence()
    {
        var (world, runner, id, line) = await LoadedWorldAsync();
        var request = Shipment();
        var result = await world.Service(runner).RecordShipmentAsync(id, line, request, Metadata("ship-1"));

        Assert.Equal(("5", "2", "3", "2"), (result.Detail.Instruction.Version, result.Detail.Lines[0].Shipped,
            result.Detail.Lines[0].LoadedNotShipped, result.Detail.Lines[0].ShippedNotDelivered));
        var staged = Assert.Single(runner.Stock.Requests);
        Assert.Equal((Company, id, line, Allocation, Label, "7", "11", request.MovementId, "2", EventTime),
            (staged.CompanyId, staged.LoadingInstructionId, staged.LoadingInstructionLineId, staged.AllocationId, staged.LabelId,
                staged.ExpectedAllocationVersion, staged.ExpectedLabelVersion, staged.MovementId, staged.QuantityKg, staged.OccurredAtUtc));
        Assert.Equal((1, 0, 1, 1), (runner.ShipmentRuns, runner.DeliveryRuns, runner.Fences, runner.Commits));
    }

    [Fact]
    public async Task Delivery_and_reopen_preserve_exact_source_and_replay_never_produces_another_stock_exit()
    {
        var (world, runner, id, line) = await LoadedWorldAsync();
        var shipment = Shipment();
        await world.Service(runner).RecordShipmentAsync(id, line, shipment, Metadata("ship-1"));
        var delivery = new RecordDeliveryRequest("5", Guid.NewGuid(), shipment.ShipmentId, "1.25", EventTime.AddMinutes(5));
        var result = await world.Service(runner).RecordDeliveryAsync(id, line, delivery, Metadata("delivery-1"));
        var reopened = await world.Service(runner).GetInstructionDetailAsync(id);
        Assert.Equal(("6", "1.25", "0.75"), (reopened.Instruction.Version, reopened.Lines[0].Delivered, reopened.Lines[0].ShippedNotDelivered));
        Assert.Equal(shipment.ShipmentId, result.Detail.Shares.Single(share => share.Kind == "Delivered").SourceShipmentId);

        world.ResetOwnerCalls();
        var replay = await world.Service(runner).RecordShipmentAsync(id, line, shipment, Metadata("ship-1", correlation: "retry"));
        var deliveryReplay = await world.Service(runner).RecordDeliveryAsync(id, line, delivery, Metadata("delivery-1", correlation: "retry"));
        Assert.True(replay.IsReplay);
        Assert.True(deliveryReplay.IsReplay);
        Assert.Equal("synthetic-correlation", replay.OriginalCorrelationId);
        Assert.Single(runner.Stock.Requests);
        Assert.Equal((1, 1, 2), (runner.ShipmentRuns, runner.DeliveryRuns, runner.Commits));
        Assert.Equal((0, 0, 0), (world.Accounts.Calls, world.OrderLines.Calls, world.Settings.Calls));
    }

    [Theory]
    [InlineData("3", "1", LogisticsProblemCodesV1.VersionConflict)]
    [InlineData("4", "5.001", LogisticsRealizationCodesV1.ShipmentExceedsLoaded)]
    public async Task Stale_or_overloaded_shipment_records_and_replays_refusal_without_stock(
        string version, string quantity, string code)
    {
        var (world, runner, id, line) = await LoadedWorldAsync();
        var request = Shipment() with { ExpectedVersion = version, Quantity = quantity };
        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service(runner).RecordShipmentAsync(id, line, request, Metadata("ship-refusal")));
        var replay = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service(runner).RecordShipmentAsync(id, line, request, Metadata("ship-refusal")));
        Assert.Equal((409, code, false, true), (refusal.Status, refusal.Code, refusal.IsReplay, replay.IsReplay));
        Assert.Single(world.Store.Refusals);
        Assert.Empty(runner.Stock.Requests);
        Assert.Equal("4", (await world.Service().GetInstructionDetailAsync(id)).Instruction.Version);
    }

    [Theory]
    [InlineData("company", 503, LogisticsProblemCodesV1.ReferenceProviderInvalid)]
    [InlineData("order-line", 409, LogisticsRealizationCodesV1.AllocationSourceMismatch)]
    [InlineData("product", 409, LogisticsRealizationCodesV1.AllocationSourceMismatch)]
    [InlineData("missing", 404, "not_found")]
    public async Task Allocation_is_this_company_order_line_and_product_or_no_stock_is_staged(string fault, int status, string code)
    {
        var (world, runner, id, line) = await LoadedWorldAsync();
        runner.Stock.AllocationFault = fault;
        var request = Shipment();
        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service(runner).RecordShipmentAsync(id, line, request, Metadata("ship-1")));
        Assert.Equal((status, code), (refusal.Status, refusal.Code));
        if (status == 409)
        {
            var replay = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
                world.Service(runner).RecordShipmentAsync(id, line, request, Metadata("ship-1", correlation: "retry")));
            var mismatch = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
                world.Service(runner).RecordShipmentAsync(id, line, request with { Quantity = "1" }, Metadata("ship-1", correlation: "changed")));
            Assert.True(replay.IsReplay);
            Assert.Equal(LogisticsProblemCodesV1.SameKeyPayloadMismatch, mismatch.Code);
            Assert.Single(world.Store.Refusals);
            Assert.Equal(1, runner.Commits);
        }
        else
        {
            Assert.Empty(world.Store.Refusals);
            Assert.Equal(0, runner.Commits);
        }
        Assert.Empty(runner.Stock.Requests);
        Assert.Equal("4", (await world.Service().GetInstructionDetailAsync(id)).Instruction.Version);
    }

    [Fact]
    public async Task Inventory_terminal_conflict_is_recorded_replayed_and_changed_payload_is_refused()
    {
        var (world, runner, id, line) = await LoadedWorldAsync();
        runner.Stock.TerminalFailureCode = "inventory_allocation_version_conflict";
        var request = Shipment();

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service(runner).RecordShipmentAsync(id, line, request, Metadata("inventory-refusal")));
        var replay = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service(runner).RecordShipmentAsync(id, line, request, Metadata("inventory-refusal", correlation: "retry")));
        var mismatch = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service(runner).RecordShipmentAsync(id, line, request with { Quantity = "1" }, Metadata("inventory-refusal", correlation: "changed")));

        Assert.Equal((409, "inventory_allocation_version_conflict", false), (refusal.Status, refusal.Code, refusal.IsReplay));
        Assert.Equal((409, "inventory_allocation_version_conflict", true), (replay.Status, replay.Code, replay.IsReplay));
        Assert.Equal(LogisticsProblemCodesV1.SameKeyPayloadMismatch, mismatch.Code);
        Assert.Single(world.Store.Refusals);
        Assert.Empty(runner.Stock.Requests);
        Assert.Equal((1, 1), (runner.ShipmentRuns, runner.Commits));
        Assert.Equal("4", (await world.Service().GetInstructionDetailAsync(id)).Instruction.Version);
    }

    [Fact]
    public async Task Inventory_authority_lost_with_terminal_conflict_records_no_refusal_attempt()
    {
        var (world, runner, id, line) = await LoadedWorldAsync();
        runner.Stock.BeforeTerminalFailure = () => world.Authority.DeniedPermission =
            LogisticsRealizationDependenciesV1.InventoryPhysicalAllocationWritePermission;
        runner.Stock.TerminalFailureCode = "inventory_allocation_version_conflict";

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service(runner).RecordShipmentAsync(id, line, Shipment(), Metadata("inventory-revoked-refusal")));

        Assert.Equal(403, refusal.Status);
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(runner.Stock.Requests);
        Assert.Equal((1, 1, 0), (runner.ShipmentRuns, runner.DeliveryRuns, runner.Commits));
        Assert.Equal("4", (await world.Service().GetInstructionDetailAsync(id)).Instruction.Version);
    }

    [Fact]
    public async Task Load_authority_does_not_substitute_for_separate_delivery_authority()
    {
        var (world, runner, id, line) = await LoadedWorldAsync();
        var shipment = Shipment();
        await world.Service(runner).RecordShipmentAsync(id, line, shipment, Metadata("ship-1"));
        world.Authority.DeniedPermission = ReadPermission;
        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service(runner).RecordDeliveryAsync(id, line,
            new RecordDeliveryRequest("5", Guid.NewGuid(), shipment.ShipmentId, "1", EventTime), Metadata("delivery-1")));
        Assert.Equal(403, refusal.Status);
        Assert.Equal(0, runner.DeliveryRuns);
        Assert.Single(runner.Stock.Requests);
    }

    [Fact]
    public async Task Authority_lost_after_stock_staging_never_reaches_commit()
    {
        var (world, runner, id, line) = await LoadedWorldAsync();
        runner.Stock.AfterStage = () => world.Authority.Granted = false;
        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service(runner).RecordShipmentAsync(id, line, Shipment(), Metadata("ship-1")));
        Assert.Equal((403, 0), (refusal.Status, runner.Commits));
        Assert.Single(runner.Stock.Requests);
        // The API-host real-SQL suite must additionally prove rollback of the staged Inventory + Logistics rows.
    }

    [Fact]
    public async Task Inventory_allocation_authority_lost_after_stock_staging_never_reaches_commit()
    {
        var (world, runner, id, line) = await LoadedWorldAsync();
        runner.Stock.AfterStage = () => world.Authority.DeniedPermission =
            LogisticsRealizationDependenciesV1.InventoryPhysicalAllocationWritePermission;

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service(runner).RecordShipmentAsync(id, line, Shipment(), Metadata("ship-inventory-fence")));

        Assert.Equal((403, 0), (refusal.Status, runner.Commits));
        Assert.Single(runner.Stock.Requests);
        // The fake runner has no rollback-capable DbContext; real host rollback is pinned by L04-05 SQL effects.
    }

    [Fact]
    public async Task Replay_disagreement_cannot_commit_or_repair_a_second_movement()
    {
        var (world, runner, id, line) = await LoadedWorldAsync();
        runner.Stock.ForceReplay = true;
        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service(runner).RecordShipmentAsync(id, line, Shipment(), Metadata("ship-1")));
        Assert.Equal((503, LogisticsProblemCodesV1.ReferenceProviderInvalid, 0), (refusal.Status, refusal.Code, runner.Commits));
        Assert.Single(runner.Stock.Requests);
    }

    [Fact]
    public async Task Missing_transaction_runner_is_unavailable_not_a_partial_write()
    {
        var (world, _, id, line) = await LoadedWorldAsync();
        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().RecordShipmentAsync(id, line, Shipment(), Metadata("ship-1")));
        Assert.Equal((503, LogisticsRealizationCodesV1.Unavailable), (refusal.Status, refusal.Code));
        Assert.Equal("4", (await world.Service().GetInstructionDetailAsync(id)).Instruction.Version);
    }

    [Fact]
    public async Task Delivery_is_capped_by_named_shipment_not_the_lines_other_shipments()
    {
        var (world, runner, id, line) = await LoadedWorldAsync();
        var first = Shipment();
        await world.Service(runner).RecordShipmentAsync(id, line, first, Metadata("ship-1"));
        await world.Service(runner).RecordShipmentAsync(id, line, Shipment() with { ExpectedVersion = "5" }, Metadata("ship-2"));
        var request = new RecordDeliveryRequest("6", Guid.NewGuid(), first.ShipmentId, "2.001", EventTime);
        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service(runner).RecordDeliveryAsync(id, line, request, Metadata("delivery-over")));
        Assert.Equal((409, LogisticsRealizationCodesV1.DeliveryExceedsShipment), (refusal.Status, refusal.Code));
        Assert.Equal(2, runner.Stock.Requests.Count);
        Assert.Equal("0", (await world.Service().GetInstructionDetailAsync(id)).Lines[0].Delivered);
    }

    [Theory]
    [InlineData("01")]
    [InlineData(" 4")]
    [InlineData("9223372036854775808")]
    public void Realization_wire_rejects_noncanonical_versions(string version) =>
        Assert.Throws<JsonException>(() => LogisticsRealizationJson.ReadShipment(JsonSerializer.Serialize(
            Shipment() with { ExpectedVersion = version }, LogisticsJson.Options)));

    [Fact]
    public void Realization_wire_is_strict_and_never_accepts_client_company_or_stock_position()
    {
        var json = JsonSerializer.Serialize(Shipment(), LogisticsJson.Options);
        Assert.Equal(Shipment().ExpectedVersion, LogisticsRealizationJson.ReadShipment(json).ExpectedVersion);
        Assert.Throws<JsonException>(() => LogisticsRealizationJson.ReadShipment(json[..^1] + ",\"companyId\":\"" + Company + "\"}"));
        Assert.Throws<JsonException>(() => LogisticsRealizationJson.ReadShipment(JsonSerializer.Serialize(
            Shipment() with { OccurredAtUtc = EventTime.ToOffset(TimeSpan.FromHours(3)) }, LogisticsJson.Options)));
        Assert.True(LogisticsCommands.IsMutation(LogisticsCommands.RecordShipmentV1));
        Assert.True(LogisticsCommands.IsMutation(LogisticsCommands.RecordDeliveryV1));
    }

    private static RecordShipmentRequest Shipment() => new("4", Guid.NewGuid(), Allocation, "7", "11", Guid.NewGuid(), "2", EventTime);

    private static async Task<(World World, FakeRealizationRunner Runner, Guid Id, Guid Line)> LoadedWorldAsync()
    {
        // Registered synthetic names keep this owner suite independent of the separate central registration patch.
        var world = new World { Bindings = new(CreatePermission, ReadPermission, CreatePermission, CreatePermission, ReadPermission) };
        var runner = new FakeRealizationRunner();
        var created = await world.Service().CreateInstructionAsync(Request(quantity: "10"), Metadata("create"));
        var id = created.Detail.Instruction.Id;
        var line = created.Detail.Lines[0].LineId;
        await world.Service().IssueInstructionAsync(id, new("2"), Metadata("issue"));
        await world.Service().RecordLoadAsync(id, line, new("3", Guid.NewGuid(), "5", "Measured"), Metadata("load"));
        return (world, runner, id, line);
    }

    private sealed class FakeRealizationRunner : ILogisticsRealizationTransactionRunnerV1
    {
        public FakeShipmentStock Stock { get; } = new();
        public int ShipmentRuns { get; private set; }
        public int DeliveryRuns { get; private set; }
        public int Fences { get; private set; }
        public int Commits { get; private set; }

        public async Task<T> ExecuteShipmentAsync<T>(Guid companyId, Func<ILogisticsShipmentStockV1, CancellationToken, Task<T>> operation,
            Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default)
        {
            Assert.Equal(Company, companyId);
            ShipmentRuns++;
            var result = await operation(Stock, cancellationToken);
            await beforeCommit(cancellationToken);
            Fences++;
            Commits++;
            return result;
        }

        public async Task<T> ExecuteDeliveryAsync<T>(Guid companyId, Func<CancellationToken, Task<T>> operation,
            Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default)
        {
            Assert.Equal(Company, companyId);
            DeliveryRuns++;
            var result = await operation(cancellationToken);
            await beforeCommit(cancellationToken);
            Fences++;
            Commits++;
            return result;
        }
    }

    private sealed class FakeShipmentStock : ILogisticsShipmentStockV1
    {
        public List<LogisticsShipmentStockRequestV1> Requests { get; } = [];
        public string? AllocationFault { get; set; }
        public bool ForceReplay { get; set; }
        public string? TerminalFailureCode { get; set; }
        public Action? BeforeTerminalFailure { get; set; }
        public Action? AfterStage { get; set; }

        public Task<LogisticsShipmentAllocationV1?> FindAllocationAsync(Guid salesOrderId, Guid salesOrderLineId, Guid allocationId,
            CancellationToken cancellationToken = default)
        {
            if (TerminalFailureCode is not null)
            {
                BeforeTerminalFailure?.Invoke();
                throw new LogisticsInstructionServiceException(409, TerminalFailureCode);
            }

            return Task.FromResult<LogisticsShipmentAllocationV1?>(AllocationFault == "missing" ? null :
                new(AllocationFault == "company" ? Guid.NewGuid() : Company, allocationId, Label, salesOrderId,
                    AllocationFault == "order-line" ? Guid.NewGuid() : salesOrderLineId, AllocationFault == "product" ? Guid.NewGuid() : Product));
        }

        public Task<LogisticsShipmentStockResultV1> StageAsync(LogisticsShipmentStockRequestV1 request, LogisticsWriteEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            AfterStage?.Invoke();
            return Task.FromResult(new LogisticsShipmentStockResultV1(request.CompanyId, request.ShipmentId, request.AllocationId,
                request.LabelId, request.MovementId, Guid.NewGuid(), ForceReplay, envelope.CorrelationId));
        }
    }
}
