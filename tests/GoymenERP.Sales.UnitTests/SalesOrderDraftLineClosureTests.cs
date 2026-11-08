using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D17 / SAT-D168 / SAT-D215 / SAT-D216, the domain half: a line closure records the line, the measured quantity, the shipped
/// total and the remainder counted as cancelled with exactly one version step and nothing else moving; the shipper never closes;
/// a cancelled order, an unknown or already closed line, no shipment and no shortfall are refused; a stored closure restores
/// before the approval axis only when it fits and is counted by the version floor. Every identity is synthetic.
/// </summary>
public sealed class SalesOrderDraftLineClosureTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 8, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly DateOnly Day = new(2026, 11, 8);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid KgUnit = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid KgProfile = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private const string Closer = "synthetic-closer-2";
    private const string Shipper = "synthetic-shipper";

    [Fact]
    public void A_closure_records_the_cancelled_remainder_with_one_version_step_and_nothing_else_moves()
    {
        var draft = ApprovedDraft(out var line);
        var approval = draft.Approval;
        Assert.False(draft.IsLineClosed(line.Id));

        var facts = draft.CloseLine(4, $" {Closer} ", Input(line, "90"), T0.AddHours(1));

        Assert.Equal(new LineClosureFacts(line.Id, 0, "100", "90", "10", DeliveryToleranceBand.Warn, Closer, T0.AddHours(1).ToUniversalTime()), facts);
        Assert.Equal((5L, SalesDraftState.Draft, InternalApprovalState.Approved, approval, "100"),
            (draft.Version, draft.State, draft.ApprovalState, draft.Approval, draft.Lines[0].QuantityText));
        Assert.True(draft.IsLineClosed(line.Id));
        Assert.False(draft.IsLineClosed(Guid.NewGuid()));
        Assert.Equal(facts, Assert.Single(draft.LineClosures));

        // At most one closure per line.
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.CloseLine(5, Closer, Input(line, "80"), T0)));
    }

    [Fact]
    public void The_shipper_never_closes_and_wrong_state_or_input_is_refused()
    {
        var draft = ApprovedDraft(out var line);
        Assert.Equal(SalesRuleCodes.LineClosureSelfClosure,
            CodeOf(() => draft.CloseLine(4, Shipper, Input(line, "90", "synthetic-other", Shipper), T0)));
        Assert.Throws<ArgumentException>(() => draft.CloseLine(4, Closer, Input(line, "90") with { ShippedBy = [] }, T0));
        Assert.Throws<ArgumentException>(() => draft.CloseLine(4, Closer, Input(line, "90", " "), T0));
        // No shipment and no shortfall are no closure; an unknown line, a stale version and a value outside the scale are refused.
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.CloseLine(4, Closer, Input(line, "0"), T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.CloseLine(4, Closer, Input(line, "100"), T0)));
        Assert.Equal(SalesRuleCodes.LineClosureLineUnknown,
            CodeOf(() => draft.CloseLine(4, Closer, Input(line, "90") with { LineId = Guid.NewGuid() }, T0)));
        Assert.Throws<SalesVersionConflictException>(() => draft.CloseLine(3, Closer, Input(line, "90"), T0));
        Assert.Throws<ArgumentException>(() => draft.CloseLine(4, Closer, Input(line, "90.0001"), T0));
        Assert.Equal((4L, 0), (draft.Version, draft.LineClosures.Count));

        // A never-approved order and a cancelled order refuse it.
        var fresh = Draft(out var freshLine);
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => fresh.CloseLine(fresh.Version, Closer, Input(freshLine, "90"), T0)));
        draft.Cancel(4, "synthetic-canceller", "neden", new CancellationImpactSnapshot("t", [new("inventory.active-allocations", "None")]), T0);
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.CloseLine(5, Closer, Input(line, "90"), T0)));
    }

    [Fact]
    public void A_stored_closure_restores_before_the_approval_axis_only_when_it_fits_and_counts_one_step()
    {
        var source = ApprovedDraft(out var line);
        var facts = source.CloseLine(4, Closer, Input(line, "90"), T0);

        var rebuilt = Draft(out _, line.Id);
        rebuilt.RestoreLineClosure(facts);
        rebuilt.RestoreApproval(InternalApprovalState.Approved, source.Approval, 5, T0);
        Assert.Equal((5L, facts), (rebuilt.Version, Assert.Single(rebuilt.LineClosures)));
        Assert.True(rebuilt.IsLineClosed(line.Id));

        var tooLow = Draft(out _, line.Id);
        tooLow.RestoreLineClosure(facts);
        Assert.Throws<InvalidOperationException>(() => tooLow.RestoreApproval(InternalApprovalState.Approved, source.Approval, 4, T0));

        Assert.Throws<InvalidOperationException>(() => Draft(out _).RestoreLineClosure(facts));
        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreLineClosure(facts with { BasisRevision = 1 }));
        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreLineClosure(facts with { ShippedAtClosure = "0" }));
        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreLineClosure(facts with { ShippedAtClosure = "100", CancelledRemainder = "0" }));
        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreLineClosure(facts with { CancelledRemainder = "11" }));
        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreLineClosure(facts with { ShippedAtClosure = "90.0" }));
        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreLineClosure(facts with { Band = (DeliveryToleranceBand)99 }));
        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreLineClosure(facts with { ClosedBy = " " }));
        var twice = Draft(out _, line.Id);
        twice.RestoreLineClosure(facts);
        Assert.Throws<InvalidOperationException>(() => twice.RestoreLineClosure(facts));
        var late = Draft(out _, line.Id);
        late.RestoreApproval(InternalApprovalState.Preparing, ApprovalFacts.None, late.Version, T0);
        Assert.Throws<InvalidOperationException>(() => late.RestoreLineClosure(facts));
    }

    [Fact]
    public void A_closed_line_is_not_revised_removed_or_changed_through_the_remainder_until_a_reopen_exists()
    {
        var draft = ApprovedDraft(out var line);
        draft.CloseLine(4, Closer, Input(line, "90"), T0);
        var version = draft.Version;

        // SAT-D167 / SAT-D217: a changed quantity of the closed line, a removal and a remainder request are the terminal state conflict.
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.ReviseQuantities(version,
            [new LineQuantityInput(line.Id, "95", 95m)], "synthetic-reviser", "SENTETIK", T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.RequireClosedLinesUnchanged(
            [new LineQuantityInput(line.Id, "95", 95m)])));
        draft.RequireClosedLinesUnchanged([new LineQuantityInput(line.Id, "100", 100m)]);
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.ReviseLines(version, [],
            [new LineRemovalInput(line.Id)], "synthetic-reviser", "SENTETIK", T0, Day)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.RequireRemainderChangeable(version, line.Id, "SENTETIK")));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.RequestRemainderChange(version, line.Id, RemainderChangeKind.Cancel,
            null, null, "SENTETIK", new RemainderFloor(90m, 90m, 90m), "synthetic-requester", T0)));
        Assert.Equal((version, "100", LineRemainderState.Open), (draft.Version, draft.Lines[0].QuantityText, draft.Lines[0].Remainder));
    }

    [Fact]
    public void A_remainder_change_requested_before_the_closure_is_not_approved_afterwards()
    {
        var draft = ApprovedDraft(out var line);
        draft.RequestRemainderChange(4, line.Id, RemainderChangeKind.Revise, "80", 80m, "SENTETIK", new RemainderFloor(60m, 60m, 60m),
            "synthetic-requester", T0);
        draft.CloseLine(5, Closer, Input(line, "90"), T0);

        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.ApproveRemainderChange(6, 1, new RemainderFloor(60m, 60m, 60m),
            "synthetic-approver", T0)));
        Assert.Equal(("100", LineRemainderState.Open), (draft.Lines[0].QuantityText, draft.Lines[0].Remainder));
    }

    [Fact]
    public void A_force_closure_records_a_closed_line_with_its_own_closure_triple_and_round_trips()
    {
        var draft = ApprovedDraft(out var line);
        var closure = draft.CloseLine(4, Closer, Input(line, "90"), T0);

        // The owner's current total (95) is validated, but a closed line is recorded as its closure measured it (100 / 90 / 10):
        // the remainder it cancelled is not counted a second time and the snapshot stays internally consistent.
        var facts = draft.ForceClose(5, "synthetic-force-closer", "SENTETIK", NoBlockImpact(), [new ForceClosureLineInput(line.Id, 95m)], T0);
        var snapshot = Assert.Single(facts.Lines);
        Assert.Equal(("100", "90", "10"), (snapshot.Ordered, snapshot.Shipped, snapshot.ClosedRemainder));
        Assert.Throws<ArgumentException>(() => ApprovedClosedDraft(out var other, out var otherLine, out _)
            .ForceClose(other.Version, "synthetic-force-closer", "SENTETIK", NoBlockImpact(), [new ForceClosureLineInput(otherLine.Id, 101m)], T0));

        // Save -> restore: closures first, then the force-closure, then the approval axis.
        var rebuilt = Draft(out _, line.Id);
        rebuilt.RestoreLineClosure(closure);
        rebuilt.RestoreForceClosure(facts);
        rebuilt.RestoreApproval(InternalApprovalState.Approved, draft.Approval, draft.Version, T0);
        Assert.Equal((draft.Version, SalesDraftState.Closed, facts), (rebuilt.Version, rebuilt.State, rebuilt.ForceClosure));
        Assert.Equal(closure, Assert.Single(rebuilt.LineClosures));

        // The old inconsistent shape (the current shipped beside the closure's remainder) is refused on load.
        var inconsistent = facts with { Lines = [snapshot with { Shipped = "95" }] };
        var refused = Draft(out _, line.Id);
        refused.RestoreLineClosure(closure);
        Assert.Throws<InvalidOperationException>(() => refused.RestoreForceClosure(inconsistent));
        var otherRemainder = Draft(out _, line.Id);
        otherRemainder.RestoreLineClosure(closure);
        Assert.Throws<InvalidOperationException>(() => otherRemainder.RestoreForceClosure(facts with { Lines = [snapshot with { ClosedRemainder = "5" }] }));
    }

    [Fact]
    public void A_force_closure_of_an_order_with_a_closed_and_an_open_line_keeps_each_lines_own_rule()
    {
        var draft = ApprovedClosedDraft(out var source, out var closedLine, out var closure);
        Assert.Same(draft, source);
        var open = draft.Lines.Single(candidate => candidate.Id != closedLine.Id);
        var facts = draft.ForceClose(draft.Version, "synthetic-force-closer", "SENTETIK", NoBlockImpact(),
            [new ForceClosureLineInput(closedLine.Id, 100m), new ForceClosureLineInput(open.Id, 60m)], T0);

        // The closed line keeps 100/90/10 even though the owner now names 100 (shipped moved after the closure); the open line is 100-60.
        Assert.Equal([("100", "90", "10"), ("100", "60", "40")],
            facts.Lines.OrderBy(entry => entry.LineNo).Select(entry => (entry.Ordered, entry.Shipped, entry.ClosedRemainder)).ToArray());

        var rebuilt = Draft(out _, closedLine.Id);
        rebuilt.AddLine(rebuilt.Version, Line(open.Id, "100"), T0);
        rebuilt.RestoreLineClosure(closure);
        rebuilt.RestoreForceClosure(facts);
        rebuilt.RestoreApproval(InternalApprovalState.Approved, draft.Approval, draft.Version, T0);
        Assert.Equal(SalesDraftState.Closed, rebuilt.State);
    }

    [Fact]
    public void A_line_closes_only_while_the_order_is_approved_with_no_pending_revision_or_fallback()
    {
        // A quantity revision (100 -> 120) leaves the order in re-approval: the closure is refused, then also while it is pending.
        var draft = ApprovedDraft(out var line);
        draft.ReviseQuantities(draft.Version, [new LineQuantityInput(line.Id, "120", 120m)], "synthetic-reviser", "SENTETIK", T0);
        Assert.NotEqual(InternalApprovalState.Approved, draft.ApprovalState);
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.CloseLine(draft.Version, Closer, Input(line, "90") with { Ordered = "120" }, T0)));
        draft.Submit(draft.Version, "synthetic-preparer", T0);
        Assert.Equal(InternalApprovalState.PendingApproval, draft.ApprovalState);
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.RequireLineClosable(draft.Version, line.Id)));
        Assert.Empty(draft.LineClosures);
        // Once approved again, the line closes over the current approved quantity.
        draft.Approve(draft.Version, "synthetic-approver", T0);
        Assert.Equal("120", draft.CloseLine(draft.Version, Closer, Input(line, "90") with { Ordered = "120" }, T0).Ordered);

        // A customer-rejection fallback (the order continues on an older accepted revision) is refused too.
        var fallback = ApprovedDraft(out var fallbackLine, acceptance: true);
        fallback.RecordCustomerAcceptance(fallback.Version, "synthetic-approver", T0);
        fallback.ReviseQuantities(fallback.Version, [new LineQuantityInput(fallbackLine.Id, "120", 120m)], "synthetic-reviser", "SENTETIK", T0);
        fallback.Submit(fallback.Version, "synthetic-preparer", T0);
        fallback.Approve(fallback.Version, "synthetic-approver", T0);
        fallback.RecordCustomerRejection(fallback.Version, "synthetic-approver", "SENTETIK", T0);
        Assert.NotNull(fallback.CustomerAcceptedFallback);
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => fallback.RequireLineClosable(fallback.Version, fallbackLine.Id)));
    }

    [Fact]
    public void A_customer_rejection_is_refused_once_a_line_is_closed_and_an_approval_never_changes_a_closed_line()
    {
        var draft = ApprovedDraft(out var line, acceptance: true);
        draft.RecordCustomerAcceptance(draft.Version, "synthetic-approver", T0);
        draft.ReviseQuantities(draft.Version, [new LineQuantityInput(line.Id, "120", 120m)], "synthetic-reviser", "SENTETIK", T0);
        draft.Submit(draft.Version, "synthetic-preparer", T0);
        draft.Approve(draft.Version, "synthetic-approver", T0);
        draft.CloseLine(draft.Version, Closer, Input(line, "90") with { Ordered = "120", BasisRevision = draft.Revision }, T0);
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.RecordCustomerRejection(draft.Version, "synthetic-approver", "SENTETIK", T0)));

        // Defence in depth: a stored state in which the closed line's quantity differs from the closure's is refused at submit and at approve.
        var source = ApprovedDraft(out var sourceLine);
        var closure = source.CloseLine(4, Closer, Input(sourceLine, "90"), T0);
        var pending = Draft(out var pendingLine, sourceLine.Id);
        pending.RestoreLineClosure(closure);
        pending.RestoreApproval(InternalApprovalState.PendingApproval, new ApprovalFacts("synthetic-preparer", T0.ToUniversalTime(), null, null, null), 10, T0);
        ReplaceQuantity(pendingLine, 95m);
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => pending.Approve(10, "synthetic-approver", T0)));

        var preparing = Draft(out var preparingLine, sourceLine.Id);
        preparing.RestoreLineClosure(closure);
        preparing.RestoreApproval(InternalApprovalState.Preparing, ApprovalFacts.None, 10, T0);
        ReplaceQuantity(preparingLine, 95m);
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => preparing.Submit(10, "synthetic-preparer", T0)));
    }

    [Fact]
    public void A_stored_closure_whose_ordered_text_is_not_the_lines_quantity_is_refused_on_restore()
    {
        var source = ApprovedDraft(out var line);
        var facts = source.CloseLine(4, Closer, Input(line, "90"), T0);
        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreLineClosure(
            facts with { Ordered = "120", CancelledRemainder = "30" }));
    }

    private static void ReplaceQuantity(SalesOrderDraftLine line, decimal value) =>
        typeof(SalesOrderDraftLine).GetMethod("ReplaceQuantity", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(line, [Kg(value), value.ToString(System.Globalization.CultureInfo.InvariantCulture)]);

    private static ForceClosureImpactSnapshot NoBlockImpact() =>
        new(new string('f', 64), [new("inventory.active-allocations", "None", false)]);

    /// <summary>A two-line approved order whose first line (100 kg) was closed at 90.</summary>
    private static SalesOrderDraft ApprovedClosedDraft(out SalesOrderDraft self, out SalesOrderDraftLine closedLine, out LineClosureFacts closure)
    {
        var draft = Draft(out closedLine);
        draft.AddLine(draft.Version, Line(Guid.NewGuid(), "100"), T0);
        draft.Submit(draft.Version, "synthetic-preparer", T0);
        draft.Approve(draft.Version, "synthetic-approver", T0);
        closure = draft.CloseLine(draft.Version, Closer, Input(closedLine, "90"), T0);
        self = draft;
        return draft;
    }

    private static LineClosureInput Input(SalesOrderDraftLine line, string shipped, params string[] shippedBy) =>
        new(line.Id, 0, "100", decimal.Parse(shipped, System.Globalization.CultureInfo.InvariantCulture), DeliveryToleranceBand.Warn,
            shippedBy.Length == 0 ? [Shipper] : shippedBy);

    private static Quantity Kg(decimal value) => Quantity.Of(value, GoymenERP.BuildingBlocks.Quantities.QuantityUnit.Kilogram);

    private static string CodeOf(Action action) => Assert.Throws<SalesRuleException>(action).Code;

    private static SalesDraftHeader Header() => new(Guid.Parse("33333333-3333-4333-8333-333333333333"),
        Guid.Parse("44444444-4444-4444-8444-444444444444"), Guid.Parse("55555555-5555-4555-8555-555555555555"), Day, Day.AddDays(10), DueTerms: TestDueTerms.PointDue30);

    private static MoneyProfileSnapshot Money() => new(Company, Guid.Parse("55555555-5555-4555-8555-555555555555"),
        SalesOrderDraft.MoneyOperation, Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), "3", Day, true, new DateOnly(2026, 9, 1), null, 2);

    private static SalesDraftLineInput Line(Guid id, string quantity)
    {
        var unit = new UnitProfileSnapshot(Company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, new DateOnly(2026, 9, 1), null);
        var binding = new UnitBindingSnapshot(Company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Guid.Parse("99999999-9999-4999-8999-999999999999"), "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        return new SalesDraftLineInput(id, product, unit, quantity, decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), Day.AddDays(3));
    }

    /// <summary>A one-line draft (100 kg) at version 2.</summary>
    private static SalesOrderDraft Draft(out SalesOrderDraftLine line, Guid? lineId = null, bool acceptance = false)
    {
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), Company, "SO-2026-000001", Header(), Money(), T0, acceptance);
        line = draft.AddLine(draft.Version, Line(lineId ?? Guid.NewGuid(), "100"), T0);
        return draft;
    }

    /// <summary>The one-line draft submitted and approved: version 4.</summary>
    private static SalesOrderDraft ApprovedDraft(out SalesOrderDraftLine line, bool acceptance = false)
    {
        var draft = Draft(out line, acceptance: acceptance);
        draft.Submit(draft.Version, "synthetic-preparer", T0);
        draft.Approve(draft.Version, "synthetic-approver", T0);
        return draft;
    }
}
