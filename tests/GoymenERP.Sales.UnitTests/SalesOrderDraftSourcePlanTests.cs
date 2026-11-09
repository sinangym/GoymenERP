using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D251..SAT-D255 (EK50), the domain half: the suggestion rule (the line's own allocation is subtracted from the need and never
/// counted as free stock — reviewer note (c)), the ONE equality of a save (reviewer note (a): on a partially delivered line the
/// locked part plus the remainder's parts equal the line quantity, no second equality), and the save itself: one version step,
/// nothing else moving, a fully delivered, closed, unknown or non-kilogram line refused, and the fail-closed order states.
/// Every identity is synthetic.
/// </summary>
public sealed class SalesOrderDraftSourcePlanTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 9, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly DateOnly Day = new(2026, 11, 9);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid KgUnit = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid KgProfile = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid PieceUnit = Guid.Parse("88888888-8888-4888-8888-888888888888");
    private const string Planner = "synthetic-planner";

    [Theory]
    // quantity, shipped, own allocation, free stock → suggestion, reason
    [InlineData(100, 0, 100, null, SourcePlanSuggestion.Stock, SourcePlanSuggestionReason.AllocationCoversRemainder)]
    [InlineData(100, 40, 60, null, SourcePlanSuggestion.Stock, SourcePlanSuggestionReason.AllocationCoversRemainder)]
    [InlineData(100, 0, 0, null, SourcePlanSuggestion.Unavailable, SourcePlanSuggestionReason.FreeStockUnavailable)]
    [InlineData(100, 0, 30, 70.0, SourcePlanSuggestion.Stock, SourcePlanSuggestionReason.FreeStockEnough)]
    [InlineData(100, 0, 30, 69.999, SourcePlanSuggestion.Unavailable, SourcePlanSuggestionReason.SupplyTypeUnmapped)]
    [InlineData(100, 100, 0, 500.0, SourcePlanSuggestion.Unavailable, SourcePlanSuggestionReason.LineDelivered)]
    [InlineData(100, 120, 0, 500.0, SourcePlanSuggestion.Unavailable, SourcePlanSuggestionReason.LineDelivered)]
    public void The_suggestion_subtracts_the_lines_own_allocation_from_the_need(double quantity, double shipped, double allocated,
        double? free, SourcePlanSuggestion suggestion, SourcePlanSuggestionReason reason)
    {
        var result = SourcePlanCalculator.Suggest((decimal)quantity, (decimal)shipped, (decimal)allocated, (decimal?)free);

        Assert.Equal(new SourcePlanSuggestionResult(suggestion, reason), result);
    }

    [Fact]
    public void Own_allocation_is_never_counted_as_free_stock_and_unreadable_inputs_are_unavailable()
    {
        // Need 100 − 0 − 50 = 50: a free stock of 50 suffices; had the own 50 been counted again, 25 would wrongly suffice.
        Assert.Equal(SourcePlanSuggestionReason.FreeStockEnough, SourcePlanCalculator.Suggest(100m, 0m, 50m, 50m).Reason);
        Assert.Equal(SourcePlanSuggestionReason.SupplyTypeUnmapped, SourcePlanCalculator.Suggest(100m, 0m, 50m, 25m).Reason);
        Assert.Equal(SourcePlanSuggestionReason.DeliveryUnreadable, SourcePlanCalculator.Suggest(100m, null, 50m, 50m).Reason);
        Assert.Equal(SourcePlanSuggestionReason.AllocationUnreadable, SourcePlanCalculator.Suggest(100m, 0m, null, 50m).Reason);
        Assert.Throws<ArgumentOutOfRangeException>(() => SourcePlanCalculator.Suggest(0m, 0m, 0m, 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => SourcePlanCalculator.Suggest(100m, -1m, 0m, 0m));
    }

    [Fact]
    public void One_equality_balances_a_partially_delivered_line()
    {
        // 100 kg, 40 delivered (locked): the remainder 60 = 25 stock + 20 production + 10 purchasing + 5 subcontract.
        Assert.True(SourcePlanCalculator.IsBalanced(100m, 40m, 25m, 20m, 10m, 5m));
        // Planning 100 over the remainder (as if nothing were delivered) or leaving a gap is the same single mismatch.
        Assert.False(SourcePlanCalculator.IsBalanced(100m, 40m, 25m, 60m, 10m, 5m));
        Assert.False(SourcePlanCalculator.IsBalanced(100m, 40m, 25m, 20m, 10m, 4.999m));
        Assert.True(SourcePlanCalculator.IsBalanced(100m, 0m, 0m, 0m, 0m, 100m));
    }

    [Fact]
    public void A_save_records_one_version_step_and_moves_nothing_else()
    {
        var draft = ApprovedDraft(out var line);
        var approval = draft.Approval;

        var facts = draft.RecordSourcePlan(4, $" {Planner} ", Plan(line, 40m, 25m, 20m, 10m, 5m), T0.AddHours(1));

        var fact = Assert.Single(facts.Lines);
        Assert.Equal(new SourcePlanLineFacts(line.Id, 1, 100m, 40m, 25m, 20m, 10m, 5m, false, Planner, T0.AddHours(1).ToUniversalTime(), 5L), fact);
        Assert.Equal((5L, 0, SalesDraftState.Draft, InternalApprovalState.Approved, approval, "100"),
            (draft.Version, draft.Revision, draft.State, draft.ApprovalState, draft.Approval, draft.Lines[0].QuantityText));

        // Free until delivery: the next save moves it again with one more step.
        draft.RecordSourcePlan(5, Planner, Plan(line, 40m, 60m, 0m, 0m, 0m), T0);
        Assert.Equal(6L, draft.Version);
    }

    [Fact]
    public void A_plan_can_be_saved_before_approval_at_revision_zero()
    {
        var draft = Draft(out var line);
        var facts = draft.RecordSourcePlan(2, Planner, Plan(line, 0m, 0m, 100m, 0m, 0m), T0);
        Assert.Equal(3L, Assert.Single(facts.Lines).DraftVersion);
    }

    [Fact]
    public void Mismatch_delivered_unknown_and_malformed_parts_are_refused_without_a_step()
    {
        var draft = ApprovedDraft(out var line);

        Assert.Equal(SalesRuleCodes.SourcePlanSumMismatch, CodeOf(() => draft.RecordSourcePlan(4, Planner, Plan(line, 40m, 25m, 20m, 10m, 0m), T0)));
        Assert.Equal(SalesRuleCodes.SourcePlanLineLocked, CodeOf(() => draft.RecordSourcePlan(4, Planner, Plan(line, 100m, 0m, 0m, 0m, 0m), T0)));
        Assert.Equal(SalesRuleCodes.SourcePlanLineUnknown,
            CodeOf(() => draft.RecordSourcePlan(4, Planner, Plan(line, 0m, 0m, 100m, 0m, 0m) with
            {
                Lines = [new SourcePlanLineInput(Guid.NewGuid(), 0m, 0m, 100m, 0m, 0m)],
            }, T0)));
        Assert.Throws<ArgumentException>(() => draft.RecordSourcePlan(4, Planner, Plan(line, 0m, 0m, 100.0001m, 0m, -0.0001m), T0));
        Assert.Throws<ArgumentException>(() => draft.RecordSourcePlan(4, Planner, Plan(line, 0m, -1m, 101m, 0m, 0m), T0));
        Assert.Throws<ArgumentException>(() => draft.RecordSourcePlan(4, Planner, new SourcePlanInput([], false), T0));
        var twice = new SourcePlanLineInput(line.Id, 0m, 0m, 100m, 0m, 0m);
        Assert.Throws<ArgumentException>(() => draft.RecordSourcePlan(4, Planner, new SourcePlanInput([twice, twice], false), T0));
        Assert.Throws<SalesVersionConflictException>(() => draft.RecordSourcePlan(3, Planner, Plan(line, 0m, 0m, 100m, 0m, 0m), T0));
        Assert.Equal(4L, draft.Version);
    }

    [Fact]
    public void A_piece_line_is_refused_while_the_unit_conversion_is_undecided()
    {
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), Company, "SO-2026-000001", Header(), Money(), T0);
        var pieces = draft.AddLine(draft.Version, PieceLine(Guid.NewGuid(), 20m), T0);

        Assert.Equal(SalesRuleCodes.SourcePlanUnitUnsupported,
            CodeOf(() => draft.RecordSourcePlan(draft.Version, Planner, new SourcePlanInput([new(pieces.Id, 0m, 0m, 20m, 0m, 0m)], false), T0)));
    }

    [Fact]
    public void A_closed_line_a_cancelled_or_withdrawn_order_and_a_pending_revision_refuse()
    {
        var closed = ApprovedDraft(out var closedLine);
        closed.CloseLine(4, "synthetic-closer", new LineClosureInput(closedLine.Id, 0, "100", 90m, DeliveryToleranceBand.Warn,
            ["synthetic-shipper"]), T0);
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => closed.RecordSourcePlan(5, Planner, Plan(closedLine, 90m, 0m, 10m, 0m, 0m), T0)));

        var cancelled = ApprovedDraft(out var cancelledLine);
        cancelled.Cancel(4, "synthetic-canceller", "neden", new CancellationImpactSnapshot("t", [new("inventory.active-allocations", "None")]), T0);
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => cancelled.RecordSourcePlan(5, Planner, Plan(cancelledLine, 0m, 0m, 100m, 0m, 0m), T0)));

        var withdrawn = Draft(out var withdrawnLine);
        withdrawn.Submit(2, "synthetic-preparer", T0);
        withdrawn.Withdraw(3, "synthetic-preparer", null, T0);
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => withdrawn.RequireSourcePlanWritable(4)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => withdrawn.RecordSourcePlan(4, Planner, Plan(withdrawnLine, 0m, 0m, 100m, 0m, 0m), T0)));
    }

    private static SourcePlanInput Plan(SalesOrderDraftLine line, decimal locked, decimal stock, decimal production, decimal purchasing,
        decimal subcontract) =>
        new([new SourcePlanLineInput(line.Id, locked, stock, production, purchasing, subcontract)], false);

    private static string CodeOf(Action action) => Assert.Throws<SalesRuleException>(action).Code;

    private static SalesDraftHeader Header() => new(Guid.Parse("33333333-3333-4333-8333-333333333333"),
        Guid.Parse("44444444-4444-4444-8444-444444444444"), Guid.Parse("55555555-5555-4555-8555-555555555555"), Day, Day.AddDays(10),
        DueTerms: TestDueTerms.PointDue30);

    private static MoneyProfileSnapshot Money() => new(Company, Guid.Parse("55555555-5555-4555-8555-555555555555"),
        SalesOrderDraft.MoneyOperation, Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), "3", Day, true, new DateOnly(2026, 9, 1), null, 2);

    private static SalesDraftLineInput Line(Guid id, string quantity)
    {
        var unit = new UnitProfileSnapshot(Company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, new DateOnly(2026, 9, 1), null);
        var binding = new UnitBindingSnapshot(Company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Guid.Parse("99999999-9999-4999-8999-999999999999"), "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        return new SalesDraftLineInput(id, product, unit, quantity, decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), Day.AddDays(3));
    }

    private static SalesDraftLineInput PieceLine(Guid id, decimal quantity)
    {
        var unit = new UnitProfileSnapshot(Company, PieceUnit, "pcs", "count", Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"), "1", false, 0, 0,
            true, new DateOnly(2026, 9, 1), null);
        var binding = new UnitBindingSnapshot(Company, unit.UnitId, unit.ProfileId, unit.ProfileVersion);
        var product = new ProductSnapshot(Guid.Parse("99999999-9999-4999-8999-999999999999"), "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        return new SalesDraftLineInput(id, product, unit, quantity.ToString(System.Globalization.CultureInfo.InvariantCulture), quantity, Day.AddDays(3));
    }

    /// <summary>A one-line draft (100 kg) at version 2.</summary>
    private static SalesOrderDraft Draft(out SalesOrderDraftLine line)
    {
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), Company, "SO-2026-000001", Header(), Money(), T0);
        line = draft.AddLine(draft.Version, Line(Guid.NewGuid(), "100"), T0);
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
