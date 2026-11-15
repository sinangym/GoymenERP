using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Domain.Transfers;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>B-22b dispatch cancellation (KLN-D20/D25/D27) and the LOJ-D11 setting stored at dispatch.</summary>
public sealed partial class InventoryTransferTests
{
    private static readonly Guid CancelledTransfer = Guid.Parse("90000000-0000-0000-0000-000000000002");
    // A property, not a field: field initializers of different partial parts run in an unspecified order.
    private static DateTimeOffset DispatchedAt => At.AddHours(1);

    [Fact]
    public void Cancelling_a_dispatch_returns_every_bucket_to_its_exact_source_position_with_one_inbound_movement()
    {
        var first = Planned("70000000-0000-0000-0000-000000000001", "80000000-0000-0000-0000-000000000001", 10m);
        var second = Planned("70000000-0000-0000-0000-000000000002", "80000000-0000-0000-0000-000000000002", 4m);
        var firstFree = Balance(first.LabelId, 7m);
        var firstBlocked = Balance(first.LabelId, 3m, BlockState.Blocked);
        var secondFree = Balance(second.LabelId, 4m);
        var ledger = StockLedger.FromStoredBalances([firstFree, firstBlocked, secondFree]);
        var transfer = Transfer(first, second);
        var dispatch = transfer.Dispatch(1, DispatchedAt, Document("outgoing"),
            [Actual(first, firstFree, firstBlocked), Actual(second, secondFree)]);
        ledger.Apply(dispatch.Movement(Guid.NewGuid(), Company));
        Assert.Empty(ledger.Balances);

        var cancellation = transfer.CancelDispatch(2, DispatchedAt.AddMinutes(30));
        var movement = cancellation.Movement(Guid.NewGuid(), Company);
        ledger.Apply(movement);

        Assert.Equal((3L, InventoryTransferStatus.DispatchCancelled), (transfer.Version, transfer.Status));
        Assert.Equal(DispatchedAt.AddMinutes(30), transfer.DispatchCancelledAtUtc);
        Assert.All(transfer.Lines, line => Assert.Equal(InventoryTransferLineStatus.DispatchCancelled, line.Status));
        Assert.Equal(StockMovementKind.TransferDispatchCancellation, movement.Kind);
        Assert.Equal(11, (int)StockMovementKind.TransferDispatchCancellation);
        Assert.All(movement.Lines, line => Assert.Equal(StockDirection.In, line.Direction));
        Assert.Equal(
            new[] { firstFree, firstBlocked, secondFree }
                .Select(balance => (balance.Position, balance.Quantity.Value))
                .OrderBy(item => item.Position.LabelId).ThenBy(item => item.Position.Block),
            ledger.Balances.Select(balance => (balance.Position, balance.Quantity.Value))
                .OrderBy(item => item.Position.LabelId).ThenBy(item => item.Position.Block));

        AssertInvalid(() => transfer.AcceptLine(3, first.LineId, DispatchedAt.AddHours(1), Document("incoming")));
        AssertInvalid(() => transfer.CancelDispatch(3, DispatchedAt.AddHours(1)));
    }

    [Theory]
    [InlineData("planned")]
    [InlineData("stale-version")]
    [InlineData("before-dispatch")]
    [InlineData("not-utc")]
    [InlineData("partially-accepted")]
    public void Dispatch_cancellation_refuses_an_unsent_partly_accepted_stale_or_out_of_order_cancel(string fault)
    {
        var first = Planned("70000000-0000-0000-0000-000000000001", "80000000-0000-0000-0000-000000000001", 10m);
        var second = Planned("70000000-0000-0000-0000-000000000002", "80000000-0000-0000-0000-000000000002", 5m);
        var transfer = Transfer(first, second);
        if (fault != "planned")
        {
            transfer.Dispatch(1, DispatchedAt, Document("outgoing"),
                [Actual(first, Balance(first.LabelId, 10m)), Actual(second, Balance(second.LabelId, 5m))]);
        }

        if (fault == "partially-accepted")
        {
            transfer.AcceptLine(2, first.LineId, DispatchedAt.AddHours(1), Document("incoming"));
        }

        var (version, status) = (transfer.Version, transfer.Status);
        AssertInvalid(() => transfer.CancelDispatch(
            fault == "stale-version" ? 1 : transfer.Version,
            fault switch
            {
                "before-dispatch" => DispatchedAt.AddMinutes(-1),
                "not-utc" => DispatchedAt.AddHours(1).ToOffset(TimeSpan.FromHours(3)),
                _ => DispatchedAt.AddHours(1),
            }));

        Assert.Equal((version, status), (transfer.Version, transfer.Status));
        Assert.Null(transfer.DispatchCancelledAtUtc);
    }

    [Fact]
    public void A_stored_cancellation_restores_only_without_acceptances_and_with_the_stored_version()
    {
        var line = Planned("70000000-0000-0000-0000-000000000001", "80000000-0000-0000-0000-000000000001", 10m);
        var dispatched = new[] { Actual(line, Balance(line.LabelId, 10m)) };
        var outgoing = Document("outgoing");

        var restored = Restore(3, InventoryTransferStatus.DispatchCancelled, dispatched, outgoing, [],
            DispatchedAt.AddHours(1));

        Assert.Equal((3L, InventoryTransferStatus.DispatchCancelled), (restored.Version, restored.Status));
        Assert.Equal(DispatchedAt.AddHours(1), restored.DispatchCancelledAtUtc);
        AssertInvalid(() => Restore(2, InventoryTransferStatus.InTransit, dispatched, outgoing, [],
            DispatchedAt.AddHours(1)));
        AssertInvalid(() => Restore(4, InventoryTransferStatus.DispatchCancelled, dispatched, outgoing,
            [new(line.LineId, DispatchedAt.AddHours(2), Document("incoming"))], DispatchedAt.AddHours(1)));
    }

    [Fact]
    public async Task Open_scale_window_cancels_with_the_fresh_logistics_confirmation_only_after_the_final_fence()
    {
        var world = new CommandWorld().Cancellable(InventoryTransferSourceScaleSetting.On);
        world.ScaleSetting.Value = InventoryTransferScaleSettingV1.On;

        await world.Service().CancelDispatchAsync(CancelledTransfer, CancelRequest(), Transport("actor"));

        var query = Assert.Single(world.CancelGate.Reads);
        Assert.Equal((Company, CancelledTransfer, Source, DispatchedAt),
            (query.CompanyId, query.TransferId, query.SourceLocationId, query.DispatchedAtUtc));
        Assert.Equal((true, false), Assert.Single(world.CancelGate.ConfirmRequests));
        Assert.Equal((Company, Source), Assert.Single(world.ScaleSetting.Reads));
        Assert.Equal(((long?)3L, (long?)null), Assert.Single(world.Writer.CancelConfirmations));
        var call = Assert.Single(world.Writer.Calls);
        Assert.Equal(("cancel", ErpPermissions.InventoryStockTransferCancel), (call.Operation, call.Metadata.PermissionCode));
        Assert.Empty(world.Writer.RecordedClosures);
        Assert.NotEmpty(world.Authority.Measured);
        Assert.All(world.Authority.Measured,
            permission => Assert.Equal(ErpPermissions.InventoryStockTransferCancel, permission));
    }

    [Fact]
    public async Task A_linked_dispatch_on_the_scale_path_needs_both_confirmations_and_stores_both_versions()
    {
        var world = new CommandWorld().Cancellable(InventoryTransferSourceScaleSetting.On);
        world.ScaleSetting.Value = InventoryTransferScaleSettingV1.Off;
        world.CancelGate.Facts = Facts(InventoryTransferEDispatchLinkV1.Linked, linkedAtUtc: DispatchedAt);

        await world.Service().CancelDispatchAsync(CancelledTransfer, CancelRequest(), Transport("actor"));

        Assert.Equal((true, true), Assert.Single(world.CancelGate.ConfirmRequests));
        Assert.Equal(((long?)3L, (long?)4L), Assert.Single(world.Writer.CancelConfirmations));
    }

    [Fact]
    public async Task A_linked_dispatch_off_the_scale_path_needs_only_the_e_dispatch_confirmation()
    {
        var world = new CommandWorld().Cancellable(InventoryTransferSourceScaleSetting.Off);
        world.ScaleSetting.Value = InventoryTransferScaleSettingV1.Off;
        world.CancelGate.Facts = Facts(InventoryTransferEDispatchLinkV1.Linked, linkedAtUtc: DispatchedAt);

        await world.Service().CancelDispatchAsync(CancelledTransfer, CancelRequest(), Transport("actor"));

        Assert.Equal((false, true), Assert.Single(world.CancelGate.ConfirmRequests));
        Assert.Equal(((long?)null, (long?)4L), Assert.Single(world.Writer.CancelConfirmations));
    }

    [Theory]
    [InlineData(InventoryTransferSourceScaleSetting.Off, InventoryTransferScaleSettingV1.On)]
    [InlineData(InventoryTransferSourceScaleSetting.On, InventoryTransferScaleSettingV1.Off)]
    [InlineData(InventoryTransferSourceScaleSetting.Unreadable, InventoryTransferScaleSettingV1.On)]
    public async Task An_unlinked_dispatch_with_either_setting_off_closes_at_dispatch_and_records_the_closure(
        InventoryTransferSourceScaleSetting stored,
        InventoryTransferScaleSettingV1 current)
    {
        var world = new CommandWorld().Cancellable(stored);
        world.ScaleSetting.Value = current;

        await Refused(world.Service().CancelDispatchAsync(CancelledTransfer, CancelRequest(), Transport("actor")),
            409, InventoryTransferCodesV1.CancelWindowClosed);

        Assert.Equal((InventoryTransferCancelClosureBasis.DispatchRecorded, DispatchedAt),
            Assert.Single(world.Writer.RecordedClosures));
        Assert.Equal("cancel-closure", Assert.Single(world.Writer.Calls).Operation);
        Assert.Empty(world.CancelGate.ConfirmRequests);
        Assert.Empty(world.Writer.CancelConfirmations);
    }

    [Theory]
    [InlineData("exit-weighing")]
    [InlineData("vehicle-exit")]
    [InlineData("gib-send")]
    public async Task The_earliest_departure_closes_the_window_at_its_moment_and_writes_no_movement(string departure)
    {
        var world = new CommandWorld().Cancellable(InventoryTransferSourceScaleSetting.On);
        world.ScaleSetting.Value = InventoryTransferScaleSettingV1.On;
        var early = Occurred(DispatchedAt.AddMinutes(20));
        var late = Occurred(DispatchedAt.AddMinutes(40));
        (world.CancelGate.Facts, var basis) = departure switch
        {
            "exit-weighing" => (Facts(exitWeighing: early, vehicleExit: late),
                InventoryTransferCancelClosureBasis.ExitWeighing),
            "vehicle-exit" => (Facts(exitWeighing: late, vehicleExit: early),
                InventoryTransferCancelClosureBasis.VehicleExit),
            _ => (Facts(InventoryTransferEDispatchLinkV1.Linked, DispatchedAt, gibSend: early, vehicleExit: late),
                InventoryTransferCancelClosureBasis.EDispatchSentToGib),
        };

        await Refused(world.Service().CancelDispatchAsync(CancelledTransfer, CancelRequest(), Transport("actor")),
            409, InventoryTransferCodesV1.CancelWindowClosed);

        Assert.Equal((basis, DispatchedAt.AddMinutes(20)), Assert.Single(world.Writer.RecordedClosures));
        Assert.Empty(world.CancelGate.ConfirmRequests);
        Assert.Empty(world.Writer.CancelConfirmations);
    }

    [Fact]
    public async Task A_departure_between_the_read_and_the_confirmation_closes_the_window_and_writes_no_movement()
    {
        var world = new CommandWorld().Cancellable(InventoryTransferSourceScaleSetting.On);
        world.ScaleSetting.Value = InventoryTransferScaleSettingV1.On;
        world.CancelGate.ConfirmationFacts = Facts(vehicleExit: Occurred(DispatchedAt.AddMinutes(50)));

        await Refused(world.Service().CancelDispatchAsync(CancelledTransfer, CancelRequest(), Transport("actor")),
            409, InventoryTransferCodesV1.CancelWindowClosed);

        Assert.Single(world.CancelGate.ConfirmRequests);
        Assert.Equal((InventoryTransferCancelClosureBasis.VehicleExit, DispatchedAt.AddMinutes(50)),
            Assert.Single(world.Writer.RecordedClosures));
        Assert.Empty(world.Writer.CancelConfirmations);
    }

    [Fact]
    public async Task A_persisted_closure_is_final_even_for_a_stale_request_and_no_owner_is_read_again()
    {
        var world = new CommandWorld().Cancellable(InventoryTransferSourceScaleSetting.On);
        world.Writer.CancelContext = world.Writer.CancelContext! with
        {
            ClosureBasis = InventoryTransferCancelClosureBasis.ExitWeighing,
            ClosedAtUtc = DispatchedAt.AddMinutes(15),
        };

        await Refused(world.Service().CancelDispatchAsync(CancelledTransfer, CancelRequest("9"), Transport("actor")),
            409, InventoryTransferCodesV1.CancelWindowClosed);

        Assert.Equal((InventoryTransferCancelClosureBasis.ExitWeighing, DispatchedAt.AddMinutes(15)),
            Assert.Single(world.Writer.RecordedClosures));
        Assert.Empty(world.ScaleSetting.Reads);
        Assert.Empty(world.CancelGate.Reads);
        Assert.Empty(world.Writer.CancelConfirmations);
    }

    [Theory]
    [InlineData("current-setting-failure")]
    [InlineData("current-setting-undefined")]
    [InlineData("unread-exit-weighing")]
    [InlineData("occurred-without-moment")]
    [InlineData("linked-without-moment")]
    [InlineData("null-facts")]
    public async Task Unreadable_readings_fail_closed_without_a_record_so_the_same_key_may_retry(string fault)
    {
        var world = new CommandWorld().Cancellable(InventoryTransferSourceScaleSetting.On);
        world.ScaleSetting.Value = InventoryTransferScaleSettingV1.On;
        switch (fault)
        {
            case "current-setting-failure": world.ScaleSetting.Fail = true; break;
            case "current-setting-undefined": world.ScaleSetting.Value = (InventoryTransferScaleSettingV1)99; break;
            case "unread-exit-weighing":
                world.CancelGate.Facts = Facts(exitWeighing: new(InventoryTransferDepartureFactStatusV1.Unreadable, null));
                break;
            case "occurred-without-moment":
                world.CancelGate.Facts = Facts(vehicleExit: new(InventoryTransferDepartureFactStatusV1.Occurred, null));
                break;
            case "linked-without-moment":
                world.CancelGate.Facts = Facts(InventoryTransferEDispatchLinkV1.Linked);
                break;
            default: world.CancelGate.Facts = null; break;
        }

        var (status, code) = fault == "null-facts"
            ? (503, InventoryTransferCodesV1.Unavailable)
            : (409, InventoryTransferCodesV1.CancelWindowUnreadable);
        await Refused(world.Service().CancelDispatchAsync(CancelledTransfer, CancelRequest(), Transport("actor")),
            status, code);

        Assert.Empty(world.Writer.RecordedClosures);
        Assert.Empty(world.Writer.Calls);
        Assert.Empty(world.CancelGate.ConfirmRequests);
    }

    [Fact]
    public async Task A_failing_owner_read_or_confirmation_is_unavailable_and_writes_nothing()
    {
        var world = new CommandWorld().Cancellable(InventoryTransferSourceScaleSetting.On);
        world.ScaleSetting.Value = InventoryTransferScaleSettingV1.On;
        world.CancelGate.FailRead = true;
        await Refused(world.Service().CancelDispatchAsync(CancelledTransfer, CancelRequest(), Transport("actor")),
            503, InventoryTransferCodesV1.Unavailable);
        Assert.Empty(world.Writer.Calls);

        world = new CommandWorld().Cancellable(InventoryTransferSourceScaleSetting.On);
        world.ScaleSetting.Value = InventoryTransferScaleSettingV1.On;
        world.CancelGate.FailConfirm = true;
        await Refused(world.Service().CancelDispatchAsync(CancelledTransfer, CancelRequest(), Transport("actor")),
            503, InventoryTransferCodesV1.Unavailable);
        Assert.Single(world.CancelGate.ConfirmRequests);
        Assert.Empty(world.Writer.Calls);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("zero")]
    [InlineData("negative")]
    public async Task A_missing_or_non_positive_confirmation_version_is_refused_and_not_recorded(string fault)
    {
        var world = new CommandWorld().Cancellable(InventoryTransferSourceScaleSetting.On);
        world.ScaleSetting.Value = InventoryTransferScaleSettingV1.On;
        world.CancelGate.LogisticsVersion = fault switch
        {
            "missing" => null,
            "zero" => 0,
            _ => -1,
        };

        await Refused(world.Service().CancelDispatchAsync(CancelledTransfer, CancelRequest(), Transport("actor")),
            409, InventoryTransferCodesV1.CancelConfirmationMissing);

        Assert.Empty(world.Writer.Calls);
        Assert.Empty(world.Writer.RecordedClosures);
    }

    [Fact]
    public async Task Without_the_owner_gate_the_cancel_is_unavailable_and_nothing_is_written()
    {
        var world = new CommandWorld { WithCancelGate = false }.Cancellable(InventoryTransferSourceScaleSetting.Off);

        await Refused(world.Service().CancelDispatchAsync(CancelledTransfer, CancelRequest(), Transport("actor")),
            503, InventoryTransferCodesV1.Unavailable);

        Assert.Empty(world.Writer.Calls);
        Assert.Empty(world.Writer.RecordedClosures);
    }

    [Fact]
    public async Task An_unregistered_cancel_permission_is_a_setup_gap_and_never_falls_back_to_the_transfer_write()
    {
        var world = new CommandWorld
        {
            IsKnown = permission => permission != InventoryPermissionManifestV1.StockTransferCancel,
        }.Cancellable(InventoryTransferSourceScaleSetting.On);

        await Refused(world.Service().CancelDispatchAsync(CancelledTransfer, CancelRequest(), Transport("actor")),
            503, InventoryTransferCodesV1.PermissionUnregistered);

        Assert.Empty(world.Authority.Measured);
        Assert.Empty(world.Writer.ReplayCalls);
        Assert.Equal(0, world.Writer.CancelContextReads);
    }

    [Fact]
    public async Task A_denied_cancel_permission_refuses_before_the_key_or_the_transfer_is_read()
    {
        var world = new CommandWorld().Cancellable(InventoryTransferSourceScaleSetting.On);
        world.Authority.Granted = false;

        await Refused(world.Service().CancelDispatchAsync(CancelledTransfer, CancelRequest(), Transport("actor")),
            403, InventoryTransferCodesV1.Forbidden);

        Assert.Empty(world.Writer.ReplayCalls);
        Assert.Equal(0, world.Writer.CancelContextReads);
    }

    [Fact]
    public async Task Stored_cancel_answer_replays_before_the_context_or_any_owner_read()
    {
        var world = new CommandWorld().Cancellable(InventoryTransferSourceScaleSetting.On);
        world.Writer.ReplayResult = new(TransferView(Company), true, "original-correlation");

        var result = await world.Service().CancelDispatchAsync(CancelledTransfer, CancelRequest(), Transport("actor"));

        Assert.True(result.IsReplay);
        Assert.Equal("cancel", Assert.Single(world.Writer.ReplayCalls));
        Assert.Equal(0, world.Writer.CancelContextReads);
        Assert.Empty(world.ScaleSetting.Reads);
        Assert.Empty(world.CancelGate.Reads);
    }

    [Fact]
    public async Task Concurrent_closure_recording_returns_the_key_winner_answer()
    {
        var world = new CommandWorld().Cancellable(InventoryTransferSourceScaleSetting.Off);
        world.ScaleSetting.Value = InventoryTransferScaleSettingV1.Off;
        world.Writer.ClosureWinner = new(TransferView(Company), true, "winner-correlation");

        var result = await world.Service().CancelDispatchAsync(CancelledTransfer, CancelRequest(), Transport("actor"));

        Assert.Same(world.Writer.ClosureWinner, result);
        Assert.Empty(world.Writer.CancelConfirmations);
    }

    [Theory]
    [InlineData("stale-version")]
    [InlineData("accepted")]
    [InlineData("partially-accepted")]
    [InlineData("already-cancelled")]
    public async Task Stale_version_or_a_state_other_than_in_transit_is_a_conflict_before_any_owner_read(string fault)
    {
        var world = new CommandWorld().Cancellable(InventoryTransferSourceScaleSetting.On);
        world.Writer.CancelContext = world.Writer.CancelContext! with
        {
            Status = fault switch
            {
                "accepted" => InventoryTransferStatus.Accepted,
                "partially-accepted" => InventoryTransferStatus.PartiallyAccepted,
                "already-cancelled" => InventoryTransferStatus.DispatchCancelled,
                _ => InventoryTransferStatus.InTransit,
            },
        };

        await Refused(world.Service().CancelDispatchAsync(CancelledTransfer,
                CancelRequest(fault == "stale-version" ? "1" : "2"), Transport("actor")),
            409, fault == "stale-version"
                ? InventoryTransferCodesV1.TransferVersionConflict
                : InventoryTransferCodesV1.TransferStateConflict);

        Assert.Empty(world.CancelGate.Reads);
        Assert.Empty(world.Writer.Calls);
    }

    [Theory]
    [InlineData("version-zero")]
    [InlineData("version-signed")]
    [InlineData("version-empty")]
    [InlineData("not-utc")]
    [InlineData("empty-reason")]
    [InlineData("control-reason")]
    [InlineData("long-reason")]
    [InlineData("empty-transfer")]
    public async Task An_invalid_cancel_request_is_refused_before_the_stored_context_is_read(string fault)
    {
        var world = new CommandWorld().Cancellable(InventoryTransferSourceScaleSetting.On);
        var request = fault switch
        {
            "version-zero" => CancelRequest("0"),
            "version-signed" => CancelRequest("+2"),
            "version-empty" => CancelRequest(""),
            "not-utc" => CancelRequest() with { CancelledAtUtc = DispatchedAt.ToOffset(TimeSpan.FromHours(3)) },
            "empty-reason" => CancelRequest() with { Reason = "   " },
            "control-reason" => CancelRequest() with { Reason = "SENTETIK\u0007neden" },
            "long-reason" => CancelRequest() with
            {
                Reason = new string('x', CancelInventoryTransferDispatchV1.MaximumReasonLength + 1),
            },
            _ => CancelRequest(),
        };

        await Refused(world.Service().CancelDispatchAsync(
                fault == "empty-transfer" ? Guid.Empty : CancelledTransfer, request, Transport("actor")),
            400, InventoryTransferCodesV1.ValidationFailed);

        Assert.Equal(0, world.Writer.CancelContextReads);
        Assert.Empty(world.Writer.Calls);
    }

    [Theory]
    [InlineData("missing", 404, InventoryTransferCodesV1.TransferNotFound)]
    [InlineData("foreign-company", 503, InventoryTransferCodesV1.Unavailable)]
    [InlineData("other-transfer", 503, InventoryTransferCodesV1.Unavailable)]
    [InlineData("no-dispatch-row", 503, InventoryTransferCodesV1.Unavailable)]
    public async Task A_missing_foreign_or_incomplete_context_is_refused_without_an_owner_read(
        string fault,
        int status,
        string code)
    {
        var world = new CommandWorld().Cancellable(InventoryTransferSourceScaleSetting.On);
        world.Writer.CancelContext = fault switch
        {
            "missing" => null,
            "foreign-company" => world.Writer.CancelContext! with { CompanyId = Guid.NewGuid() },
            "other-transfer" => world.Writer.CancelContext! with { TransferId = Guid.NewGuid() },
            _ => world.Writer.CancelContext! with { StoredScaleSettingAtDispatch = null },
        };

        await Refused(world.Service().CancelDispatchAsync(CancelledTransfer, CancelRequest(), Transport("actor")),
            status, code);

        Assert.Empty(world.CancelGate.Reads);
        Assert.Empty(world.Writer.Calls);
    }

    [Theory]
    [InlineData("on", InventoryTransferSourceScaleSetting.On)]
    [InlineData("off", InventoryTransferSourceScaleSetting.Off)]
    [InlineData("failure", InventoryTransferSourceScaleSetting.Unreadable)]
    [InlineData("no-port", InventoryTransferSourceScaleSetting.Unreadable)]
    public async Task Dispatch_stores_the_source_scale_setting_its_owner_reports_and_unreadable_otherwise(
        string reading,
        InventoryTransferSourceScaleSetting stored)
    {
        var world = new CommandWorld { WithScaleSettingPort = reading != "no-port" }.Dispatchable();
        world.ScaleSetting.Value = reading == "on" ? InventoryTransferScaleSettingV1.On : InventoryTransferScaleSettingV1.Off;
        world.ScaleSetting.Fail = reading == "failure";

        await world.Service().DispatchAsync(world.Reader.Result!.TransferId, DispatchRequest(), Transport("actor"));

        Assert.Equal(stored, Assert.Single(world.Writer.DispatchSettings));
        if (reading == "no-port")
        {
            Assert.Empty(world.ScaleSetting.Reads);
        }
        else
        {
            Assert.Equal((Company, Source), Assert.Single(world.ScaleSetting.Reads));
        }
    }

    [Fact]
    public void Transfer_cancel_permission_is_registered_company_wide_without_critical_marking_or_grant()
    {
        Assert.Equal(InventoryPermissionManifestV1.StockTransferCancel, ErpPermissions.InventoryStockTransferCancel);
        Assert.NotEqual(ErpPermissions.InventoryStockTransferWrite, ErpPermissions.InventoryStockTransferCancel);
        Assert.True(ErpPermissions.IsKnown(ErpPermissions.InventoryStockTransferCancel));
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ErpPermissions.InventoryStockTransferCancel));
        Assert.False(ErpPermissions.IsCritical(ErpPermissions.InventoryStockTransferCancel));
    }

    private static InventoryTransfer Restore(
        long version,
        InventoryTransferStatus status,
        IReadOnlyList<InventoryTransferDispatchLine> dispatched,
        InventoryTransferDocumentReference outgoing,
        IReadOnlyList<InventoryTransferAcceptedLineState> accepted,
        DateTimeOffset? cancelledAtUtc)
    {
        var line = Planned("70000000-0000-0000-0000-000000000001", "80000000-0000-0000-0000-000000000001", 10m);
        return InventoryTransfer.Restore(Guid.Parse("90000000-0000-0000-0000-000000000001"), Company, Source,
            new StockCustodian(StockPartyKind.Company, Company), Destination,
            new StockCustodian(StockPartyKind.Counterparty, TargetCustodian), At, [line], version, status,
            DispatchedAt, outgoing, dispatched, accepted, cancelledAtUtc);
    }

    private static CancelInventoryTransferDispatchV1 CancelRequest(string expectedVersion = "2") =>
        new(expectedVersion, DispatchedAt.AddHours(1), "SENTETIK yanlış araç");

    private static InventoryTransferDepartureFactV1 NotOccurred { get; } =
        new(InventoryTransferDepartureFactStatusV1.NotOccurred, null);

    private static InventoryTransferDepartureFactV1 Occurred(DateTimeOffset atUtc) =>
        new(InventoryTransferDepartureFactStatusV1.Occurred, atUtc);

    private static InventoryTransferDispatchCancelFactsV1 Facts(
        InventoryTransferEDispatchLinkV1 link = InventoryTransferEDispatchLinkV1.NotLinked,
        DateTimeOffset? linkedAtUtc = null,
        InventoryTransferDepartureFactV1? gibSend = null,
        InventoryTransferDepartureFactV1? exitWeighing = null,
        InventoryTransferDepartureFactV1? vehicleExit = null) =>
        new(link, linkedAtUtc, gibSend ?? NotOccurred, exitWeighing ?? NotOccurred, vehicleExit ?? NotOccurred);

    private static InventoryTransferDispatchCancelContextV1 InTransitContext(
        InventoryTransferSourceScaleSetting storedAtDispatch) =>
        new(CancelledTransfer, Company, 2, InventoryTransferStatus.InTransit, Source, DispatchedAt, storedAtDispatch,
            null, null);

    private sealed class TestScaleSetting : IInventoryTransferSourceScaleSettingPortV1
    {
        public InventoryTransferScaleSettingV1 Value { get; set; } = InventoryTransferScaleSettingV1.Off;
        public bool Fail { get; set; }
        public List<(Guid CompanyId, Guid SourceLocationId)> Reads { get; } = [];

        public Task<InventoryTransferScaleSettingV1> ReadAsync(
            Guid companyId,
            Guid sourceLocationId,
            CancellationToken cancellationToken = default)
        {
            Reads.Add((companyId, sourceLocationId));
            return Fail
                ? throw new InvalidOperationException("synthetic setting failure")
                : Task.FromResult(Value);
        }
    }

    private sealed class TestCancelGate : IInventoryTransferDispatchCancelGatePortV1
    {
        public InventoryTransferDispatchCancelFactsV1? Facts { get; set; } = InventoryTransferTests.Facts();
        public InventoryTransferDispatchCancelFactsV1? ConfirmationFacts { get; set; }
        public long? LogisticsVersion { get; set; } = 3;
        public long? EDispatchVersion { get; set; } = 4;
        public bool FailRead { get; set; }
        public bool FailConfirm { get; set; }
        public List<InventoryTransferDispatchCancelGateQueryV1> Reads { get; } = [];
        public List<(bool Logistics, bool EDispatch)> ConfirmRequests { get; } = [];

        public Task<InventoryTransferDispatchCancelFactsV1> ReadAsync(
            InventoryTransferDispatchCancelGateQueryV1 query,
            CancellationToken cancellationToken = default)
        {
            Reads.Add(query);
            return FailRead
                ? throw new InvalidOperationException("synthetic gate read failure")
                : Task.FromResult(Facts!);
        }

        public Task<InventoryTransferDispatchCancelConfirmationV1> ConfirmAsync(
            InventoryTransferDispatchCancelGateQueryV1 query,
            bool requireLogisticsNoExit,
            bool requireEDispatchCancel,
            CancellationToken cancellationToken = default)
        {
            ConfirmRequests.Add((requireLogisticsNoExit, requireEDispatchCancel));
            return FailConfirm
                ? throw new InvalidOperationException("synthetic gate confirmation failure")
                : Task.FromResult(new InventoryTransferDispatchCancelConfirmationV1(
                    ConfirmationFacts ?? Facts!, LogisticsVersion, EDispatchVersion));
        }
    }
}
