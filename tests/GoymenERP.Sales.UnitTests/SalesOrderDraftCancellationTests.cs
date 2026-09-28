using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// DEC-SAL-005 / AT-007, the domain half: only an APPROVED order is cancelled, with a required reason and the impact preview
/// it was decided on; the lifecycle moves Draft → Cancelled, the version by exactly one, and NOTHING else of the history —
/// the approval facts, the revision, the lines, the archived revisions and the schedules stay. A cancelled order refuses
/// every mutation entry; a stored cancellation restores only when it fits. Every identity is synthetic.
/// </summary>
public sealed class SalesOrderDraftCancellationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly DateOnly Day = new(2026, 9, 27);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid KgUnit = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid KgProfile = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private const string Canceller = "synthetic-canceller";

    private static readonly CancellationImpactSnapshot Impact = new(new string('c', 64),
        [new("inventory.active-allocations", "None"), new("purchasing.linked-orders", "NoSource")]);

    [Fact]
    public void An_approved_order_is_cancelled_with_its_reason_and_impact_and_nothing_of_its_history_moves()
    {
        var draft = ApprovedDraft(out var line);
        var approval = draft.Approval;
        var lines = draft.Lines.Select(candidate => (candidate.Id, candidate.QuantityText, candidate.RequestedDeliveryDate)).ToArray();

        var facts = draft.Cancel(draft.Version, $" {Canceller} ", "  SENTETIK müşteri vazgeçti  ", Impact, T0.AddHours(1));

        Assert.Equal((SalesDraftState.Cancelled, InternalApprovalState.Approved, 0, 5L), (draft.State, draft.ApprovalState, draft.Revision, draft.Version));
        // AT-007: who approved stays who approved; the lines are the lines.
        Assert.Equal(approval, draft.Approval);
        Assert.Equal(lines, draft.Lines.Select(candidate => (candidate.Id, candidate.QuantityText, candidate.RequestedDeliveryDate)));
        Assert.Equal(new CancellationFacts(0, Canceller, T0.AddHours(1).ToUniversalTime(), "SENTETIK müşteri vazgeçti", Impact), facts);
        Assert.Same(facts, draft.Cancellation);
        Assert.Equal("1.25", line.QuantityText);
    }

    [Theory]
    [InlineData("Preparing")]
    [InlineData("PendingApproval")]
    [InlineData("Rejected")]
    [InlineData("Withdrawn")]
    [InlineData("RevisedBackToPreparing")]
    public void Only_an_approved_order_is_cancelled_every_other_state_is_a_state_conflict(string state)
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
                // A price revision of an approved order archives Rev.0 and re-enters approval at Rev.1: no approved revision in force.
                draft.Submit(draft.Version, "synthetic-preparer", T0);
                draft.Approve(draft.Version, "synthetic-approver", T0);
                draft.ReviseCommercial(draft.Version, [new LineCommercialInput(line.Id, new CommercialDraftFields("10", KgUnit, null))], "synthetic-pricer", T0);
                break;
        }

        var version = draft.Version;
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Cancel(draft.Version, Canceller, "neden", Impact, T0)));
        Assert.Equal((SalesDraftState.Draft, version, (CancellationFacts?)null), (draft.State, draft.Version, draft.Cancellation));
    }

    [Fact]
    public void A_cancellation_needs_a_reason_within_the_bound_the_current_version_and_the_preview()
    {
        var draft = ApprovedDraft(out _);

        Assert.Equal(SalesRuleCodes.CancellationReasonRequired, CodeOf(() => draft.Cancel(draft.Version, Canceller, "   ", Impact, T0)));
        Assert.Equal(SalesRuleCodes.CancellationReasonRequired, CodeOf(() => draft.Cancel(draft.Version, Canceller, null, Impact, T0)));
        Assert.Equal(SalesRuleCodes.CancellationReasonTooLong,
            CodeOf(() => draft.Cancel(draft.Version, Canceller, new string('x', SalesOrderDraft.MaximumCancellationReasonLength + 1), Impact, T0)));
        Assert.Throws<SalesVersionConflictException>(() => draft.Cancel(draft.Version - 1, Canceller, "neden", Impact, T0));
        Assert.Throws<ArgumentException>(() => draft.Cancel(draft.Version, Canceller, "neden", new CancellationImpactSnapshot(" ", Impact.Sources), T0));
        Assert.Throws<ArgumentException>(() => draft.Cancel(draft.Version, Canceller, "neden", new CancellationImpactSnapshot("t", []), T0));
        Assert.Throws<ArgumentException>(() => draft.Cancel(draft.Version, Canceller, "neden",
            new CancellationImpactSnapshot("t", [new("a", "None"), new("a", "Present")]), T0));
        // Every refusal moved nothing.
        Assert.Equal((SalesDraftState.Draft, 4L), (draft.State, draft.Version));
        // The preconditions alone (the service asks them before any owner) answer the same, and change nothing either.
        Assert.Equal("neden", draft.RequireCancellable(draft.Version, "  neden "));
        Assert.Equal(4L, draft.Version);
    }

    [Fact]
    public void A_cancelled_order_refuses_every_mutation_entry()
    {
        var draft = ApprovedDraft(out var line);
        draft.Cancel(draft.Version, Canceller, "neden", Impact, T0);
        var version = draft.Version;

        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Cancel(draft.Version, Canceller, "yine", Impact, T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.RescheduleDelivery(draft.Version, Day.AddDays(30),
            [new LineScheduleInput(line.Id, Day.AddDays(31), [])], "synthetic-planner", T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.ReviseCommercial(draft.Version,
            [new LineCommercialInput(line.Id, new CommercialDraftFields("10", KgUnit, null))], "synthetic-pricer", T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Submit(draft.Version, "synthetic-preparer", T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Approve(draft.Version, "synthetic-approver", T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Reject(draft.Version, "synthetic-approver", null, T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Withdraw(draft.Version, "synthetic-preparer", null, T0)));
        Assert.Equal(SalesRuleCodes.DraftNotDraft, CodeOf(() => draft.AddLine(draft.Version, Line(Guid.NewGuid(), "1"), T0)));
        Assert.Equal((SalesDraftState.Cancelled, version), (draft.State, draft.Version));
    }

    [Fact]
    public void A_stored_cancellation_restores_before_the_approval_axis_and_only_when_it_fits()
    {
        var source = ApprovedDraft(out var line);
        var facts = source.Cancel(source.Version, Canceller, "neden", Impact, T0);

        var rebuilt = Draft(out _, line.Id);
        rebuilt.RestoreCancellation(facts);
        rebuilt.RestoreApproval(InternalApprovalState.Approved, source.Approval, source.Version, T0);
        Assert.Equal((SalesDraftState.Cancelled, facts, 5L), (rebuilt.State, rebuilt.Cancellation, rebuilt.Version));

        // The cancellation is one step of its own: a stored version that does not count it is refused.
        var tooLow = Draft(out _, line.Id);
        tooLow.RestoreCancellation(facts);
        Assert.Throws<InvalidOperationException>(() => tooLow.RestoreApproval(InternalApprovalState.Approved, source.Approval, 4, T0));

        // Only an approved order is ever cancelled: a stored cancellation beside another approval state is refused.
        var notApproved = Draft(out _, line.Id);
        notApproved.RestoreCancellation(facts);
        Assert.Throws<InvalidOperationException>(() => notApproved.RestoreApproval(InternalApprovalState.Preparing, ApprovalFacts.None, 5, T0));

        // Facts that do not fit (another revision, no actor, no reason, no preview) are refused, not repaired; after the
        // approval axis nothing is restored.
        Assert.Throws<InvalidOperationException>(() => Draft(out _).RestoreCancellation(facts with { Revision = 1 }));
        Assert.Throws<InvalidOperationException>(() => Draft(out _).RestoreCancellation(facts with { CancelledBy = " " }));
        Assert.Throws<InvalidOperationException>(() => Draft(out _).RestoreCancellation(facts with { Reason = "" }));
        Assert.Throws<InvalidOperationException>(() => Draft(out _).RestoreCancellation(facts with { Impact = new CancellationImpactSnapshot("t", []) }));
        var twice = Draft(out _);
        twice.RestoreCancellation(facts);
        Assert.Throws<InvalidOperationException>(() => twice.RestoreCancellation(facts));
        var late = Draft(out _);
        late.RestoreApproval(InternalApprovalState.Preparing, ApprovalFacts.None, late.Version, T0);
        Assert.Throws<InvalidOperationException>(() => late.RestoreCancellation(facts));
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
