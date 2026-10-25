using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SINAN_KARARLARI_2026-10-21 No 30 / SAT-D12, the domain half: only an APPROVED order without a pending remainder change is
/// force-closed, with a required reason, the non-blocking impact preview it was decided on and every line's shipped total; the
/// lifecycle moves Draft → Closed, the version by exactly one, the closed remainder (ordered − shipped, exact) is recorded per
/// line and NOTHING else moves — the line quantities, the approval facts, the revision and the schedules stay. A closed order
/// refuses every mutation entry; a stored closure restores only when it fits. Every identity is synthetic.
/// </summary>
public sealed class SalesOrderDraftForceClosureTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly DateOnly Day = new(2026, 10, 25);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid KgUnit = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid KgProfile = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private const string Closer = "synthetic-closer";

    private static readonly ForceClosureImpactSnapshot Impact = new(new string('f', 64),
    [
        new("inventory.active-allocations", "None", false), new("purchasing.linked-orders", "NoSource", false),
        new("invoicing.uninvoiced-dispatch-notes", "Present", false),
    ]);

    [Fact]
    public void An_approved_order_is_closed_with_its_reason_impact_and_closed_remainder_and_nothing_of_its_history_moves()
    {
        var draft = ApprovedDraft(out var line);
        var approval = draft.Approval;
        var lines = draft.Lines.Select(candidate => (candidate.Id, candidate.QuantityText, candidate.Quantity, candidate.RequestedDeliveryDate)).ToArray();

        var facts = draft.ForceClose(draft.Version, $" {Closer} ", "  SENTETIK kalan istenmiyor  ", Impact,
            [new ForceClosureLineInput(line.Id, 0.500m)], T0.AddHours(1));

        Assert.Equal((SalesDraftState.Closed, InternalApprovalState.Approved, 0, 5L), (draft.State, draft.ApprovalState, draft.Revision, draft.Version));
        Assert.Equal(approval, draft.Approval);
        // The line quantities do not move: the closed remainder is a separate axis (DEC-SAL-001).
        Assert.Equal(lines, draft.Lines.Select(candidate => (candidate.Id, candidate.QuantityText, candidate.Quantity, candidate.RequestedDeliveryDate)));
        Assert.Equal((0, Closer, T0.AddHours(1).ToUniversalTime(), "SENTETIK kalan istenmiyor", Impact),
            (facts.Revision, facts.ClosedBy, facts.ClosedAtUtc, facts.Reason, facts.Impact));
        // Exact, never rounded; trailing zeros dropped (0.500 → 0.5, 1.25 − 0.5 → 0.75).
        Assert.Equal(new ForceClosureLineSnapshot(line.Id, 1, "1.25", "0.5", "0.75"), Assert.Single(facts.Lines));
        Assert.Same(facts, draft.ForceClosure);
        Assert.Null(draft.Cancellation);
    }

    [Theory]
    [InlineData("0", "1.25")]
    [InlineData("1.25", "0")]
    [InlineData("1.249", "0.001")]
    public void The_closed_remainder_is_ordered_minus_shipped_exactly(string shipped, string remainder)
    {
        var draft = ApprovedDraft(out var line);
        var value = decimal.Parse(shipped, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(remainder, SalesOrderDraft.ClosedRemainderText(line, value));

        var facts = draft.ForceClose(draft.Version, Closer, "neden", Impact, [new ForceClosureLineInput(line.Id, value)], T0);

        Assert.Equal((shipped, remainder), (facts.Lines[0].Shipped, facts.Lines[0].ClosedRemainder));
    }

    [Theory]
    [InlineData("Preparing")]
    [InlineData("PendingApproval")]
    [InlineData("Rejected")]
    [InlineData("Withdrawn")]
    [InlineData("RevisedBackToPreparing")]
    [InlineData("Cancelled")]
    [InlineData("PendingRemainderChange")]
    public void Only_an_approved_live_order_without_a_pending_remainder_change_is_closed_every_other_state_is_a_state_conflict(string state)
    {
        var draft = Draft(out var line);
        switch (state)
        {
            case "PendingApproval":
                draft.Submit(draft.Version, "synthetic-preparer", T0);
                break;
            case "Rejected":
                draft.Submit(draft.Version, "synthetic-preparer", T0);
                draft.Reject(draft.Version, "synthetic-approver", null, T0);
                break;
            case "Withdrawn":
                draft.Withdraw(draft.Version, "synthetic-preparer", null, T0);
                break;
            case "RevisedBackToPreparing":
                draft.Submit(draft.Version, "synthetic-preparer", T0);
                draft.Approve(draft.Version, "synthetic-approver", T0);
                draft.ReviseCommercial(draft.Version, [new LineCommercialInput(line.Id, new CommercialDraftFields("10", KgUnit, null))], "synthetic-pricer", T0);
                break;
            case "Cancelled":
                draft.Submit(draft.Version, "synthetic-preparer", T0);
                draft.Approve(draft.Version, "synthetic-approver", T0);
                draft.Cancel(draft.Version, "synthetic-canceller", "neden",
                    new CancellationImpactSnapshot("t", [new("inventory.active-allocations", "None")]), T0);
                break;
            case "PendingRemainderChange":
                draft.Submit(draft.Version, "synthetic-preparer", T0);
                draft.Approve(draft.Version, "synthetic-approver", T0);
                draft.RequestRemainderChange(draft.Version, line.Id, RemainderChangeKind.Cancel, null, null, "SENTETIK",
                    new RemainderFloor(0.5m, 0.5m, 0.5m), "synthetic-requester", T0);
                break;
        }

        var (stateBefore, version) = (draft.State, draft.Version);
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.ForceClose(draft.Version, Closer, "neden", Impact,
            [new ForceClosureLineInput(line.Id, 0m)], T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.RequireForceClosable(draft.Version, "neden")));
        Assert.Equal((stateBefore, version, (ForceClosureFacts?)null), (draft.State, draft.Version, draft.ForceClosure));
    }

    [Fact]
    public void A_closure_needs_a_reason_within_the_bound_the_current_version_a_non_blocking_preview_and_every_lines_shipped_total()
    {
        var draft = ApprovedDraft(out var line);
        ForceClosureLineInput[] shipped = [new(line.Id, 0m)];

        Assert.Equal(SalesRuleCodes.ForceCloseReasonRequired, CodeOf(() => draft.ForceClose(draft.Version, Closer, "   ", Impact, shipped, T0)));
        Assert.Equal(SalesRuleCodes.ForceCloseReasonRequired, CodeOf(() => draft.ForceClose(draft.Version, Closer, null, Impact, shipped, T0)));
        Assert.Equal(SalesRuleCodes.ForceCloseReasonTooLong, CodeOf(() => draft.ForceClose(draft.Version, Closer,
            new string('x', SalesOrderDraft.MaximumForceCloseReasonLength + 1), Impact, shipped, T0)));
        Assert.Throws<SalesVersionConflictException>(() => draft.ForceClose(draft.Version - 1, Closer, "neden", Impact, shipped, T0));
        Assert.Throws<ArgumentException>(() => draft.ForceClose(draft.Version, Closer, "neden", new ForceClosureImpactSnapshot(" ", Impact.Sources), shipped, T0));
        Assert.Throws<ArgumentException>(() => draft.ForceClose(draft.Version, Closer, "neden", new ForceClosureImpactSnapshot("t", []), shipped, T0));
        Assert.Throws<ArgumentException>(() => draft.ForceClose(draft.Version, Closer, "neden",
            new ForceClosureImpactSnapshot("t", [new("a", "None", false), new("a", "Present", false)]), shipped, T0));
        // A source the caller marked as blocking is never recorded as closed over.
        Assert.Throws<ArgumentException>(() => draft.ForceClose(draft.Version, Closer, "neden",
            new ForceClosureImpactSnapshot("t", [new("inventory.active-allocations", "Present", true)]), shipped, T0));
        // Every line exactly once, a valid shipped total within the ordered quantity in the line's own unit.
        Assert.Throws<ArgumentException>(() => draft.ForceClose(draft.Version, Closer, "neden", Impact, [], T0));
        Assert.Throws<ArgumentException>(() => draft.ForceClose(draft.Version, Closer, "neden", Impact, [new(line.Id, 0m), new(line.Id, 0m)], T0));
        Assert.Throws<ArgumentException>(() => draft.ForceClose(draft.Version, Closer, "neden", Impact, [new(Guid.NewGuid(), 0m)], T0));
        Assert.Throws<ArgumentException>(() => draft.ForceClose(draft.Version, Closer, "neden", Impact, [new(line.Id, 1.26m)], T0));
        Assert.Throws<ArgumentException>(() => draft.ForceClose(draft.Version, Closer, "neden", Impact, [new(line.Id, -0.1m)], T0));
        Assert.Throws<ArgumentException>(() => draft.ForceClose(draft.Version, Closer, "neden", Impact, [new(line.Id, 0.0001m)], T0));
        Assert.Null(SalesOrderDraft.ClosedRemainderText(line, 1.26m));
        // Every refusal moved nothing.
        Assert.Equal((SalesDraftState.Draft, 4L), (draft.State, draft.Version));
        Assert.Equal("neden", draft.RequireForceClosable(draft.Version, "  neden "));
        Assert.Equal(4L, draft.Version);
    }

    [Fact]
    public void A_closed_order_refuses_every_mutation_entry()
    {
        var draft = ApprovedDraft(out var line);
        draft.ForceClose(draft.Version, Closer, "neden", Impact, [new ForceClosureLineInput(line.Id, 0m)], T0);
        var version = draft.Version;

        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.ForceClose(draft.Version, Closer, "yine", Impact,
            [new ForceClosureLineInput(line.Id, 0m)], T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Cancel(draft.Version, Closer, "iptal",
            new CancellationImpactSnapshot("t", [new("inventory.active-allocations", "None")]), T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.RescheduleDelivery(draft.Version, Day.AddDays(30),
            [new LineScheduleInput(line.Id, Day.AddDays(31), [])], "synthetic-planner", T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.ReviseCommercial(draft.Version,
            [new LineCommercialInput(line.Id, new CommercialDraftFields("10", KgUnit, null))], "synthetic-pricer", T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.ReviseQuantities(draft.Version,
            [new LineQuantityInput(line.Id, "2", 2m)], "synthetic-reviser", "neden", T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.ApprovePriceFloorExceptions(draft.Version,
            [new LinePriceFloorExceptionApprovalInput(line.Id, "neden")], "synthetic-approver", T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.RecordCustomerAcceptance(draft.Version, "synthetic-approver", T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.RequestRemainderChange(draft.Version, line.Id, RemainderChangeKind.Cancel,
            null, null, "neden", new RemainderFloor(0.5m, 0.5m, 0.5m), "synthetic-requester", T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.ApproveRemainderChange(draft.Version, 1,
            new RemainderFloor(0.5m, 0.5m, 0.5m), "synthetic-approver", T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.RejectRemainderChange(draft.Version, 1, "synthetic-approver", null, T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Submit(draft.Version, "synthetic-preparer", T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Approve(draft.Version, "synthetic-approver", T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Reject(draft.Version, "synthetic-approver", null, T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Withdraw(draft.Version, "synthetic-preparer", null, T0)));
        Assert.Equal(SalesRuleCodes.DraftNotDraft, CodeOf(() => draft.AddLine(draft.Version, Line(Guid.NewGuid(), "1"), T0)));
        Assert.Equal((SalesDraftState.Closed, version), (draft.State, draft.Version));
    }

    [Fact]
    public void A_stored_closure_restores_before_the_approval_axis_and_only_when_it_fits()
    {
        var source = ApprovedDraft(out var line);
        var facts = source.ForceClose(source.Version, Closer, "neden", Impact, [new ForceClosureLineInput(line.Id, 0.5m)], T0);

        var rebuilt = Draft(out _, line.Id);
        rebuilt.RestoreForceClosure(facts);
        rebuilt.RestoreApproval(InternalApprovalState.Approved, source.Approval, source.Version, T0);
        Assert.Equal((SalesDraftState.Closed, facts, 5L), (rebuilt.State, rebuilt.ForceClosure, rebuilt.Version));

        // The closure is one step of its own: a stored version that does not count it is refused.
        var tooLow = Draft(out _, line.Id);
        tooLow.RestoreForceClosure(facts);
        Assert.Throws<InvalidOperationException>(() => tooLow.RestoreApproval(InternalApprovalState.Approved, source.Approval, 4, T0));

        // Only an approved order is ever closed: a stored closure beside another approval state is refused.
        var notApproved = Draft(out _, line.Id);
        notApproved.RestoreForceClosure(facts);
        Assert.Throws<InvalidOperationException>(() => notApproved.RestoreApproval(InternalApprovalState.Preparing, ApprovalFacts.None, 5, T0));

        // Facts that do not fit are refused, not repaired.
        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreForceClosure(facts with { Revision = 1 }));
        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreForceClosure(facts with { ClosedBy = " " }));
        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreForceClosure(facts with { Reason = "" }));
        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreForceClosure(facts with { Impact = new ForceClosureImpactSnapshot("t", []) }));
        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreForceClosure(facts with
        {
            Impact = new ForceClosureImpactSnapshot("t", [new("inventory.active-allocations", "Present", true)]),
        }));
        // The lines must be the order's lines with a remainder that IS ordered − shipped.
        Assert.Throws<InvalidOperationException>(() => Draft(out _).RestoreForceClosure(facts));
        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreForceClosure(facts with
        {
            Lines = [facts.Lines[0] with { ClosedRemainder = "0.7" }],
        }));
        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreForceClosure(facts with
        {
            Lines = [facts.Lines[0] with { Ordered = "1.250" }],
        }));
        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreForceClosure(facts with
        {
            Lines = [facts.Lines[0] with { Shipped = "2", ClosedRemainder = "0" }],
        }));
        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreForceClosure(facts with { Lines = [] }));
        var twice = Draft(out _, line.Id);
        twice.RestoreForceClosure(facts);
        Assert.Throws<InvalidOperationException>(() => twice.RestoreForceClosure(facts));
        // A cancellation and a closure never stand together.
        var both = Draft(out _, line.Id);
        both.RestoreCancellation(new CancellationFacts(0, "synthetic-canceller", T0, "neden",
            new CancellationImpactSnapshot("t", [new("inventory.active-allocations", "None")])));
        Assert.Throws<InvalidOperationException>(() => both.RestoreForceClosure(facts));
        var late = Draft(out _, line.Id);
        late.RestoreApproval(InternalApprovalState.Preparing, ApprovalFacts.None, late.Version, T0);
        Assert.Throws<InvalidOperationException>(() => late.RestoreForceClosure(facts));
    }

    private static string CodeOf(Action action) => Assert.Throws<SalesRuleException>(action).Code;

    private static SalesDraftHeader Header() => new(Guid.Parse("33333333-3333-4333-8333-333333333333"),
        Guid.Parse("44444444-4444-4444-8444-444444444444"), Guid.Parse("55555555-5555-4555-8555-555555555555"), Day, Day.AddDays(10));

    private static MoneyProfileSnapshot Money() => new(Company, Guid.Parse("55555555-5555-4555-8555-555555555555"),
        SalesOrderDraft.MoneyOperation, Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), "3", Day, true, new DateOnly(2026, 9, 1), null, 2);

    private static SalesDraftLineInput Line(Guid id, string quantity)
    {
        var unit = new UnitProfileSnapshot(Company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, new DateOnly(2026, 9, 1), null);
        var binding = new UnitBindingSnapshot(Company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Guid.Parse("99999999-9999-4999-8999-999999999999"), "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        return new SalesDraftLineInput(id, product, unit, quantity, decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), Day.AddDays(3));
    }

    /// <summary>A one-line draft (1.25 kg) at version 2.</summary>
    private static SalesOrderDraft Draft(out SalesOrderDraftLine line, Guid? lineId = null)
    {
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), Company, "SO-2026-000001", Header(), Money(), T0);
        line = draft.AddLine(draft.Version, Line(lineId ?? Guid.NewGuid(), "1.25"), T0);
        return draft;
    }

    /// <summary>The one-line draft submitted and approved: version 4.</summary>
    private static SalesOrderDraft ApprovedDraft(out SalesOrderDraftLine line)
    {
        var draft = Draft(out line);
        draft.Submit(draft.Version, "synthetic-preparer", T0);
        draft.Approve(draft.Version, "synthetic-approver", T0);
        return draft;
    }
}
