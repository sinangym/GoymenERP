using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D30 / SAT-D213 / SAT-D216, the domain half: an approval records the line, its number, the measured quantity and the shipped
/// total it covers with exactly one version step and nothing else moving; it covers that shipped total (or less) against the same
/// measured quantity only; the shipper never approves; a cancelled or closed order, an unknown line and a total that is not an
/// over-delivery or is already covered are refused; a stored approval restores before the approval axis only when it fits and is
/// counted by the version floor. Every identity is synthetic.
/// </summary>
public sealed class SalesOrderDraftOverDeliveryApprovalTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 7, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly DateOnly Day = new(2026, 11, 7);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid KgUnit = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid KgProfile = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private const string Approver = "synthetic-approver-2";
    private const string Shipper = "synthetic-shipper";

    [Fact]
    public void An_approval_records_the_covered_shipped_total_with_one_version_step_and_nothing_else_moves()
    {
        var draft = ApprovedDraft(out var line);
        var approval = draft.Approval;
        Assert.False(draft.IsOverDeliveryApproved(line.Id, Kg(100m), Kg(102.5m)));

        var facts = draft.ApproveOverDelivery(4, $" {Approver} ", Input(line, "102.500"), T0.AddHours(1));

        Assert.Equal(new OverDeliveryApprovalFacts(line.Id, 1, 0, "100", "102.5", Approver, T0.AddHours(1).ToUniversalTime()), facts);
        Assert.Equal((5L, SalesDraftState.Draft, InternalApprovalState.Approved, approval, "100"),
            (draft.Version, draft.State, draft.ApprovalState, draft.Approval, draft.Lines[0].QuantityText));
        // SAT-D213: it covers the approved total (and less) against the same measured quantity — never more, never another basis.
        Assert.True(draft.IsOverDeliveryApproved(line.Id, Kg(100m), Kg(102.5m)));
        Assert.True(draft.IsOverDeliveryApproved(line.Id, Kg(100m), Kg(102.1m)));
        Assert.False(draft.IsOverDeliveryApproved(line.Id, Kg(100m), Kg(102.6m)));
        Assert.False(draft.IsOverDeliveryApproved(line.Id, Kg(101m), Kg(102.5m)));
        Assert.False(draft.IsOverDeliveryApproved(line.Id, Kg(100m), Kg(100m)));
        Assert.False(draft.IsOverDeliveryApproved(Guid.NewGuid(), Kg(100m), Kg(102.5m)));

        // An already covered total is a state conflict; a larger one is the line's next approval.
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.ApproveOverDelivery(5, Approver, Input(line, "102.5"), T0)));
        var second = draft.ApproveOverDelivery(5, Approver, Input(line, "102.8"), T0);
        Assert.Equal((2, "102.8", 6L), (second.ApprovalNo, second.ApprovedShipped, draft.Version));
    }

    [Fact]
    public void The_shipper_never_approves_and_wrong_state_or_input_is_refused()
    {
        var draft = ApprovedDraft(out var line);
        // SAT-D216: the approver among the shippers the owner names.
        Assert.Equal(SalesRuleCodes.OverDeliverySelfApproval,
            CodeOf(() => draft.ApproveOverDelivery(4, Shipper, Input(line, "102.5", "synthetic-other", Shipper), T0)));
        // No shipper set is never an input (the caller refuses before, fail-closed).
        Assert.Throws<ArgumentException>(() => draft.ApproveOverDelivery(4, Approver, Input(line, "102.5") with { ShippedBy = [] }, T0));
        Assert.Throws<ArgumentException>(() => draft.ApproveOverDelivery(4, Approver, Input(line, "102.5", " "), T0));
        // Not an over-delivery, an unknown line, a stale version, a value outside the line's scale.
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.ApproveOverDelivery(4, Approver, Input(line, "100"), T0)));
        Assert.Equal(SalesRuleCodes.OverDeliveryLineUnknown,
            CodeOf(() => draft.ApproveOverDelivery(4, Approver, Input(line, "102.5") with { LineId = Guid.NewGuid() }, T0)));
        Assert.Throws<SalesVersionConflictException>(() => draft.ApproveOverDelivery(3, Approver, Input(line, "102.5"), T0));
        Assert.Throws<ArgumentException>(() => draft.ApproveOverDelivery(4, Approver, Input(line, "102.5001"), T0));
        Assert.Equal((4L, 0), (draft.Version, draft.OverDeliveryApprovals.Count));

        // A cancelled order refuses it.
        draft.Cancel(4, "synthetic-canceller", "neden", new CancellationImpactSnapshot("t", [new("inventory.active-allocations", "None")]), T0);
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.ApproveOverDelivery(5, Approver, Input(line, "102.5"), T0)));
    }

    [Fact]
    public void A_stored_approval_restores_before_the_approval_axis_only_when_it_fits_and_counts_one_step()
    {
        var source = ApprovedDraft(out var line);
        var facts = source.ApproveOverDelivery(4, Approver, Input(line, "102.5"), T0);

        var rebuilt = Draft(out _, line.Id);
        rebuilt.RestoreOverDeliveryApproval(facts);
        rebuilt.RestoreApproval(InternalApprovalState.Approved, source.Approval, 5, T0);
        Assert.Equal((5L, facts), (rebuilt.Version, Assert.Single(rebuilt.OverDeliveryApprovals)));
        Assert.True(rebuilt.IsOverDeliveryApproved(line.Id, Kg(100m), Kg(102.5m)));

        // One step of its own: a stored version that does not count it is refused.
        var tooLow = Draft(out _, line.Id);
        tooLow.RestoreOverDeliveryApproval(facts);
        Assert.Throws<InvalidOperationException>(() => tooLow.RestoreApproval(InternalApprovalState.Approved, source.Approval, 4, T0));

        // Facts that do not fit are refused, not repaired.
        Assert.Throws<InvalidOperationException>(() => Draft(out _).RestoreOverDeliveryApproval(facts));
        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreOverDeliveryApproval(facts with { ApprovalNo = 2 }));
        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreOverDeliveryApproval(facts with { BasisRevision = 1 }));
        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreOverDeliveryApproval(facts with { ApprovedShipped = "100" }));
        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreOverDeliveryApproval(facts with { ApprovedShipped = "102.50" }));
        Assert.Throws<InvalidOperationException>(() => Draft(out _, line.Id).RestoreOverDeliveryApproval(facts with { ApprovedBy = " " }));
        var twice = Draft(out _, line.Id);
        twice.RestoreOverDeliveryApproval(facts);
        Assert.Throws<InvalidOperationException>(() => twice.RestoreOverDeliveryApproval(facts));
        var late = Draft(out _, line.Id);
        late.RestoreApproval(InternalApprovalState.Preparing, ApprovalFacts.None, late.Version, T0);
        Assert.Throws<InvalidOperationException>(() => late.RestoreOverDeliveryApproval(facts));
    }

    private static OverDeliveryApprovalInput Input(SalesOrderDraftLine line, string shipped, params string[] shippedBy) =>
        new(line.Id, 0, "100", decimal.Parse(shipped, System.Globalization.CultureInfo.InvariantCulture), shippedBy.Length == 0 ? [Shipper] : shippedBy);

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
    private static SalesOrderDraft Draft(out SalesOrderDraftLine line, Guid? lineId = null)
    {
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), Company, "SO-2026-000001", Header(), Money(), T0);
        line = draft.AddLine(draft.Version, Line(lineId ?? Guid.NewGuid(), "100"), T0);
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
