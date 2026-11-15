using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Domain.Transfers;
using GoymenERP.Modules.Inventory.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// B-22c transfer receipt (KLN-D21 shortage fire, KLN-D24 excess maker-checker, KLN-D23 pending-label loss). The domain
/// facts, the permission names and the fenced command service. The maker≠approver comparison is the store's (stored
/// recorder against the authorized actor) and is proved there, not by these doubles.
/// </summary>
public sealed partial class InventoryTransferTests
{
    private const string ReceiptLineId = "71000000-0000-0000-0000-000000000001";
    private const string ReceiptLabelId = "81000000-0000-0000-0000-000000000001";

    [Fact]
    public void Receiving_the_dispatched_kilograms_accepts_the_whole_label_without_a_shortage_or_an_excess()
    {
        var line = Planned(ReceiptLineId, ReceiptLabelId, 10m);
        var transfer = Transfer(line);
        transfer.Dispatch(1, At.AddHours(1), Document("outgoing"), [Actual(line, Balance(line.LabelId, 10m))]);

        var receipt = transfer.ReceiveLine(2, line.LineId, At.AddHours(2), Document("incoming"), Kilograms.Positive(10m));

        Assert.Equal((3L, InventoryTransferStatus.Accepted), (transfer.Version, transfer.Status));
        Assert.Null(receipt.Shortage);
        Assert.Equal((10m, 10m, 0m, 0m), (receipt.DispatchedQuantity.Value, receipt.ReceivedQuantity.Value,
            receipt.ShortageQuantity.Value, receipt.OverageQuantity.Value));
        Assert.Equal(StockMovementKind.TransferAccept, receipt.Acceptance.Movement(Guid.NewGuid(), Company).Kind);
        Assert.Equal(InventoryTransferLineStatus.Accepted, Assert.Single(transfer.Lines).Status);
    }

    [Fact]
    public void A_receipt_shortage_accepts_the_whole_label_then_fires_the_missing_kilograms_out_of_the_destination()
    {
        var line = Planned(ReceiptLineId, ReceiptLabelId, 10m);
        var source = Balance(line.LabelId, 10m);
        var ledger = StockLedger.FromStoredBalances([source]);
        var transfer = Transfer(line);
        ledger.Apply(transfer.Dispatch(1, At.AddHours(1), Document("outgoing"), [Actual(line, source)])
            .Movement(Guid.NewGuid(), Company));

        var receipt = transfer.ReceiveLine(2, line.LineId, At.AddHours(2), Document("incoming"),
            Kilograms.Positive(8.5m));
        var acceptance = receipt.Acceptance.Movement(Guid.NewGuid(), Company);
        var shortage = receipt.Shortage!.Movement(Guid.NewGuid(), Company);
        ledger.Apply(acceptance);
        ledger.Apply(shortage);

        Assert.Equal((1.5m, 0m), (receipt.ShortageQuantity.Value, receipt.OverageQuantity.Value));
        Assert.Equal(StockMovementKind.TransferReceiptShortage, shortage.Kind);
        var fire = Assert.Single(shortage.Lines);
        Assert.Equal((StockDirection.Out, 1.5m), (fire.Direction, fire.Quantity.Value));
        Assert.Equal(Assert.Single(acceptance.Lines).Position, fire.Position);
        Assert.Equal(Destination, fire.Position.LocationId);
        Assert.Equal(new StockCustodian(StockPartyKind.Counterparty, TargetCustodian), fire.Position.Custodian);
        Assert.Equal(new StockSourceReference(InventoryTransfer.SourceKind, transfer.Id, line.LineId), shortage.Source);
        var balance = Assert.Single(ledger.Balances);
        Assert.Equal((Destination, 8.5m), (balance.Position.LocationId, balance.Quantity.Value));
        Assert.Equal((3L, InventoryTransferStatus.Accepted), (transfer.Version, transfer.Status));
    }

    [Fact]
    public void A_receipt_excess_is_only_reported_and_enters_the_destination_bucket_only_through_the_approved_effect()
    {
        var line = Planned(ReceiptLineId, ReceiptLabelId, 10m);
        var source = Balance(line.LabelId, 10m, BlockState.Blocked);
        var ledger = StockLedger.FromStoredBalances([source]);
        var transfer = Transfer(line);
        ledger.Apply(transfer.Dispatch(1, At.AddHours(1), Document("outgoing"), [Actual(line, source)])
            .Movement(Guid.NewGuid(), Company));

        var receipt = transfer.ReceiveLine(2, line.LineId, At.AddHours(2), Document("incoming"), Kilograms.Positive(12m));
        ledger.Apply(receipt.Acceptance.Movement(Guid.NewGuid(), Company));

        Assert.Null(receipt.Shortage);
        Assert.Equal((0m, 2m), (receipt.ShortageQuantity.Value, receipt.OverageQuantity.Value));
        Assert.Equal(10m, Assert.Single(ledger.Balances).Quantity.Value);
        AssertInvalid(() => transfer.ReceiptOverage(line.LineId, Kilograms.Positive(2m), At.AddHours(1)));
        AssertInvalid(() => transfer.ReceiptOverage(line.LineId, Kilograms.Zero, At.AddHours(3)));
        AssertInvalid(() => transfer.ReceiptOverage(Guid.NewGuid(), Kilograms.Positive(2m), At.AddHours(3)));

        var overage = transfer.ReceiptOverage(line.LineId, Kilograms.Positive(2m), At.AddHours(3))
            .Movement(Guid.NewGuid(), Company);
        ledger.Apply(overage);

        Assert.Equal(StockMovementKind.TransferReceiptOverage, overage.Kind);
        var excess = Assert.Single(overage.Lines);
        Assert.Equal((StockDirection.In, 2m, BlockState.Blocked), (excess.Direction, excess.Quantity.Value,
            excess.Position.Block));
        Assert.Equal(Destination, excess.Position.LocationId);
        var balance = Assert.Single(ledger.Balances);
        Assert.Equal((Destination, 12m), (balance.Position.LocationId, balance.Quantity.Value));
        Assert.Equal(3L, transfer.Version);
    }

    [Fact]
    public void A_difference_on_a_label_dispatched_in_two_block_buckets_is_refused_without_changing_the_transfer()
    {
        var line = Planned(ReceiptLineId, ReceiptLabelId, 10m);
        var transfer = Transfer(line);
        transfer.Dispatch(1, At.AddHours(1), Document("outgoing"),
            [Actual(line, Balance(line.LabelId, 7m), Balance(line.LabelId, 3m, BlockState.Blocked))]);

        AssertInvalid(() => transfer.ReceiveLine(2, line.LineId, At.AddHours(2), Document("incoming"),
            Kilograms.Positive(9m)));
        AssertInvalid(() => transfer.ReceiveLine(2, line.LineId, At.AddHours(2), Document("incoming"),
            Kilograms.Positive(11m)));
        AssertInvalid(() => transfer.ReceiveLine(2, line.LineId, At.AddHours(2), Document("incoming"), Kilograms.Zero));
        Assert.Equal((2L, InventoryTransferStatus.InTransit), (transfer.Version, transfer.Status));

        var exact = transfer.ReceiveLine(2, line.LineId, At.AddHours(2), Document("incoming"), Kilograms.Positive(10m));

        Assert.Null(exact.Shortage);
        Assert.Equal(2, exact.Acceptance.Line.Buckets.Count);
        AssertInvalid(() => transfer.ReceiptOverage(line.LineId, Kilograms.Positive(1m), At.AddHours(3)));
    }

    [Fact]
    public void Writing_off_the_last_pending_line_closes_the_transfer_with_a_net_zero_source_loss_movement()
    {
        var first = Planned(ReceiptLineId, ReceiptLabelId, 10m);
        var second = Planned("71000000-0000-0000-0000-000000000002", "81000000-0000-0000-0000-000000000002", 5m);
        var firstSource = Balance(first.LabelId, 10m);
        var secondFree = Balance(second.LabelId, 4m);
        var secondBlocked = Balance(second.LabelId, 1m, BlockState.Blocked);
        var ledger = StockLedger.FromStoredBalances([firstSource, secondFree, secondBlocked]);
        var transfer = Transfer(first, second);
        ledger.Apply(transfer.Dispatch(1, At.AddHours(1), Document("outgoing"),
            [Actual(first, firstSource), Actual(second, secondFree, secondBlocked)]).Movement(Guid.NewGuid(), Company));
        ledger.Apply(transfer.ReceiveLine(2, first.LineId, At.AddHours(2), Document("incoming"),
            Kilograms.Positive(10m)).Acceptance.Movement(Guid.NewGuid(), Company));
        var beforeLoss = ledger.Balances;

        AssertInvalid(() => transfer.WriteOffLine(2, second.LineId, At.AddHours(3)));
        AssertInvalid(() => transfer.WriteOffLine(3, second.LineId, At));
        AssertInvalid(() => transfer.WriteOffLine(3, first.LineId, At.AddHours(3)));
        AssertInvalid(() => transfer.WriteOffLine(3, Guid.NewGuid(), At.AddHours(3)));
        var loss = transfer.WriteOffLine(3, second.LineId, At.AddHours(3));
        var movement = loss.Movement(Guid.NewGuid(), Company);
        ledger.Apply(movement);

        Assert.Equal((4L, InventoryTransferStatus.Closed), (transfer.Version, transfer.Status));
        Assert.Equal(4L, loss.TransferVersion);
        Assert.Equal(InventoryTransferLineStatus.WrittenOff,
            transfer.Lines.Single(line => line.LineId == second.LineId).Status);
        Assert.Equal(StockMovementKind.TransferTransitLoss, movement.Kind);
        Assert.Equal(new[] { StockDirection.In, StockDirection.Out, StockDirection.In, StockDirection.Out },
            movement.Lines.Select(line => line.Direction));
        Assert.Equal(new[] { 4m, 4m, 1m, 1m }, movement.Lines.Select(line => line.Quantity.Value));
        Assert.All(movement.Lines, line => Assert.Equal(Source, line.Position.LocationId));
        Assert.Equal(beforeLoss, ledger.Balances);
        AssertInvalid(() => transfer.ReceiveLine(4, second.LineId, At.AddHours(4), Document("incoming-2"),
            Kilograms.Positive(5m)));
        AssertInvalid(() => transfer.WriteOffLine(4, second.LineId, At.AddHours(4)));
    }

    [Fact]
    public void A_transfer_whose_only_lines_are_written_off_is_closed_and_a_transit_loss_must_pair_on_one_position()
    {
        var line = Planned(ReceiptLineId, ReceiptLabelId, 10m);
        var source = Balance(line.LabelId, 10m);
        var transfer = Transfer(line);
        transfer.Dispatch(1, At.AddHours(1), Document("outgoing"), [Actual(line, source)]);

        transfer.WriteOffLine(2, line.LineId, At.AddHours(2));

        Assert.Equal((3L, InventoryTransferStatus.Closed), (transfer.Version, transfer.Status));
        var other = Balance(line.LabelId, 10m, location: Destination);
        var refusal = Assert.Throws<InventoryRuleException>(() => new StockMovement(Guid.NewGuid(), Company,
            StockMovementKind.TransferTransitLoss, At.AddHours(2),
            new StockSourceReference(InventoryTransfer.SourceKind, transfer.Id, line.LineId),
            [
                new StockMovementLine(source.Position, StockDirection.In, source.Quantity),
                new StockMovementLine(other.Position, StockDirection.Out, other.Quantity),
            ]));
        Assert.Equal(InventoryRuleCodes.InvalidMovement, refusal.Code);
        Assert.Equal((12, 13, 14), ((int)StockMovementKind.TransferReceiptShortage,
            (int)StockMovementKind.TransferReceiptOverage, (int)StockMovementKind.TransferTransitLoss));
        Assert.Equal((5, 6), ((int)InventoryTransferLineStatus.WrittenOff, (int)InventoryTransferStatus.Closed));
    }

    [Fact]
    public void Receipt_excess_and_loss_permissions_are_registered_company_wide_and_separate_without_critical_marking()
    {
        var names = new[]
        {
            (InventoryPermissionManifestV1.StockTransferOverageApprove, ErpPermissions.InventoryStockTransferOverageApprove),
            (InventoryPermissionManifestV1.StockTransferLossRecord, ErpPermissions.InventoryStockTransferLossRecord),
            (InventoryPermissionManifestV1.StockTransferLossApprove, ErpPermissions.InventoryStockTransferLossApprove),
        };

        Assert.Equal(
            ["inventory.stock-transfer.overage-approve", "inventory.stock-transfer.loss-record",
                "inventory.stock-transfer.loss-approve"],
            names.Select(name => name.Item1));
        Assert.All(names, name =>
        {
            Assert.Equal(name.Item1, name.Item2);
            Assert.True(ErpPermissions.IsKnown(name.Item2));
            Assert.True(ErpPermissions.RequiresCompanyWideScope(name.Item2));
            Assert.False(ErpPermissions.IsCritical(name.Item2));
            Assert.NotEqual(ErpPermissions.InventoryStockTransferWrite, name.Item2);
        });
        Assert.Equal(names.Length, names.Select(name => name.Item2).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Owner_registration_adds_the_receipt_command_and_query_but_no_destination_adapter_or_grant()
    {
        var services = new ServiceCollection().AddInventoryTransfers();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInventoryTransferReceiptWriterV1));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInventoryTransferReceiptReaderV1));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInventoryTransferReceiptCommandV1)
            && descriptor.ImplementationType == typeof(InventoryTransferReceiptCommandService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInventoryTransferReceiptQueryV1)
            && descriptor.ImplementationType == typeof(InventoryTransferReceiptQueryService));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IInventoryTransferDestinationPortV1));
    }

    [Fact]
    public void Receipt_requests_are_strict_and_free_text_is_canonical()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var receive = """
            {"expectedTransferVersion":"2","receivedAtUtc":"2026-09-30T14:00:00Z","incomingDocument":{"kind":"inventory.transfer.incoming","id":"7a000000-0000-4000-8000-000000000001","lineId":null},"receivedKg":"9.5","overageReason":null,"shortageKg":"0.5"}
            """;
        var decisionWithoutReason = """
            {"decision":"Approve","decidedAtUtc":"2026-09-30T15:00:00Z"}
            """;
        var decision = """
            {"decision":"Reject","decidedAtUtc":"2026-09-30T15:00:00Z","reason":"SENTETIK tartı tekrarı"}
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReceiveInventoryTransferLineV1>(receive, web));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<DecideInventoryTransferOverageV1>(decisionWithoutReason, web));
        Assert.Equal(InventoryTransferDecisionKindV1.Reject,
            JsonSerializer.Deserialize<DecideInventoryTransferOverageV1>(decision, web)!.Decision);
        Assert.Equal("SENTETIK açıklama", InventoryTransferReceiptApiV1.NormalizeText("  SENTETIK açıklama "));
        Assert.Null(InventoryTransferReceiptApiV1.NormalizeText(" "));
        Assert.Null(InventoryTransferReceiptApiV1.NormalizeText(null));
        Assert.Null(InventoryTransferReceiptApiV1.NormalizeText("satır\nkırılımı"));
        Assert.Null(InventoryTransferReceiptApiV1.NormalizeText(
            new string('a', InventoryTransferReceiptApiV1.MaximumTextLength + 1)));
        Assert.NotNull(InventoryTransferReceiptApiV1.NormalizeText(
            new string('a', InventoryTransferReceiptApiV1.MaximumTextLength)));
    }

    [Theory]
    [InlineData("receive", ErpPermissions.InventoryStockTransferWrite)]
    [InlineData("overage", ErpPermissions.InventoryStockTransferOverageApprove)]
    [InlineData("loss-request", ErpPermissions.InventoryStockTransferLossRecord)]
    [InlineData("loss-decision", ErpPermissions.InventoryStockTransferLossApprove)]
    public async Task Each_receipt_command_is_measured_and_written_only_under_its_own_permission(
        string operation,
        string permission)
    {
        var world = new ReceiptWorld();

        await Invoke(world.Service(), operation, Transport("actor"));

        Assert.NotEmpty(world.Authority.Measured);
        Assert.All(world.Authority.Measured, measured => Assert.Equal(permission, measured));
        Assert.Equal([operation], world.Writer.ReplayCalls);
        var (written, metadata) = Assert.Single(world.Writer.Calls);
        Assert.Equal((operation, "actor", permission, "key-1", "corr-1"),
            (written, metadata.ActorId, metadata.PermissionCode, metadata.IdempotencyKey, metadata.CorrelationId));
        var receives = operation == "receive" ? 1 : 0;
        Assert.Equal((receives, receives), (world.Reader.Calls, world.Destination.Calls));
    }

    [Theory]
    [InlineData("receive")]
    [InlineData("overage")]
    [InlineData("loss-request")]
    [InlineData("loss-decision")]
    public async Task A_stored_receipt_answer_replays_before_the_transfer_or_destination_read(string operation)
    {
        var replay = new InventoryTransferReceiptResultV1(TransferView(Company), ReceiptFacts(), true, "original");
        var world = new ReceiptWorld();
        world.Writer.ReplayResult = replay;
        world.Destination.Status = InventoryTransferDestinationStatusV1.Inactive;

        var answer = await Invoke(world.Service(), operation, Transport("actor"));

        Assert.Same(replay, answer);
        Assert.Empty(world.Writer.Calls);
        Assert.Equal((0, 0), (world.Reader.Calls, world.Destination.Calls));
    }

    [Theory]
    [InlineData("receive")]
    [InlineData("overage")]
    [InlineData("loss-request")]
    [InlineData("loss-decision")]
    public async Task An_unregistered_receipt_permission_is_a_setup_gap_and_never_measured_or_written(string operation)
    {
        var world = new ReceiptWorld { Known = false };

        await Refused(Invoke(world.Service(), operation, Transport("actor")), 503,
            InventoryTransferCodesV1.PermissionUnregistered);

        Assert.Empty(world.Authority.Measured);
        Assert.Empty(world.Writer.ReplayCalls);
        Assert.Empty(world.Writer.Calls);
    }

    [Theory]
    [InlineData("receive")]
    [InlineData("overage")]
    [InlineData("loss-request")]
    [InlineData("loss-decision")]
    public async Task A_missing_grant_or_another_transport_actor_is_forbidden_before_the_writer(string operation)
    {
        var revoked = new ReceiptWorld();
        revoked.Authority.Granted = false;
        var impersonated = new ReceiptWorld();

        await Refused(Invoke(revoked.Service(), operation, Transport("actor")), 403, InventoryTransferCodesV1.Forbidden);
        await Refused(Invoke(impersonated.Service(), operation, Transport("someone-else")), 403,
            InventoryTransferCodesV1.Forbidden);

        Assert.Empty(revoked.Writer.ReplayCalls);
        Assert.Empty(impersonated.Writer.ReplayCalls);
        Assert.Empty(revoked.Writer.Calls);
        Assert.Empty(impersonated.Writer.Calls);
    }

    [Fact]
    public async Task Revocation_at_the_receipt_writer_fence_refuses_without_recording_the_receipt()
    {
        var world = new ReceiptWorld();
        world.Writer.BeforeFence = () => world.Authority.Granted = false;

        await Refused(Invoke(world.Service(), "receive", Transport("actor")), 403, InventoryTransferCodesV1.Forbidden);

        Assert.Empty(world.Writer.Calls);
    }

    [Theory]
    [InlineData(InventoryTransferDestinationStatusV1.NotFound, InventoryTransferCodesV1.DestinationNotFound)]
    [InlineData(InventoryTransferDestinationStatusV1.Inactive, InventoryTransferCodesV1.DestinationInactive)]
    [InlineData(InventoryTransferDestinationStatusV1.ContextMismatch,
        InventoryTransferCodesV1.DestinationContextMismatch)]
    public async Task A_receipt_rechecks_the_stored_destination_and_refuses_an_unusable_one_before_writing(
        InventoryTransferDestinationStatusV1 status,
        string code)
    {
        var world = new ReceiptWorld();
        world.Destination.Status = status;

        await Refused(Invoke(world.Service(), "receive", Transport("actor")), 422, code);

        Assert.Equal((1, 1), (world.Reader.Calls, world.Destination.Calls));
        Assert.Empty(world.Writer.Calls);
    }

    [Fact]
    public async Task A_receipt_of_a_missing_or_foreign_transfer_is_refused_before_the_destination_or_writer()
    {
        var missing = new ReceiptWorld();
        missing.Reader.Result = null;
        var foreign = new ReceiptWorld();
        foreign.Reader.Result = TransferView(Guid.NewGuid());

        await Refused(Invoke(missing.Service(), "receive", Transport("actor")), 404,
            InventoryTransferCodesV1.TransferNotFound);
        await Refused(Invoke(foreign.Service(), "receive", Transport("actor")), 503,
            InventoryTransferCodesV1.Unavailable);

        Assert.Equal((0, 0), (missing.Destination.Calls, foreign.Destination.Calls));
        Assert.Empty(missing.Writer.Calls);
        Assert.Empty(foreign.Writer.Calls);
    }

    [Fact]
    public async Task Receipt_facts_are_read_under_the_balances_read_permission_and_refuse_a_foreign_answer()
    {
        var authority = new TestAuthority();
        var stored = ReceiptFacts();
        var reader = new TestReceiptReader { Result = stored };
        var query = new InventoryTransferReceiptQueryService(new TestContext(), new TestUser(), authority, reader);

        var facts = await query.GetReceiptFactsAsync(stored.TransferId);

        Assert.Same(stored, facts);
        Assert.Equal(2, authority.Measured.Count);
        Assert.All(authority.Measured, measured => Assert.Equal(ErpPermissions.InventoryBalancesRead, measured));

        reader.Result = stored with { CompanyId = Guid.NewGuid() };
        await Refused(query.GetReceiptFactsAsync(stored.TransferId), 503, InventoryTransferCodesV1.Unavailable);
        reader.Result = null;
        await Refused(query.GetReceiptFactsAsync(Guid.NewGuid()), 404, InventoryTransferCodesV1.TransferNotFound);
        authority.Granted = false;
        await Refused(query.GetReceiptFactsAsync(Guid.NewGuid()), 403, InventoryTransferCodesV1.Forbidden);
        Assert.Equal(3, reader.Calls);
    }

    private static Task<InventoryTransferReceiptResultV1> Invoke(
        InventoryTransferReceiptCommandService service,
        string operation,
        InventoryCommandMetadata transport)
    {
        var transferId = Guid.Parse("91000000-0000-0000-0000-000000000001");
        var lineId = Guid.Parse(ReceiptLineId);
        return operation switch
        {
            "receive" => service.ReceiveLineAsync(transferId, lineId,
                new ReceiveInventoryTransferLineV1("2", At.AddHours(2),
                    new("inventory.transfer.incoming", Guid.NewGuid(), null), "9.5", null), transport),
            "overage" => service.DecideOverageAsync(transferId, lineId,
                new DecideInventoryTransferOverageV1(InventoryTransferDecisionKindV1.Approve, At.AddHours(3),
                    "SENTETIK tartı onayı"), transport),
            "loss-request" => service.RequestLossAsync(transferId, lineId,
                new RequestInventoryTransferLossV1("2", At.AddHours(3), "SENTETIK yolda kayıp"), transport),
            "loss-decision" => service.DecideLossAsync(transferId, lineId,
                new DecideInventoryTransferLossV1("2", InventoryTransferDecisionKindV1.Approve, At.AddHours(4),
                    "SENTETIK kayıp onayı"), transport),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };
    }

    private static InventoryTransferReceiptFactsViewV1 ReceiptFacts() =>
        new(Guid.Parse("91000000-0000-0000-0000-000000000001"), Company, "3", "Accepted", [], []);

    private sealed class ReceiptWorld
    {
        public TestContext Context { get; } = new();
        public TestUser User { get; } = new();
        public TestAuthority Authority { get; } = new();
        public TestDestination Destination { get; } = new();
        public TestReader Reader { get; } = new() { Result = TransferView(Company) };
        public TestReceiptWriter Writer { get; } = new();
        public bool Known { get; init; } = true;

        public InventoryTransferReceiptCommandService Service() =>
            new(Context, User, Authority, Destination, Reader, Writer, _ => Known);
    }

    private sealed class TestReceiptReader : IInventoryTransferReceiptReaderV1
    {
        public InventoryTransferReceiptFactsViewV1? Result { get; set; }
        public int Calls { get; private set; }

        public Task<InventoryTransferReceiptFactsViewV1?> ReadReceiptFactsAsync(
            Guid transferId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class TestReceiptWriter : IInventoryTransferReceiptWriterV1
    {
        private readonly InventoryTransferReceiptResultV1 result =
            new(TransferView(Company), ReceiptFacts(), false, "corr-1");

        public List<(string Operation, InventoryCommandMetadata Metadata)> Calls { get; } = [];
        public List<string> ReplayCalls { get; } = [];
        public Action? BeforeFence { get; set; }
        public InventoryTransferReceiptResultV1? ReplayResult { get; set; }

        public Task<InventoryTransferReceiptResultV1?> ReplayReceiveLineAsync(Guid transferId, Guid lineId,
            ReceiveInventoryTransferLineV1 request, InventoryCommandMetadata metadata,
            Func<CancellationToken, Task> freshAuthorityFence, CancellationToken cancellationToken = default) =>
            Replay("receive", freshAuthorityFence, cancellationToken);

        public Task<InventoryTransferReceiptResultV1> ReceiveLineAsync(Guid transferId, Guid lineId,
            ReceiveInventoryTransferLineV1 request, InventoryCommandMetadata metadata,
            Func<CancellationToken, Task> freshAuthorityFence, CancellationToken cancellationToken = default) =>
            Answer("receive", metadata, freshAuthorityFence, cancellationToken);

        public Task<InventoryTransferReceiptResultV1?> ReplayDecideOverageAsync(Guid transferId, Guid lineId,
            DecideInventoryTransferOverageV1 request, InventoryCommandMetadata metadata,
            Func<CancellationToken, Task> freshAuthorityFence, CancellationToken cancellationToken = default) =>
            Replay("overage", freshAuthorityFence, cancellationToken);

        public Task<InventoryTransferReceiptResultV1> DecideOverageAsync(Guid transferId, Guid lineId,
            DecideInventoryTransferOverageV1 request, InventoryCommandMetadata metadata,
            Func<CancellationToken, Task> freshAuthorityFence, CancellationToken cancellationToken = default) =>
            Answer("overage", metadata, freshAuthorityFence, cancellationToken);

        public Task<InventoryTransferReceiptResultV1?> ReplayRequestLossAsync(Guid transferId, Guid lineId,
            RequestInventoryTransferLossV1 request, InventoryCommandMetadata metadata,
            Func<CancellationToken, Task> freshAuthorityFence, CancellationToken cancellationToken = default) =>
            Replay("loss-request", freshAuthorityFence, cancellationToken);

        public Task<InventoryTransferReceiptResultV1> RequestLossAsync(Guid transferId, Guid lineId,
            RequestInventoryTransferLossV1 request, InventoryCommandMetadata metadata,
            Func<CancellationToken, Task> freshAuthorityFence, CancellationToken cancellationToken = default) =>
            Answer("loss-request", metadata, freshAuthorityFence, cancellationToken);

        public Task<InventoryTransferReceiptResultV1?> ReplayDecideLossAsync(Guid transferId, Guid lineId,
            DecideInventoryTransferLossV1 request, InventoryCommandMetadata metadata,
            Func<CancellationToken, Task> freshAuthorityFence, CancellationToken cancellationToken = default) =>
            Replay("loss-decision", freshAuthorityFence, cancellationToken);

        public Task<InventoryTransferReceiptResultV1> DecideLossAsync(Guid transferId, Guid lineId,
            DecideInventoryTransferLossV1 request, InventoryCommandMetadata metadata,
            Func<CancellationToken, Task> freshAuthorityFence, CancellationToken cancellationToken = default) =>
            Answer("loss-decision", metadata, freshAuthorityFence, cancellationToken);

        private async Task<InventoryTransferReceiptResultV1> Answer(string operation,
            InventoryCommandMetadata metadata, Func<CancellationToken, Task> freshAuthorityFence,
            CancellationToken cancellationToken)
        {
            BeforeFence?.Invoke();
            await freshAuthorityFence(cancellationToken);
            Calls.Add((operation, metadata));
            return result;
        }

        private async Task<InventoryTransferReceiptResultV1?> Replay(string operation,
            Func<CancellationToken, Task> freshAuthorityFence, CancellationToken cancellationToken)
        {
            ReplayCalls.Add(operation);
            if (ReplayResult is null) return null;
            await freshAuthorityFence(cancellationToken);
            return ReplayResult;
        }
    }
}
