using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SINAN_KARARLARI_2026-10-21 No 30 (force-close) as narrowed by SAT-D12, over SYNTHETIC owners: the preview reads the
/// cancellation's six categories from the same owner ports and says per source whether it BLOCKS — an active allocation, open
/// loading (instructed above shipped) or an unreadable owner blocks; an uninvoiced dispatch note is a warning only; a fully
/// shipped instruction line is the shipped part. Every line shows the closed remainder (ordered − shipped, exact) the command
/// records. The command carries the preview's token; a stale version, a wrong state, a changed impact and a blocked closure
/// are the key's terminal 409s; an unreadable owner is 503 and unrecorded; the same key replays with no second effect. The
/// closed order keeps its line quantities and history, refuses every later mutation and is no longer an approved demand.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private const string ForceClosePermission = SalesPermissionManifestV1.OrdersForceClose;

    [Fact]
    public async Task An_approved_order_with_nothing_recorded_previews_closable_closes_once_with_its_remainder_and_keeps_its_history()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0];
        var (allocations, loadings) = Owners(world);
        world.Authority.Measured.Clear();

        var preview = await world.Service().GetForceCloseImpactAsync(approved.Draft.Id);

        Assert.Equal(("4", SalesDraftApiV1.Rev0, true), (preview.DraftVersion, preview.Revision, preview.Closable));
        Assert.Equal(
            [
                (SalesCancellationImpactSourcesV1.ActiveAllocations, SalesCancellationImpactStatusesV1.None, false),
                (SalesCancellationImpactSourcesV1.LinkedPurchasing, SalesCancellationImpactStatusesV1.NoSource, false),
                (SalesCancellationImpactSourcesV1.LinkedProduction, SalesCancellationImpactStatusesV1.NoSource, false),
                (SalesCancellationImpactSourcesV1.LoadingWithoutDispatchNote, SalesCancellationImpactStatusesV1.None, false),
                (SalesCancellationImpactSourcesV1.UninvoicedDispatchNotes, SalesCancellationImpactStatusesV1.None, false),
                (SalesCancellationImpactSourcesV1.UnmatchedAdvances, SalesCancellationImpactStatusesV1.NoSource, false),
            ],
            preview.Sources.Select(source => (source.Source, source.Status, source.Blocks)));
        Assert.Equal(new SalesDraftForceCloseImpactLineV1(line.LineId, 1, KgUnit, "1.25", 0, "0", 0, "0", "0", "0", "0", "1.25"),
            Assert.Single(preview.Lines));
        Assert.Matches("^[0-9a-f]{64}$", preview.ImpactToken);
        // Read-only, under the force-close name only: before, after the draft, after each owner.
        Assert.Equal([ForceClosePermission, ForceClosePermission, ForceClosePermission, ForceClosePermission], world.Authority.Measured);
        Assert.Equal([approved.Draft.Id], allocations.Asked);
        Assert.Equal([approved.Draft.Id], loadings.Asked);
        // Its own shape: the cancellation preview of the same facts is another token.
        Assert.NotEqual((await world.Service().GetCancellationImpactAsync(approved.Draft.Id)).ImpactToken, preview.ImpactToken);
        Assert.Equal(preview.ImpactToken, (await world.Service().GetForceCloseImpactAsync(approved.Draft.Id)).ImpactToken);

        var closed = await world.Service().ForceCloseDraftAsync(approved.Draft.Id,
            new SalesDraftForceCloseRequest("4", "  SENTETIK kalan istenmiyor  ", preview.ImpactToken), Metadata("k-close"));

        Assert.False(closed.IsReplay);
        Assert.Equal(("Closed", "Approved", "5", SalesDraftApiV1.Rev0), (closed.Detail.Draft.State, closed.Detail.Draft.ApprovalState,
            closed.Detail.Draft.Version, closed.Detail.Draft.Revision));
        // The approval facts and the line quantities do not move; the remainder is recorded beside them.
        Assert.Equal(approved.Approval, closed.Detail.Approval);
        Assert.Equal(approved.Lines.Select(item => (item.LineId, item.Quantity, item.RequestedDeliveryDate)),
            closed.Detail.Lines.Select(item => (item.LineId, item.Quantity, item.RequestedDeliveryDate)));
        var transition = world.Store.Transitions[^1];
        Assert.Equal((SalesDraftCommands.ForceCloseV1, ForceClosePermission, "sales.order-draft.force-closed.v1", "ForceClose", 4L, 5L, "Web"),
            (transition.Command, transition.Envelope.PermissionCode, transition.Audit.EventType, transition.Audit.Operation,
                transition.VersionBefore, transition.VersionAfter, transition.Envelope.SourceChannel));
        var facts = Assert.Single(world.Store.ForceClosures);
        Assert.Equal((0, Actor, "SENTETIK kalan istenmiyor", preview.ImpactToken), (facts.Revision, facts.ClosedBy, facts.Reason, facts.Impact.Token));
        Assert.Equal(preview.Sources.Select(source => (source.Source, source.Status, source.Blocks)),
            facts.Impact.Sources.Select(entry => (entry.Source, entry.Status, entry.Blocks)));
        Assert.Equal([(line.LineId, 1, "1.25", "0", "1.25")],
            facts.Lines.Select(item => (item.LineId, item.LineNo, item.Ordered, item.Shipped, item.ClosedRemainder)));

        // Reopen: the order reads Closed with its reason, the preview it was decided on and the closed remainder.
        var detail = await world.Service().GetDraftDetailAsync(approved.Draft.Id);
        Assert.Equal(("Closed", "Approved"), (detail.Draft.State, detail.Draft.ApprovalState));
        var recorded = (await world.Service().GetDraftForceClosureAsync(approved.Draft.Id)).ForceClosure!;
        Assert.Equal((SalesDraftApiV1.Rev0, Actor, "SENTETIK kalan istenmiyor", preview.ImpactToken, 6),
            (recorded.Revision, recorded.ClosedBy, recorded.Reason, recorded.ImpactToken, recorded.Sources.Count));
        Assert.Equal(new SalesDraftForceClosureLineV1(line.LineId, 1, "1.25", "0", "1.25"), Assert.Single(recorded.Lines));
        Assert.Null((await world.Service().GetDraftCancellationAsync(approved.Draft.Id)).Cancellation);

        // The same key and content: the stored result, replayed — the owners are not asked, nothing is closed twice.
        var asked = (allocations.Asked.Count, loadings.Asked.Count);
        var replay = await world.Service().ForceCloseDraftAsync(approved.Draft.Id,
            new SalesDraftForceCloseRequest("4", "  SENTETIK kalan istenmiyor  ", preview.ImpactToken), Metadata("k-close", correlation: "retry"));
        Assert.Equal((true, "synthetic-correlation", "Closed", "5"), (replay.IsReplay, replay.OriginalCorrelationId, replay.Detail.Draft.State, replay.Detail.Draft.Version));
        Assert.Equal(asked, (allocations.Asked.Count, loadings.Asked.Count));
        Assert.Single(world.Store.ForceClosures);
        // The same key with other content is one 409, nothing disclosed.
        var other = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ForceCloseDraftAsync(approved.Draft.Id,
            new SalesDraftForceCloseRequest("4", "başka neden", preview.ImpactToken), Metadata("k-close")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch), Status(other));
        // A closed order previews as not closable.
        Assert.False((await world.Service().GetForceCloseImpactAsync(approved.Draft.Id)).Closable);
    }

    [Theory]
    [InlineData("fully-shipped", "1", "1", "1", "0.5", "0.25")]
    [InlineData("shipped-and-delivered-in-two-instructions", "1.2", "1.2", "1.2", "1.2", "0.05")]
    [InlineData("whole-line-shipped", "1.25", "1.25", "1.25", "0", "0")]
    public async Task A_fully_shipped_instruction_line_does_not_block_and_the_closed_remainder_is_ordered_minus_shipped(string stage,
        string instructed, string loaded, string shipped, string delivered, string remainder)
    {
        _ = stage;
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        var (_, loadings) = Owners(world);
        var value = (string text) => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        loadings.Answer = order => new(Company, order, [new SalesOrderLineLoadingV1(line, KgUnit, 2, value(instructed), value(loaded),
            value(shipped), value(delivered))]);

        var preview = await world.Service().GetForceCloseImpactAsync(approved.Draft.Id);

        Assert.True(preview.Closable);
        Assert.Equal((SalesCancellationImpactStatusesV1.None, false),
            preview.Sources.Where(source => source.Source == SalesCancellationImpactSourcesV1.LoadingWithoutDispatchNote)
                .Select(source => (source.Status, source.Blocks)).Single());
        Assert.Equal((2, instructed, shipped, delivered, remainder), (preview.Lines[0].LoadingInstructionLines, preview.Lines[0].Instructed,
            preview.Lines[0].Shipped, preview.Lines[0].Delivered, preview.Lines[0].ClosedRemainder));

        var closed = await world.Service().ForceCloseDraftAsync(approved.Draft.Id,
            new SalesDraftForceCloseRequest("4", "neden", preview.ImpactToken), Metadata("k-close"));

        Assert.Equal(("Closed", "1.25"), (closed.Detail.Draft.State, closed.Detail.Lines[0].Quantity));
        var recorded = Assert.Single(Assert.Single(world.Store.ForceClosures).Lines);
        Assert.Equal(("1.25", shipped, remainder), (recorded.Ordered, recorded.Shipped, recorded.ClosedRemainder));
    }

    [Theory]
    [InlineData("instructed-only", "1", "0", "0")]
    [InlineData("loaded-not-shipped", "1", "1", "0")]
    [InlineData("partly-shipped", "1", "1", "0.5")]
    public async Task Open_loading_instructed_above_shipped_blocks_the_closure_as_the_keys_recorded_refusal(string stage,
        string instructed, string loaded, string shipped)
    {
        _ = stage;
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        var (_, loadings) = Owners(world);
        var value = (string text) => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        loadings.Answer = order => new(Company, order, [new SalesOrderLineLoadingV1(line, KgUnit, 1, value(instructed), value(loaded),
            value(shipped), 0m)]);

        var preview = await world.Service().GetForceCloseImpactAsync(approved.Draft.Id);

        Assert.False(preview.Closable);
        Assert.Equal((SalesCancellationImpactStatusesV1.Present, true),
            preview.Sources.Where(source => source.Source == SalesCancellationImpactSourcesV1.LoadingWithoutDispatchNote)
                .Select(source => (source.Status, source.Blocks)).Single());
        var blocked = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ForceCloseDraftAsync(approved.Draft.Id,
            new SalesDraftForceCloseRequest("4", "neden", preview.ImpactToken), Metadata("k-close")));
        Assert.Equal((409, SalesDraftProblemCodesV1.ForceCloseBlocked),
            Status(blocked));
        Assert.Equal([new SalesDraftFieldError(SalesCancellationImpactSourcesV1.LoadingWithoutDispatchNote, SalesCancellationImpactStatusesV1.Present)],
            blocked.Errors);
        var refusal = Assert.Single(world.Store.Refusals);
        Assert.Equal((ForceClosePermission, (Guid?)approved.Draft.Id), (refusal.Envelope.PermissionCode, refusal.DraftId));
        var replayed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ForceCloseDraftAsync(approved.Draft.Id,
            new SalesDraftForceCloseRequest("4", "neden", preview.ImpactToken), Metadata("k-close", correlation: "retry")));
        Assert.Equal((409, SalesDraftProblemCodesV1.ForceCloseBlocked, true), (replayed.Status, replayed.Code, replayed.IsReplay));
        Assert.Equal("Draft", (await world.Service().GetDraftDetailAsync(approved.Draft.Id)).Draft.State);
        Assert.Empty(world.Store.ForceClosures);
    }

    [Fact]
    public async Task An_active_allocation_blocks_the_closure_and_once_released_in_inventory_the_order_closes()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        var (allocations, _) = Owners(world);
        allocations.Answer = order => new(Company, order, [new(line, 1, 0.5m)]);

        var preview = await world.Service().GetForceCloseImpactAsync(approved.Draft.Id);
        Assert.False(preview.Closable);
        Assert.True(preview.Sources.Single(source => source.Source == SalesCancellationImpactSourcesV1.ActiveAllocations).Blocks);
        Assert.Equal((1, "0.5"), (preview.Lines[0].ActiveAllocations, preview.Lines[0].AllocatedKg));

        // DEC-STK-005: Sales does not release Inventory's allocation; the closure is refused and says why.
        var blocked = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ForceCloseDraftAsync(approved.Draft.Id,
            new SalesDraftForceCloseRequest("4", "neden", preview.ImpactToken), Metadata("k-close-1")));
        Assert.Equal((409, SalesDraftProblemCodesV1.ForceCloseBlocked), Status(blocked));
        Assert.Equal([new SalesDraftFieldError(SalesCancellationImpactSourcesV1.ActiveAllocations, SalesCancellationImpactStatusesV1.Present)], blocked.Errors);

        allocations.Answer = order => new(Company, order, []);
        var again = await world.Service().GetForceCloseImpactAsync(approved.Draft.Id);
        Assert.True(again.Closable);
        Assert.NotEqual(preview.ImpactToken, again.ImpactToken);
        var closed = await world.Service().ForceCloseDraftAsync(approved.Draft.Id,
            new SalesDraftForceCloseRequest("4", "neden", again.ImpactToken), Metadata("k-close-2"));
        Assert.Equal("Closed", closed.Detail.Draft.State);
    }

    [Fact]
    public async Task An_uninvoiced_dispatch_note_is_a_warning_that_does_not_block_the_closure_SAT_D12()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        var (_, loadings) = Owners(world);
        // The shipped part is on an approved dispatch note not yet invoiced: the financial-closing axis stays open.
        loadings.Answer = order => new(Company, order, [new SalesOrderLineLoadingV1(line, KgUnit, 1, 1m, 1m, 1m, 1m)], 1);

        var preview = await world.Service().GetForceCloseImpactAsync(approved.Draft.Id);

        Assert.True(preview.Closable);
        Assert.Equal((SalesCancellationImpactStatusesV1.Present, false),
            preview.Sources.Where(source => source.Source == SalesCancellationImpactSourcesV1.UninvoicedDispatchNotes)
                .Select(source => (source.Status, source.Blocks)).Single());
        // The cancellation keeps its stricter reading of the same facts.
        Assert.False((await world.Service().GetCancellationImpactAsync(approved.Draft.Id)).Cancellable);

        var closed = await world.Service().ForceCloseDraftAsync(approved.Draft.Id,
            new SalesDraftForceCloseRequest("4", "neden", preview.ImpactToken), Metadata("k-close"));

        Assert.Equal("Closed", closed.Detail.Draft.State);
        var facts = Assert.Single(world.Store.ForceClosures);
        Assert.Contains(facts.Impact.Sources, entry => entry.Source == SalesCancellationImpactSourcesV1.UninvoicedDispatchNotes
            && entry.Status == SalesCancellationImpactStatusesV1.Present && !entry.Blocks);
        Assert.Equal("0.25", Assert.Single(facts.Lines).ClosedRemainder);
    }

    [Fact]
    public async Task An_owner_that_is_not_composed_or_fails_is_unreadable_blocks_and_the_closure_is_503_unrecorded()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);

        var preview = await world.Service().GetForceCloseImpactAsync(approved.Draft.Id);

        Assert.False(preview.Closable);
        Assert.Equal(
            [
                (SalesCancellationImpactStatusesV1.Unreadable, true), (SalesCancellationImpactStatusesV1.Unreadable, true),
                (SalesCancellationImpactStatusesV1.Unreadable, true),
            ],
            preview.Sources.Where(source => !NoSourceCategories.Contains(source.Source)).Select(source => (source.Status, source.Blocks)));
        Assert.Equal(new SalesDraftForceCloseImpactLineV1(approved.Lines[0].LineId, 1, KgUnit, "1.25", null, null, null, null, null, null, null, null),
            preview.Lines[0]);
        var unavailable = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ForceCloseDraftAsync(approved.Draft.Id,
            new SalesDraftForceCloseRequest("4", "neden", preview.ImpactToken), Metadata("k-close")));
        Assert.Equal((503, SalesDraftProblemCodesV1.ForceCloseImpactUnavailable), Status(unavailable));

        // Only the dispatch-note half is unreadable when the owner answers malformed counts: still a blocker, never "a warning".
        var (_, loadings) = Owners(world);
        loadings.Answer = order => new(Guid.NewGuid(), order, [], 0);
        var malformed = await world.Service().GetForceCloseImpactAsync(approved.Draft.Id);
        Assert.Equal((SalesCancellationImpactStatusesV1.Unreadable, true),
            malformed.Sources.Where(source => source.Source == SalesCancellationImpactSourcesV1.UninvoicedDispatchNotes)
                .Select(source => (source.Status, source.Blocks)).Single());
        Assert.False(malformed.Closable);
        Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ForceCloseDraftAsync(approved.Draft.Id,
            new SalesDraftForceCloseRequest("4", "neden", malformed.ImpactToken), Metadata("k-close")))).Status);
        // Not a terminal answer: nothing recorded, the retry reads the owners again.
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(world.Store.ForceClosures);
    }

    [Fact]
    public async Task A_closure_on_a_preview_the_owners_no_longer_answer_or_on_a_cancellation_token_is_the_keys_terminal_impact_changed()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var (allocations, _) = Owners(world);
        var preview = await world.Service().GetForceCloseImpactAsync(approved.Draft.Id);
        Assert.True(preview.Closable);

        allocations.Answer = order => new(Company, order, [new(approved.Lines[0].LineId, 1, 1m)]);
        var changed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ForceCloseDraftAsync(approved.Draft.Id,
            new SalesDraftForceCloseRequest("4", "neden", preview.ImpactToken), Metadata("k-close")));
        Assert.Equal((409, SalesDraftProblemCodesV1.ForceCloseImpactChanged, "impactToken"), (changed.Status, changed.Code, Assert.Single(changed.Errors).Field));
        Assert.Single(world.Store.Refusals);

        allocations.Answer = order => new(Company, order, []);
        var cancellationToken = (await world.Service().GetCancellationImpactAsync(approved.Draft.Id)).ImpactToken;
        var wrongShape = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ForceCloseDraftAsync(approved.Draft.Id,
            new SalesDraftForceCloseRequest("4", "neden", cancellationToken), Metadata("k-close-cancellation-token")));
        Assert.Equal(SalesDraftProblemCodesV1.ForceCloseImpactChanged, wrongShape.Code);
        Assert.Empty(world.Store.ForceClosures);
    }

    [Fact]
    public async Task A_stale_version_a_wrong_state_or_an_already_closed_or_cancelled_order_is_terminal_and_no_owner_is_asked()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var (allocations, loadings) = Owners(world);
        var token = (await world.Service().GetForceCloseImpactAsync(approved.Draft.Id)).ImpactToken;
        allocations.Asked.Clear();
        loadings.Asked.Clear();

        var stale = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ForceCloseDraftAsync(approved.Draft.Id,
            new SalesDraftForceCloseRequest("3", "neden", token), Metadata("k-stale")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict, "expectedVersion"), (stale.Status, stale.Code, stale.Errors[0].Field));

        // A draft that was never approved is withdrawn by its preparer, not force-closed.
        var preparing = (await world.Service().CreateDraftAsync(Request(), Metadata("k-create-2"))).Detail.Draft;
        var wrongState = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ForceCloseDraftAsync(preparing.Id,
            new SalesDraftForceCloseRequest("2", "neden", token), Metadata("k-wrong")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict, "state"), (wrongState.Status, wrongState.Code, wrongState.Errors[0].Field));
        Assert.Empty(allocations.Asked);
        Assert.Empty(loadings.Asked);
        Assert.Equal(2, world.Store.Refusals.Count);

        await world.Service().ForceCloseDraftAsync(approved.Draft.Id, new SalesDraftForceCloseRequest("4", "neden", token), Metadata("k-close"));
        var twice = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ForceCloseDraftAsync(approved.Draft.Id,
            new SalesDraftForceCloseRequest("5", "yine", token), Metadata("k-close-again")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), Status(twice));
        // A closed order is not cancelled afterwards either.
        var cancel = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("5", "neden", token), Metadata("k-cancel-after-close")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), Status(cancel));
        Assert.Single(world.Store.ForceClosures);
        Assert.Empty(world.Store.Cancellations);
    }

    [Fact]
    public async Task A_cancelled_order_is_not_force_closed()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        Owners(world);
        var cancelToken = (await world.Service().GetCancellationImpactAsync(approved.Draft.Id)).ImpactToken;
        await world.Service().CancelDraftAsync(approved.Draft.Id, new SalesDraftCancelRequest("4", "neden", cancelToken), Metadata("k-cancel"));

        var preview = await world.Service().GetForceCloseImpactAsync(approved.Draft.Id);
        Assert.False(preview.Closable);
        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ForceCloseDraftAsync(approved.Draft.Id,
            new SalesDraftForceCloseRequest("5", "neden", preview.ImpactToken), Metadata("k-close")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), Status(refused));
        Assert.Empty(world.Store.ForceClosures);
    }

    [Fact]
    public async Task A_blank_or_too_long_force_close_reason_is_422_on_reason_and_nothing_is_recorded()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        Owners(world);
        var token = (await world.Service().GetForceCloseImpactAsync(approved.Draft.Id)).ImpactToken;

        var blank = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ForceCloseDraftAsync(approved.Draft.Id,
            new SalesDraftForceCloseRequest("4", "   ", token), Metadata("k-blank")));
        Assert.Equal((422, "validation_failed", "reason", SalesRuleCodes.ForceCloseReasonRequired),
            (blank.Status, blank.Code, blank.Errors[0].Field, blank.Errors[0].Code));
        var tooLong = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ForceCloseDraftAsync(approved.Draft.Id,
            new SalesDraftForceCloseRequest("4", new string('x', 4001), token), Metadata("k-long")));
        Assert.Equal(SalesRuleCodes.ForceCloseReasonTooLong, tooLong.Errors[0].Code);
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(world.Store.ForceClosures);
        var malformed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ForceCloseDraftAsync(approved.Draft.Id,
            new SalesDraftForceCloseRequest("x", "neden", token), Metadata("k-bad")));
        Assert.Equal((400, "validation_failed"), Status(malformed));
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("unbound")]
    [InlineData("cancel-only")]
    public async Task Without_the_force_close_name_the_preview_and_the_command_are_refused_before_the_draft_and_the_owners(string fault)
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var (allocations, loadings) = Owners(world);
        if (fault == "unbound")
        {
            world.Bindings = world.Bindings with { ForceClosePermission = null };
        }
        else
        {
            // Every other name (the draft read, create, approve, price, schedule, quantity AND cancel) stays granted: none confers it.
            world.Authority.Denied.Add(ForceClosePermission);
        }

        world.Store.OnFindDraft = () => throw new InvalidOperationException("the draft must not be read");
        var preview = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetForceCloseImpactAsync(approved.Draft.Id));
        var command = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ForceCloseDraftAsync(approved.Draft.Id,
            new SalesDraftForceCloseRequest("4", "neden", new string('a', 64)), Metadata("k-close")));

        var expected = fault == "unbound" ? (503, SalesDraftProblemCodesV1.PermissionUnregistered) : (403, "forbidden");
        Assert.Equal(expected, Status(preview));
        Assert.Equal(expected, Status(command));
        Assert.Empty(allocations.Asked);
        Assert.Empty(loadings.Asked);
        Assert.Empty(world.Store.Refusals);
    }

    [Fact]
    public async Task An_unknown_or_another_companys_draft_is_404_for_the_force_close_preview_the_command_and_the_record()
    {
        var world = new World();
        var (allocations, _) = Owners(world);

        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetForceCloseImpactAsync(Guid.NewGuid()))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ForceCloseDraftAsync(Guid.NewGuid(),
            new SalesDraftForceCloseRequest("4", "neden", new string('a', 64)), Metadata("k-close")))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftForceClosureAsync(Guid.NewGuid()))).Status);
        Assert.Empty(allocations.Asked);
        var approved = await ApprovedAsync(world);
        Assert.Null((await world.Service().GetDraftForceClosureAsync(approved.Draft.Id)).ForceClosure);
    }

    [Fact]
    public async Task A_closed_order_refuses_later_commands_stays_readable_and_is_no_longer_an_approved_demand_for_consumers()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0];
        Owners(world);
        await world.Service().RecordCustomerAcceptanceAsync(approved.Draft.Id, Command("4"), Metadata("k-accept-before-close"));
        var token = (await world.Service().GetForceCloseImpactAsync(approved.Draft.Id)).ImpactToken;

        var company = new CompanyContext();
        company.Set(Company);
        var port = new SalesOrderLineReferencePort(world.Store, company);
        Assert.Equal(SalesOrderLineReferenceOutcomeV1.Approved, (await port.GetApprovedLineAsync(approved.Draft.Id, line.LineId)).Outcome);
        Assert.Single((await port.ListApprovedLinesAsync(10, null)).Items);

        await world.Service().ForceCloseDraftAsync(approved.Draft.Id, new SalesDraftForceCloseRequest("5", "neden", token), Metadata("k-close"));

        var answer = await port.GetApprovedLineAsync(approved.Draft.Id, line.LineId);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.NotApproved, "Closed"), (answer.Outcome, answer.ApprovalState));
        Assert.Equal(SalesOrderLineReferenceOutcomeV1.NotApproved,
            (await port.GetApprovedLineForLoadingAsync(approved.Draft.Id, line.LineId, Day)).Outcome);
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
        Assert.Equal("Closed", Assert.Single((await world.Service().GetDraftListAsync(new SalesOrderDraftListQuery())).Items, item => item.Id == approved.Draft.Id).State);
        world.Deliveries = new FakeDeliveries(order => new(Company, order, []));
        Assert.Equal("1.25", Assert.Single((await world.Service().GetDraftDeliveryProgressAsync(approved.Draft.Id)).Lines).Remaining);
    }
}
