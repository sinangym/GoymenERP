using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D32 / SAT-D215, the domain half of the user closure: only an APPROVED, live order without a pending remainder change is
/// closed by the user (no reason, no remainder); the lifecycle moves Draft → Closed (the force-closure's state) with exactly one
/// version step and its OWN record; nothing else moves. A user-closed order refuses every mutation entry; a stored user closure
/// restores only when it fits and never beside a cancellation or a force-closure. Every identity is synthetic.
/// </summary>
public sealed class SalesOrderDraftUserClosureTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly DateOnly Day = new(2026, 10, 25);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid KgUnit = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid KgProfile = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private const string Closer = "synthetic-closer";

    [Fact]
    public void An_approved_order_is_closed_by_the_user_with_its_own_record_and_nothing_of_its_history_moves()
    {
        var draft = ApprovedDraft(out _);
        var approval = draft.Approval;
        var lines = draft.Lines.Select(candidate => (candidate.Id, candidate.QuantityText)).ToArray();

        var facts = draft.CloseByUser(draft.Version, $" {Closer} ", T0.AddHours(1));

        Assert.Equal((SalesDraftState.Closed, InternalApprovalState.Approved, 0, 5L), (draft.State, draft.ApprovalState, draft.Revision, draft.Version));
        Assert.Equal(approval, draft.Approval);
        Assert.Equal(lines, draft.Lines.Select(candidate => (candidate.Id, candidate.QuantityText)));
        Assert.Equal(new UserClosureFacts(0, Closer, T0.AddHours(1).ToUniversalTime()), facts);
        Assert.Same(facts, draft.UserClosure);
        Assert.Null(draft.ForceClosure);
        Assert.Null(draft.Cancellation);

        // Terminal: the next mutation, a second close and a force-closure see a closed order.
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.CloseByUser(draft.Version, Closer, T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.RequireForceClosable(draft.Version, "neden")));
        var conflict = Assert.Throws<SalesRuleException>(() => draft.Cancel(draft.Version, "synthetic-canceller", "neden",
            new CancellationImpactSnapshot("t", [new("inventory.active-allocations", "None")]), T0));
        Assert.Contains("Kapatılmış sipariş", conflict.Message);
        Assert.Throws<SalesVersionConflictException>(() => draft.CloseByUser(draft.Version - 1, Closer, T0));
    }

    [Theory]
    [InlineData("Preparing")]
    [InlineData("PendingApproval")]
    [InlineData("Rejected")]
    [InlineData("Cancelled")]
    [InlineData("ForceClosed")]
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
            case "Cancelled":
                draft.Submit(draft.Version, "synthetic-preparer", T0);
                draft.Approve(draft.Version, "synthetic-approver", T0);
                draft.Cancel(draft.Version, "synthetic-canceller", "neden",
                    new CancellationImpactSnapshot("t", [new("inventory.active-allocations", "None")]), T0);
                break;
            case "ForceClosed":
                draft.Submit(draft.Version, "synthetic-preparer", T0);
                draft.Approve(draft.Version, "synthetic-approver", T0);
                draft.ForceClose(draft.Version, Closer, "neden", new ForceClosureImpactSnapshot("t", [new("inventory.active-allocations", "None", false)]),
                    [new ForceClosureLineInput(line.Id, 0m)], T0);
                break;
            case "PendingRemainderChange":
                draft.Submit(draft.Version, "synthetic-preparer", T0);
                draft.Approve(draft.Version, "synthetic-approver", T0);
                draft.RequestRemainderChange(draft.Version, line.Id, RemainderChangeKind.Cancel, null, null, "SENTETIK",
                    new RemainderFloor(0.5m, 0.5m, 0.5m), "synthetic-requester", T0);
                break;
        }

        var (stateBefore, version) = (draft.State, draft.Version);
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.CloseByUser(draft.Version, Closer, T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.RequireUserClosable(draft.Version)));
        Assert.Equal((stateBefore, version, (UserClosureFacts?)null), (draft.State, draft.Version, draft.UserClosure));
    }

    [Fact]
    public void A_stored_user_closure_restores_before_the_approval_axis_only_when_it_fits_and_never_beside_another_closure()
    {
        var source = ApprovedDraft(out var line);
        var facts = source.CloseByUser(source.Version, Closer, T0);

        var rebuilt = Draft(out _, line.Id);
        rebuilt.RestoreUserClosure(facts);
        rebuilt.RestoreApproval(InternalApprovalState.Approved, source.Approval, source.Version, T0);
        Assert.Equal((SalesDraftState.Closed, facts, 5L), (rebuilt.State, rebuilt.UserClosure, rebuilt.Version));

        // The closed lifecycle is one step: a stored version that does not count it is refused (the version floor).
        var tooLow = Draft(out _, line.Id);
        tooLow.RestoreUserClosure(facts);
        Assert.Throws<InvalidOperationException>(() => tooLow.RestoreApproval(InternalApprovalState.Approved, source.Approval, 4, T0));
        var notApproved = Draft(out _, line.Id);
        notApproved.RestoreUserClosure(facts);
        Assert.Throws<InvalidOperationException>(() => notApproved.RestoreApproval(InternalApprovalState.Preparing, ApprovalFacts.None, 5, T0));

        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreUserClosure(facts with { Revision = 1 }));
        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreUserClosure(facts with { ClosedBy = " " }));
        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreUserClosure(facts with
        {
            ClosedBy = new string('x', SalesOrderDraft.MaximumActorLength + 1),
        }));
        var twice = Draft(out _, line.Id);
        twice.RestoreUserClosure(facts);
        Assert.Throws<InvalidOperationException>(() => twice.RestoreUserClosure(facts));

        // A user closure and a force-closure (or a cancellation) never stand together, in either order.
        var forceFacts = new ForceClosureFacts(0, Closer, T0, "neden",
            new ForceClosureImpactSnapshot("t", [new("inventory.active-allocations", "None", false)]),
            [new ForceClosureLineSnapshot(line.Id, 1, "1.25", "1.25", "0")]);
        var userThenForce = Draft(out _, line.Id);
        userThenForce.RestoreUserClosure(facts);
        Assert.Throws<InvalidOperationException>(() => userThenForce.RestoreForceClosure(forceFacts));
        var forceThenUser = Draft(out _, line.Id);
        forceThenUser.RestoreForceClosure(forceFacts);
        Assert.Throws<InvalidOperationException>(() => forceThenUser.RestoreUserClosure(facts));
        var cancelled = Draft(out _, line.Id);
        cancelled.RestoreCancellation(new CancellationFacts(0, "synthetic-canceller", T0, "neden",
            new CancellationImpactSnapshot("t", [new("inventory.active-allocations", "None")])));
        Assert.Throws<InvalidOperationException>(() => cancelled.RestoreUserClosure(facts));
        var userThenCancel = Draft(out _, line.Id);
        userThenCancel.RestoreUserClosure(facts);
        Assert.Throws<InvalidOperationException>(() => userThenCancel.RestoreCancellation(new CancellationFacts(0, "synthetic-canceller", T0, "neden",
            new CancellationImpactSnapshot("t", [new("inventory.active-allocations", "None")]))));
        var late = Draft(out _, line.Id);
        late.RestoreApproval(InternalApprovalState.Preparing, ApprovalFacts.None, late.Version, T0);
        Assert.Throws<InvalidOperationException>(() => late.RestoreUserClosure(facts));
    }

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
