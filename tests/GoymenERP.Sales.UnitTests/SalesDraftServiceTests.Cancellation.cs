using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// DEC-SAL-005 / AT-007 over SYNTHETIC owners: the impact preview reads each category from its owner's port (Inventory
/// allocations plus Logistics loading and approved dispatch notes), shows a composed owner failure as unreadable and a
/// category with no implemented source as no source, never zero; the fail-closed
/// reading refuses a cancellation while any effect exists or an owner cannot answer; the command carries the preview's token;
/// a stale version, a wrong state, a changed impact and a blocked cancellation are the key's terminal 409s; the same key
/// replays with no second effect; the cancelled order keeps its history and refuses every later mutation; its lines are no
/// longer an approved demand for the consumers.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    /// <summary>A registered platform name standing in for `sales.orders.cancel` until the central catalogue knows it.</summary>
    private const string CancelPermission = ErpPermissions.OrganizationContextRead;

    private static readonly string[] NoSourceCategories =
    [
        SalesCancellationImpactSourcesV1.LinkedPurchasing, SalesCancellationImpactSourcesV1.LinkedProduction,
        SalesCancellationImpactSourcesV1.UnmatchedAdvances,
    ];

    [Fact]
    public async Task An_approved_order_with_nothing_recorded_by_its_owners_previews_cancellable_cancels_once_and_keeps_its_history()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0];
        var (allocations, loadings) = Owners(world);
        world.Authority.Measured.Clear();

        var preview = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);

        Assert.Equal(("4", SalesDraftApiV1.Rev0, true), (preview.DraftVersion, preview.Revision, preview.Cancellable));
        Assert.Equal(
            [
                (SalesCancellationImpactSourcesV1.ActiveAllocations, SalesCancellationImpactStatusesV1.None),
                (SalesCancellationImpactSourcesV1.LinkedPurchasing, SalesCancellationImpactStatusesV1.NoSource),
                (SalesCancellationImpactSourcesV1.LinkedProduction, SalesCancellationImpactStatusesV1.NoSource),
                (SalesCancellationImpactSourcesV1.LoadingWithoutDispatchNote, SalesCancellationImpactStatusesV1.None),
                (SalesCancellationImpactSourcesV1.UninvoicedDispatchNotes, SalesCancellationImpactStatusesV1.None),
                (SalesCancellationImpactSourcesV1.UnmatchedAdvances, SalesCancellationImpactStatusesV1.NoSource),
            ],
            preview.Sources.Select(source => (source.Source, source.Status)));
        Assert.Equal(new SalesDraftCancellationImpactLineV1(line.LineId, 1, KgUnit, "1.25", 0, "0", 0, "0", "0", "0", "0"), Assert.Single(preview.Lines));
        Assert.Matches("^[0-9a-f]{64}$", preview.ImpactToken);
        // Read-only, under the cancel name only: before, after the draft, after each owner.
        Assert.Equal([CancelPermission, CancelPermission, CancelPermission, CancelPermission], world.Authority.Measured);
        Assert.Equal([approved.Draft.Id], allocations.Asked);
        Assert.Equal([approved.Draft.Id], loadings.Asked);
        Assert.Equal(2, world.Store.Transitions.Count);
        // The same facts read again are the same preview.
        Assert.Equal(preview.ImpactToken, (await world.Service().GetCancellationImpactAsync(approved.Draft.Id)).ImpactToken);

        var cancelled = await world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "  SENTETIK müşteri vazgeçti  ", preview.ImpactToken), Metadata("k-cancel"));

        Assert.False(cancelled.IsReplay);
        Assert.Equal(("Cancelled", "Approved", "5", SalesDraftApiV1.Rev0), (cancelled.Detail.Draft.State, cancelled.Detail.Draft.ApprovalState,
            cancelled.Detail.Draft.Version, cancelled.Detail.Draft.Revision));
        // AT-007: the approval facts are the approval facts; the line is the line.
        Assert.Equal(approved.Approval, cancelled.Detail.Approval);
        Assert.Equal(approved.Lines.Select(item => (item.LineId, item.Quantity, item.RequestedDeliveryDate)),
            cancelled.Detail.Lines.Select(item => (item.LineId, item.Quantity, item.RequestedDeliveryDate)));
        var transition = world.Store.Transitions[^1];
        Assert.Equal((SalesDraftCommands.CancelV1, CancelPermission, "sales.order-draft.cancelled.v1", "Cancel", 4L, 5L, "Web"),
            (transition.Command, transition.Envelope.PermissionCode, transition.Audit.EventType, transition.Audit.Operation,
                transition.VersionBefore, transition.VersionAfter, transition.Envelope.SourceChannel));
        var facts = Assert.Single(world.Store.Cancellations);
        Assert.Equal((0, Actor, "SENTETIK müşteri vazgeçti", preview.ImpactToken), (facts.Revision, facts.CancelledBy, facts.Reason, facts.Impact.Token));
        Assert.Equal(preview.Sources.Select(source => (source.Source, source.Status)), facts.Impact.Sources.Select(entry => (entry.Source, entry.Status)));

        // Close and reopen: the order reads Cancelled with its reason and the preview it was decided on.
        var detail = await world.Service().GetDraftDetailAsync(approved.Draft.Id);
        Assert.Equal(("Cancelled", "Approved"), (detail.Draft.State, detail.Draft.ApprovalState));
        var recorded = (await world.Service().GetDraftCancellationAsync(approved.Draft.Id)).Cancellation!;
        Assert.Equal((SalesDraftApiV1.Rev0, Actor, "SENTETIK müşteri vazgeçti", preview.ImpactToken, 6),
            (recorded.Revision, recorded.CancelledBy, recorded.Reason, recorded.ImpactToken, recorded.Sources.Count));

        // The same key and content: the stored result, replayed — the owners are not asked, nothing is cancelled twice.
        var asked = (allocations.Asked.Count, loadings.Asked.Count);
        var replay = await world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "  SENTETIK müşteri vazgeçti  ", preview.ImpactToken), Metadata("k-cancel", correlation: "retry"));
        Assert.Equal((true, "synthetic-correlation", "Cancelled", "5"), (replay.IsReplay, replay.OriginalCorrelationId, replay.Detail.Draft.State, replay.Detail.Draft.Version));
        Assert.Equal(asked, (allocations.Asked.Count, loadings.Asked.Count));
        Assert.Single(world.Store.Cancellations);
        Assert.Equal(3, world.Store.Transitions.Count);
        // The same key with other content is one 409, nothing disclosed.
        var other = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "başka neden", preview.ImpactToken), Metadata("k-cancel")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch), Status(other));
    }

    [Fact]
    public async Task An_active_allocation_blocks_the_cancellation_with_the_key_recorded_and_once_released_the_order_cancels()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        var (allocations, _) = Owners(world);
        allocations.Answer = order => new(Company, order, [new(line, 1, 0.5m)]);

        var preview = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);
        Assert.False(preview.Cancellable);
        Assert.Equal(SalesCancellationImpactStatusesV1.Present, preview.Sources.Single(source => source.Source == SalesCancellationImpactSourcesV1.ActiveAllocations).Status);
        Assert.Equal((1, "0.5"), (preview.Lines[0].ActiveAllocations, preview.Lines[0].AllocatedKg));

        // Fail-closed: Sales does not release Inventory's allocation; the cancellation is refused and says why.
        var blocked = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "neden", preview.ImpactToken), Metadata("k-cancel-1")));
        Assert.Equal((409, SalesDraftProblemCodesV1.CancellationBlocked), Status(blocked));
        Assert.Equal([new SalesDraftFieldError(SalesCancellationImpactSourcesV1.ActiveAllocations, SalesCancellationImpactStatusesV1.Present)], blocked.Errors);
        var refusal = Assert.Single(world.Store.Refusals);
        Assert.Equal((CancelPermission, (Guid?)approved.Draft.Id), (refusal.Envelope.PermissionCode, refusal.DraftId));
        var replayed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "neden", preview.ImpactToken), Metadata("k-cancel-1", correlation: "retry")));
        Assert.Equal((409, SalesDraftProblemCodesV1.CancellationBlocked, true), (replayed.Status, replayed.Code, replayed.IsReplay));
        Assert.Equal(("Draft", "4"), ((await world.Service().GetDraftDetailAsync(approved.Draft.Id)).Draft.State,
            (await world.Service().GetDraftDetailAsync(approved.Draft.Id)).Draft.Version));
        Assert.Empty(world.Store.Cancellations);

        // The allocation released IN INVENTORY: the preview is read again and the order cancels under a new key.
        allocations.Answer = order => new(Company, order, []);
        var again = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);
        Assert.True(again.Cancellable);
        Assert.NotEqual(preview.ImpactToken, again.ImpactToken);
        var cancelled = await world.Service().CancelDraftAsync(approved.Draft.Id, new SalesDraftCancelRequest("4", "neden", again.ImpactToken), Metadata("k-cancel-2"));
        Assert.Equal("Cancelled", cancelled.Detail.Draft.State);
    }

    [Theory]
    [InlineData("instructed-only")]
    [InlineData("loaded")]
    [InlineData("shipped-and-delivered")]
    public async Task Any_loading_instruction_line_of_the_order_blocks_the_cancellation(string stage)
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        var (_, loadings) = Owners(world);
        loadings.Answer = order => new(Company, order, [stage switch
        {
            "instructed-only" => new SalesOrderLineLoadingV1(line, KgUnit, 1, 1m, 0m, 0m, 0m),
            "loaded" => new SalesOrderLineLoadingV1(line, KgUnit, 1, 1m, 1m, 0m, 0m),
            _ => new SalesOrderLineLoadingV1(line, KgUnit, 2, 1.25m, 1.25m, 1.25m, 1m),
        }]);

        var preview = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);

        Assert.False(preview.Cancellable);
        Assert.Equal(SalesCancellationImpactStatusesV1.Present,
            preview.Sources.Single(source => source.Source == SalesCancellationImpactSourcesV1.LoadingWithoutDispatchNote).Status);
        Assert.True(preview.Lines[0].LoadingInstructionLines >= 1);
        var blocked = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "neden", preview.ImpactToken), Metadata("k-cancel")));
        Assert.Equal((409, SalesDraftProblemCodesV1.CancellationBlocked, SalesCancellationImpactSourcesV1.LoadingWithoutDispatchNote),
            (blocked.Status, blocked.Code, Assert.Single(blocked.Errors).Field));
    }

    [Fact]
    public async Task An_approved_dispatch_note_blocks_cancellation_as_an_uninvoiced_source_candidate()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var (_, loadings) = Owners(world);
        loadings.Answer = order => new(Company, order, [], 1);

        var preview = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);

        Assert.False(preview.Cancellable);
        Assert.Equal(SalesCancellationImpactStatusesV1.Present,
            preview.Sources.Single(source => source.Source == SalesCancellationImpactSourcesV1.UninvoicedDispatchNotes).Status);
        var blocked = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "neden", preview.ImpactToken), Metadata("k-cancel-dispatch-note")));
        Assert.Equal((409, SalesDraftProblemCodesV1.CancellationBlocked,
                SalesCancellationImpactSourcesV1.UninvoicedDispatchNotes),
            (blocked.Status, blocked.Code, Assert.Single(blocked.Errors).Field));
    }

    [Fact]
    public async Task A_cancellation_on_a_preview_the_owners_no_longer_answer_is_the_keys_terminal_impact_changed()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var (allocations, _) = Owners(world);
        var preview = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);
        Assert.True(preview.Cancellable);

        // An allocation is recorded in Inventory after the user saw the preview.
        allocations.Answer = order => new(Company, order, [new(approved.Lines[0].LineId, 1, 1m)]);
        var changed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "neden", preview.ImpactToken), Metadata("k-cancel")));

        Assert.Equal((409, SalesDraftProblemCodesV1.CancellationImpactChanged, "impactToken"), (changed.Status, changed.Code, Assert.Single(changed.Errors).Field));
        Assert.Single(world.Store.Refusals);
        Assert.Empty(world.Store.Cancellations);
        // A token the preview never answered is the same refusal.
        var forged = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "neden", new string('0', 64)), Metadata("k-forged")));
        Assert.Equal(SalesDraftProblemCodesV1.CancellationImpactChanged, forged.Code);
    }

    [Fact]
    public async Task A_stale_version_a_wrong_state_or_an_already_cancelled_order_is_terminal_and_no_owner_is_asked()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var (allocations, loadings) = Owners(world);
        var token = (await world.Service().GetCancellationImpactAsync(approved.Draft.Id)).ImpactToken;
        allocations.Asked.Clear();
        loadings.Asked.Clear();

        var stale = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("3", "neden", token), Metadata("k-stale")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict, "expectedVersion"), (stale.Status, stale.Code, stale.Errors[0].Field));

        // A draft that was never approved is withdrawn by its preparer, not cancelled.
        var preparing = (await world.Service().CreateDraftAsync(Request(), Metadata("k-create-2"))).Detail.Draft;
        var wrongState = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(preparing.Id,
            new SalesDraftCancelRequest("2", "neden", token), Metadata("k-wrong")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict, "state"), (wrongState.Status, wrongState.Code, wrongState.Errors[0].Field));
        Assert.Empty(allocations.Asked);
        Assert.Empty(loadings.Asked);
        Assert.Equal(2, world.Store.Refusals.Count);

        await world.Service().CancelDraftAsync(approved.Draft.Id, new SalesDraftCancelRequest("4", "neden", token), Metadata("k-cancel"));
        var twice = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("5", "yine", token), Metadata("k-cancel-again")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), Status(twice));
        Assert.Single(world.Store.Cancellations);
        // A cancelled order previews as not cancellable.
        Assert.False((await world.Service().GetCancellationImpactAsync(approved.Draft.Id)).Cancellable);
    }

    [Fact]
    public async Task An_owner_that_is_not_composed_or_fails_is_unreadable_never_zero_and_the_cancellation_is_503_unrecorded()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);

        var preview = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);

        Assert.False(preview.Cancellable);
        Assert.Equal([SalesCancellationImpactStatusesV1.Unreadable, SalesCancellationImpactStatusesV1.Unreadable,
                SalesCancellationImpactStatusesV1.Unreadable],
            preview.Sources.Where(source => !NoSourceCategories.Contains(source.Source)).Select(source => source.Status));
        Assert.Equal(new SalesDraftCancellationImpactLineV1(approved.Lines[0].LineId, 1, KgUnit, "1.25", null, null, null, null, null, null, null),
            preview.Lines[0]);
        var unavailable = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "neden", preview.ImpactToken), Metadata("k-cancel")));
        Assert.Equal((503, SalesDraftProblemCodesV1.CancellationImpactUnavailable), Status(unavailable));

        var (allocations, _) = Owners(world);
        allocations.Answer = _ => throw new InvalidOperationException("SENTETIK owner outage");
        var failing = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);
        Assert.Equal(SalesCancellationImpactStatusesV1.Unreadable, failing.Sources[0].Status);
        Assert.Null(failing.Lines[0].ActiveAllocations);
        Assert.Equal("0", failing.Lines[0].Instructed);
        Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "neden", failing.ImpactToken), Metadata("k-cancel")))).Status);
        // Not a terminal answer: nothing recorded, the retry reads the owners again.
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(world.Store.Cancellations);
    }

    public static TheoryData<string> MalformedImpactAnswers => new()
    {
        "allocation-other-company", "allocation-other-order", "allocation-unknown-line", "allocation-twice", "allocation-negative",
        "allocation-above-scale", "loading-other-unit", "loading-delivered-above-shipped", "loading-loaded-above-instructed",
        "loading-above-scale", "loading-null-lines", "dispatch-other-company", "dispatch-other-order", "dispatch-negative",
    };

    [Theory]
    [MemberData(nameof(MalformedImpactAnswers))]
    public async Task A_malformed_owner_answer_is_unreadable_and_shows_no_number(string fault)
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        var (allocations, loadings) = Owners(world);
        switch (fault)
        {
            case "allocation-other-company": allocations.Answer = order => new(Guid.NewGuid(), order, []); break;
            case "allocation-other-order": allocations.Answer = _ => new(Company, Guid.NewGuid(), []); break;
            case "allocation-unknown-line": allocations.Answer = order => new(Company, order, [new(Guid.NewGuid(), 1, 1m)]); break;
            case "allocation-twice": allocations.Answer = order => new(Company, order, [new(line, 1, 1m), new(line, 1, 1m)]); break;
            case "allocation-negative": allocations.Answer = order => new(Company, order, [new(line, -1, 0m)]); break;
            case "allocation-above-scale": allocations.Answer = order => new(Company, order, [new(line, 1, 0.0001m)]); break;
            // No conversion: a total in another unit cannot be read as the order unit's.
            case "loading-other-unit": loadings.Answer = order => new(Company, order, [new(line, Guid.NewGuid(), 1, 1m, 0m, 0m, 0m)]); break;
            case "loading-delivered-above-shipped": loadings.Answer = order => new(Company, order, [new(line, KgUnit, 1, 1m, 1m, 0.5m, 0.6m)]); break;
            case "loading-loaded-above-instructed": loadings.Answer = order => new(Company, order, [new(line, KgUnit, 1, 1m, 1.5m, 0m, 0m)]); break;
            case "loading-above-scale": loadings.Answer = order => new(Company, order, [new(line, KgUnit, 1, 1.0005m, 0m, 0m, 0m)]); break;
            case "loading-null-lines": loadings.Answer = order => new(Company, order, null!); break;
            case "dispatch-other-company": loadings.Answer = order => new(Guid.NewGuid(), order, [], 0); break;
            case "dispatch-other-order": loadings.Answer = _ => new(Company, Guid.NewGuid(), [], 0); break;
            default: loadings.Answer = order => new(Company, order, [], -1); break;
        }

        var preview = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);

        Assert.False(preview.Cancellable);
        var unreadable = fault.StartsWith("allocation", StringComparison.Ordinal)
            ? SalesCancellationImpactSourcesV1.ActiveAllocations
            : fault.StartsWith("loading", StringComparison.Ordinal)
                ? SalesCancellationImpactSourcesV1.LoadingWithoutDispatchNote
                : SalesCancellationImpactSourcesV1.UninvoicedDispatchNotes;
        Assert.Equal(SalesCancellationImpactStatusesV1.Unreadable, preview.Sources.Single(source => source.Source == unreadable).Status);
        if (unreadable == SalesCancellationImpactSourcesV1.ActiveAllocations)
        {
            Assert.Null(preview.Lines[0].AllocatedKg);
        }
        else if (unreadable == SalesCancellationImpactSourcesV1.LoadingWithoutDispatchNote)
        {
            Assert.Null(preview.Lines[0].Instructed);
        }
    }

    [Fact]
    public async Task A_blank_or_too_long_reason_is_422_on_reason_and_nothing_is_recorded()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        Owners(world);
        var token = (await world.Service().GetCancellationImpactAsync(approved.Draft.Id)).ImpactToken;

        var blank = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "   ", token), Metadata("k-blank")));
        Assert.Equal((422, "validation_failed", "reason", SalesRuleCodes.CancellationReasonRequired),
            (blank.Status, blank.Code, blank.Errors[0].Field, blank.Errors[0].Code));
        var tooLong = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", new string('x', 4001), token), Metadata("k-long")));
        Assert.Equal(SalesRuleCodes.CancellationReasonTooLong, tooLong.Errors[0].Code);
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(world.Store.Cancellations);
        var malformed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("x", "neden", token), Metadata("k-bad")));
        Assert.Equal((400, "validation_failed"), Status(malformed));
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("unbound")]
    public async Task Without_the_cancel_name_the_preview_and_the_command_are_refused_before_the_draft_and_the_owners(string fault)
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var (allocations, loadings) = Owners(world);
        if (fault == "unbound")
        {
            world.Bindings = world.Bindings with { CancelPermission = null };
        }
        else
        {
            // Every other name (the draft read, create, approve, price, schedule) stays granted: none confers it.
            world.Authority.Denied.Add(CancelPermission);
        }

        world.Store.OnFindDraft = () => throw new InvalidOperationException("the draft must not be read");
        var preview = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetCancellationImpactAsync(approved.Draft.Id));
        var command = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "neden", new string('a', 64)), Metadata("k-cancel")));

        var expected = fault == "unbound" ? (503, SalesDraftProblemCodesV1.PermissionUnregistered) : (403, "forbidden");
        Assert.Equal(expected, Status(preview));
        Assert.Equal(expected, Status(command));
        Assert.Empty(allocations.Asked);
        Assert.Empty(loadings.Asked);
        Assert.Empty(world.Store.Refusals);
    }

    [Fact]
    public async Task An_unknown_or_another_companys_draft_is_404_for_the_preview_the_command_and_the_record()
    {
        var world = new World();
        var (allocations, _) = Owners(world);

        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetCancellationImpactAsync(Guid.NewGuid()))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(Guid.NewGuid(),
            new SalesDraftCancelRequest("4", "neden", new string('a', 64)), Metadata("k-cancel")))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftCancellationAsync(Guid.NewGuid()))).Status);
        Assert.Empty(allocations.Asked);
        // An order that is not cancelled has no recorded cancellation (null, never an empty record).
        var approved = await ApprovedAsync(world);
        Assert.Null((await world.Service().GetDraftCancellationAsync(approved.Draft.Id)).Cancellation);
    }

    [Fact]
    public async Task A_cancelled_order_refuses_later_commands_stays_readable_and_is_no_longer_an_approved_demand_for_consumers()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0];
        Owners(world);
        await world.Service().RecordCustomerAcceptanceAsync(approved.Draft.Id, Command("4"), Metadata("k-accept-before-cancel"));
        var token = (await world.Service().GetCancellationImpactAsync(approved.Draft.Id)).ImpactToken;

        // Before: the line port answers the approved line and lists it for a consumer's picker.
        var company = new CompanyContext();
        company.Set(Company);
        var port = new SalesOrderLineReferencePort(world.Store, company);
        Assert.Equal(SalesOrderLineReferenceOutcomeV1.Approved, (await port.GetApprovedLineAsync(approved.Draft.Id, line.LineId)).Outcome);
        Assert.Single((await port.ListApprovedLinesAsync(10, null)).Items);

        await world.Service().CancelDraftAsync(approved.Draft.Id, new SalesDraftCancelRequest("5", "neden", token), Metadata("k-cancel"));

        var answer = await port.GetApprovedLineAsync(approved.Draft.Id, line.LineId);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.NotApproved, "Cancelled"), (answer.Outcome, answer.ApprovalState));
        Assert.Empty((await port.ListApprovedLinesAsync(10, null)).Items);

        var reschedule = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().RescheduleDeliveryAsync(approved.Draft.Id,
            new SalesDraftDeliveryScheduleRequest("6", Day.AddDays(30),
                [new SalesDraftLineScheduleV1(line.LineId, Day.AddDays(31), [.. line.RequestedDeliverySlices!.Select(slice => new SalesDraftSliceScheduleV1(slice.SliceId, Day.AddDays(32)))])]),
            Metadata("k-reschedule")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), Status(reschedule));
        var revise = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(approved.Draft.Id,
            new SalesDraftCommercialRevisionRequest("6", [new SalesDraftLineCommercialV1(line.LineId, "10", KgUnit)]), Metadata("k-revise")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), Status(revise));
        var submit = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().SubmitDraftAsync(approved.Draft.Id, Command("6"), Metadata("k-submit-2")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), Status(submit));
        // Still readable as history: the detail, the list (its lifecycle column says Cancelled) and the delivery progress.
        Assert.Equal("Cancelled", Assert.Single((await world.Service().GetDraftListAsync(new SalesOrderDraftListQuery())).Items, item => item.Id == approved.Draft.Id).State);
        world.Deliveries = new FakeDeliveries(order => new(Company, order, []));
        Assert.Equal("1.25", Assert.Single((await world.Service().GetDraftDeliveryProgressAsync(approved.Draft.Id)).Lines).Remaining);
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------

    /// <summary>A one-line draft created, submitted and approved through the service: version 4.</summary>
    private static async Task<SalesOrderDraftDetailView> ApprovedAsync(World world)
    {
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-create"))).Detail.Draft;
        await world.Service().SubmitDraftAsync(created.Id, Command("2"), Metadata("k-submit"));
        return (await world.Service().ApproveDraftAsync(created.Id, Command("3"), Metadata("k-approve"))).Detail;
    }

    /// <summary>Both impact owners composed, answering nothing for the order.</summary>
    private static (FakeAllocations Allocations, FakeLoadings Loadings) Owners(World world)
    {
        var allocations = new FakeAllocations(order => new(Company, order, []));
        var loadings = new FakeLoadings(order => new(Company, order, []));
        world.Allocations = allocations;
        world.Loadings = loadings;
        return (allocations, loadings);
    }

    /// <summary>A SYNTHETIC allocation owner (Inventory's role): answers what the case gives it and records which orders it was asked for.</summary>
    private sealed class FakeAllocations(Func<Guid, SalesOrderAllocationReadAnswerV1> answer) : ISalesOrderAllocationReadPortV1
    {
        public Func<Guid, SalesOrderAllocationReadAnswerV1> Answer { get; set; } = answer;

        public List<Guid> Asked { get; } = [];

        public Task<SalesOrderAllocationReadAnswerV1> ReadOrderAllocationsAsync(Guid orderId, CancellationToken cancellationToken = default)
        {
            Asked.Add(orderId);
            return Task.FromResult(Answer(orderId));
        }
    }

    /// <summary>A SYNTHETIC loading owner (Logistics's role).</summary>
    private sealed class FakeLoadings(Func<Guid, SalesOrderLoadingReadAnswerV1> answer) : ISalesOrderLoadingReadPortV1
    {
        public Func<Guid, SalesOrderLoadingReadAnswerV1> Answer { get; set; } = answer;

        public List<Guid> Asked { get; } = [];

        public Task<SalesOrderLoadingReadAnswerV1> ReadOrderLoadingAsync(Guid orderId, CancellationToken cancellationToken = default)
        {
            Asked.Add(orderId);
            return Task.FromResult(Answer(orderId));
        }
    }
}
