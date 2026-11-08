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
