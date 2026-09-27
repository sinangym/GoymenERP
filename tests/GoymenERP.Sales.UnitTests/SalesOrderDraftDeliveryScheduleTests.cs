using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// DEC-SAL-003 (KILITLI): "Termin üst bilgi, satır ve miktar dilimleriyle izlenir; termin güncellemesi planlama/sevkiyat
/// rolündedir ve yeniden onay gerektirmez, ancak auditlidir." The domain half: a delivery-schedule update moves the header,
/// line and existing-slice dates only (no slice added, removed or re-split), leaves the approval axis and the commercial
/// revision where they are, advances the version by exactly one, records an append-only schedule, and a stored schedule
/// restores only when it fits the draft. Every identity is synthetic.
/// </summary>
public sealed class SalesOrderDraftDeliveryScheduleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 26, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly DateOnly Day = new(2026, 9, 26);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid KgUnit = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid KgProfile = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid SliceA = Guid.Parse("a1a1a1a1-a1a1-41a1-81a1-a1a1a1a1a1a1");
    private static readonly Guid SliceB = Guid.Parse("b2b2b2b2-b2b2-42b2-82b2-b2b2b2b2b2b2");
    private const string Planner = "synthetic-planner";

    [Fact]
    public void An_approved_order_moves_its_dates_without_re_approval_and_advances_the_version_by_exactly_one()
    {
        var draft = ApprovedDraft(out var line, out var plain);
        var approval = draft.Approval;

        var recorded = draft.RescheduleDelivery(draft.Version, Day.AddDays(20), [
            new LineScheduleInput(line.Id, Day.AddDays(21), [new(SliceB, Day.AddDays(9)), new(SliceA, Day.AddDays(8))]),
            new LineScheduleInput(plain.Id, null, []),
        ], $" {Planner} ", T0.AddHours(1));

        // DEC-SAL-003: no re-approval — the axis, its facts and the commercial revision stay; only the version moves.
        Assert.Equal((InternalApprovalState.Approved, 0, 6L), (draft.ApprovalState, draft.Revision, draft.Version));
        Assert.Equal(approval, draft.Approval);
        Assert.Equal((Day.AddDays(20), Day.AddDays(21), (DateOnly?)null), (draft.Header.RequestedDeliveryDate, line.RequestedDeliveryDate, plain.RequestedDeliveryDate));
        // Dates only: the same slices, in the same order, with the same quantities; only their dates moved.
        Assert.Equal([(SliceA, "0.5", Day.AddDays(8)), (SliceB, "0.75", Day.AddDays(9))],
            line.RequestedDeliverySlices.Select(slice => (slice.SliceId, slice.QuantityText, slice.RequestedDeliveryDate)));
        Assert.True(line.UnplannedQuantity.IsZero);
        // The recorded schedule is complete and attributed; the history keeps schedule 0 as created.
        Assert.Equal((1, Planner, T0.AddHours(1).ToUniversalTime()), (recorded.ScheduleNo, recorded.ChangedBy, recorded.ChangedAtUtc));
        Assert.Equal([0, 1], draft.DeliveryScheduleHistory.Select(schedule => schedule.ScheduleNo));
        var original = draft.DeliveryScheduleHistory[0];
        Assert.Equal((Day.AddDays(10), (string?)null), (original.RequestedDeliveryDate, original.ChangedBy));
        Assert.Equal([Day.AddDays(1), Day.AddDays(2)], original.Lines[0].Slices.Select(slice => slice.RequestedDeliveryDate));
    }

    [Theory]
    [InlineData("Preparing")]
    [InlineData("PendingApproval")]
    [InlineData("Rejected")]
    public void Any_state_but_withdrawn_accepts_a_date_change_and_the_state_does_not_move(string state)
    {
        var draft = Draft(out var line, out var plain);
        if (state != "Preparing") draft.Submit(draft.Version, "synthetic-preparer", T0);
        if (state == "Rejected") draft.Reject(draft.Version, "synthetic-approver", null, T0);
        var before = draft.ApprovalState;

        draft.RescheduleDelivery(draft.Version, Day.AddDays(30), [Same(line), Same(plain)], Planner, T0);

        Assert.Equal((before, Day.AddDays(30)), (draft.ApprovalState, draft.Header.RequestedDeliveryDate));
    }

    [Fact]
    public void A_withdrawn_draft_is_a_state_conflict_and_nothing_moves()
    {
        var draft = Draft(out var line, out var plain);
        draft.Withdraw(draft.Version, "synthetic-preparer", null, T0);
        var version = draft.Version;

        Assert.Equal(SalesRuleCodes.StateConflict,
            CodeOf(() => draft.RescheduleDelivery(version, Day.AddDays(30), [Same(line), Same(plain)], Planner, T0)));
        Assert.Equal((version, Day.AddDays(10), 0), (draft.Version, draft.Header.RequestedDeliveryDate, draft.DeliverySchedules.Count));
    }

    [Fact]
    public void A_stale_version_is_a_version_conflict()
    {
        var draft = Draft(out var line, out var plain);
        Assert.Throws<SalesVersionConflictException>(() =>
            draft.RescheduleDelivery(draft.Version - 1, Day.AddDays(30), [Same(line), Same(plain)], Planner, T0));
    }

    [Fact]
    public void Every_line_must_be_named_exactly_once_and_no_other()
    {
        var draft = Draft(out var line, out var plain);
        var version = draft.Version;
        Assert.Equal(SalesRuleCodes.ScheduleLinesMismatch, CodeOf(() => draft.RescheduleDelivery(version, null, [Same(line)], Planner, T0)));
        Assert.Equal(SalesRuleCodes.ScheduleLinesMismatch, CodeOf(() => draft.RescheduleDelivery(version, null, [Same(line), Same(line)], Planner, T0)));
        Assert.Equal(SalesRuleCodes.ScheduleLinesMismatch, CodeOf(() => draft.RescheduleDelivery(version, null,
            [Same(line), new LineScheduleInput(Guid.NewGuid(), null, [])], Planner, T0)));
        Assert.Equal(version, draft.Version);
    }

    /// <summary>Dates only: re-splitting is not in DEC-SAL-003's text, so a slice is never added, dropped or duplicated here.</summary>
    [Fact]
    public void Slices_must_be_exactly_the_lines_current_slices_so_nothing_is_added_removed_or_re_split()
    {
        var draft = Draft(out var line, out var plain);
        var version = draft.Version;
        LineScheduleInput With(params SliceScheduleInput[] slices) => new(line.Id, line.RequestedDeliveryDate, slices);

        var missing = Assert.Throws<SalesRuleException>(() => draft.RescheduleDelivery(version, null,
            [With(new SliceScheduleInput(SliceA, Day)), Same(plain)], Planner, T0));
        Assert.Equal((SalesRuleCodes.ScheduleSlicesMismatch, (Guid?)line.Id), (missing.Code, missing.LineId));
        Assert.Equal(SalesRuleCodes.ScheduleSlicesMismatch, CodeOf(() => draft.RescheduleDelivery(version, null,
            [With(new(SliceA, Day), new(SliceB, Day), new(Guid.NewGuid(), Day)), Same(plain)], Planner, T0)));
        Assert.Equal(SalesRuleCodes.ScheduleSlicesMismatch, CodeOf(() => draft.RescheduleDelivery(version, null,
            [With(new(SliceA, Day), new(SliceA, Day)), Same(plain)], Planner, T0)));
        Assert.Equal(SalesRuleCodes.ScheduleSlicesMismatch, CodeOf(() => draft.RescheduleDelivery(version, null,
            [Same(line), new LineScheduleInput(plain.Id, null, [new(SliceA, Day)])], Planner, T0)));
        Assert.Equal((version, 2), (draft.Version, line.RequestedDeliverySlices.Count));
    }

    [Fact]
    public void A_change_that_moves_no_date_is_refused_and_records_nothing()
    {
        var draft = Draft(out var line, out var plain);
        var version = draft.Version;
        Assert.Equal(SalesRuleCodes.ScheduleNoChange,
            CodeOf(() => draft.RescheduleDelivery(version, draft.Header.RequestedDeliveryDate, [Same(line), Same(plain)], Planner, T0)));
        Assert.Equal((version, 0), (draft.Version, draft.DeliverySchedules.Count));
    }

    [Fact]
    public void A_blank_actor_is_refused_before_anything_moves()
    {
        var draft = Draft(out var line, out var plain);
        Assert.Throws<ArgumentException>(() => draft.RescheduleDelivery(draft.Version, Day.AddDays(30), [Same(line), Same(plain)], " ", T0));
        Assert.Equal(Day.AddDays(10), draft.Header.RequestedDeliveryDate);
    }

    [Fact]
    public void A_stored_schedule_restores_the_current_dates_before_the_approval_axis_and_counts_one_version_step()
    {
        var source = ApprovedDraft(out var line, out var plain);
        var recorded = source.RescheduleDelivery(source.Version, Day.AddDays(20), [
            new LineScheduleInput(line.Id, Day.AddDays(21), [new(SliceA, Day.AddDays(8)), new(SliceB, Day.AddDays(9))]),
            Same(plain),
        ], Planner, T0);

        var rebuilt = Draft(out var rebuiltLine, out _, line.Id, plain.Id);
        rebuilt.RestoreDeliverySchedule(recorded);
        rebuilt.RestoreApproval(InternalApprovalState.Approved, source.Approval, source.Version, T0);

        Assert.Equal((Day.AddDays(20), Day.AddDays(21), 6L), (rebuilt.Header.RequestedDeliveryDate, rebuiltLine.RequestedDeliveryDate, rebuilt.Version));
        Assert.Equal([Day.AddDays(8), Day.AddDays(9)], rebuiltLine.RequestedDeliverySlices.Select(slice => slice.RequestedDeliveryDate));
        Assert.Equal([0, 1], rebuilt.DeliveryScheduleHistory.Select(schedule => schedule.ScheduleNo));
        Assert.Equal(Day.AddDays(10), rebuilt.DeliveryScheduleHistory[0].RequestedDeliveryDate);

        // The version floor counts the schedule: a stored version that cannot account for it is refused.
        var tooLow = Draft(out _, out _, line.Id, plain.Id);
        tooLow.RestoreDeliverySchedule(recorded);
        Assert.Throws<InvalidOperationException>(() => tooLow.RestoreApproval(InternalApprovalState.Approved, source.Approval, 5, T0));
    }

    [Fact]
    public void A_stored_schedule_that_does_not_fit_is_refused_not_repaired()
    {
        var source = Draft(out var line, out var plain);
        var recorded = source.RescheduleDelivery(source.Version, Day.AddDays(20), [Same(line), Same(plain)], Planner, T0);

        // Out of order, without an actor, naming other lines or other slices, or after the approval axis.
        Assert.Throws<InvalidOperationException>(() => Draft(out _, out _, line.Id, plain.Id).RestoreDeliverySchedule(recorded with { ScheduleNo = 2 }));
        Assert.Throws<InvalidOperationException>(() => Draft(out _, out _, line.Id, plain.Id).RestoreDeliverySchedule(recorded with { ChangedBy = null }));
        Assert.Throws<InvalidOperationException>(() => Draft(out _, out _).RestoreDeliverySchedule(recorded));
        var otherSlice = recorded with
        {
            Lines = [recorded.Lines[0] with { Slices = [recorded.Lines[0].Slices[0] with { SliceId = Guid.NewGuid() }, recorded.Lines[0].Slices[1]] }, recorded.Lines[1]],
        };
        Assert.Throws<InvalidOperationException>(() => Draft(out _, out _, line.Id, plain.Id).RestoreDeliverySchedule(otherSlice));
        var late = Draft(out _, out _, line.Id, plain.Id);
        late.RestoreApproval(InternalApprovalState.Preparing, ApprovalFacts.None, late.Version, T0);
        Assert.Throws<InvalidOperationException>(() => late.RestoreDeliverySchedule(recorded));
    }

    private static string CodeOf(Action action) => Assert.Throws<SalesRuleException>(action).Code;

    private static LineScheduleInput Same(SalesOrderDraftLine line) =>
        new(line.Id, line.RequestedDeliveryDate, [.. line.RequestedDeliverySlices.Select(slice => new SliceScheduleInput(slice.SliceId, slice.RequestedDeliveryDate))]);

    private static SalesDraftHeader Header() => new(Guid.Parse("33333333-3333-4333-8333-333333333333"),
        Guid.Parse("44444444-4444-4444-8444-444444444444"), Guid.Parse("55555555-5555-4555-8555-555555555555"), Day, Day.AddDays(10));

    private static MoneyProfileSnapshot Money() => new(Company, Guid.Parse("55555555-5555-4555-8555-555555555555"),
        SalesOrderDraft.MoneyOperation, Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), "3", Day, true, new DateOnly(2026, 9, 1), null, 2);

    private static SalesDraftLineInput Line(Guid id, string quantity, IReadOnlyList<DeliverySliceInput>? slices, DateOnly? date)
    {
        var unit = new UnitProfileSnapshot(Company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, new DateOnly(2026, 9, 1), null);
        var binding = new UnitBindingSnapshot(Company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Guid.Parse("99999999-9999-4999-8999-999999999999"), "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        return new SalesDraftLineInput(id, product, unit, quantity, decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture),
            date, RequestedDeliverySlices: slices);
    }

    /// <summary>A two-line draft at version 3: line 1 with two slices (0.5 + 0.75 of 1.25), line 2 without slices or date.</summary>
    private static SalesOrderDraft Draft(out SalesOrderDraftLine sliced, out SalesOrderDraftLine plain, Guid? slicedId = null, Guid? plainId = null)
    {
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), Company, "SO-2026-000001", Header(), Money(), T0);
        sliced = draft.AddLine(draft.Version, Line(slicedId ?? Guid.NewGuid(), "1.25",
            [new(SliceA, KgUnit, "0.5", 0.5m, Day.AddDays(1)), new(SliceB, KgUnit, "0.75", 0.75m, Day.AddDays(2))], Day.AddDays(3)), T0);
        plain = draft.AddLine(draft.Version, Line(plainId ?? Guid.NewGuid(), "2", null, null), T0);
        return draft;
    }

    private static SalesOrderDraft ApprovedDraft(out SalesOrderDraftLine sliced, out SalesOrderDraftLine plain)
    {
        var draft = Draft(out sliced, out plain);
        draft.Submit(draft.Version, "synthetic-preparer", T0);
        draft.Approve(draft.Version, "synthetic-approver", T0);
        return draft;
    }
}
