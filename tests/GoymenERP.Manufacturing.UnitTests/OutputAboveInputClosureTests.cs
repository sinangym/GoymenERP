using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-D41 (1)-(2) and URT-D42 (1)-(3): output above input never closes an order. The ± tolerance of URT-D6 covers only a
/// shortfall, output above input has no approval path, every planned parent roll is measured on its own, and a close
/// stored under the earlier rule is neither reopened nor changed (it stays readable and is listed by the report).
/// </summary>
public sealed class OutputAboveInputClosureTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000e001");
    private static readonly Guid RollA = Guid.Parse("30000000-0000-0000-0000-00000000e001");
    private static readonly Guid RollB = Guid.Parse("30000000-0000-0000-0000-00000000e002");
    private static readonly Guid Planner = Guid.Parse("40000000-0000-0000-0000-00000000e001");
    private static readonly Guid Closer = Guid.Parse("40000000-0000-0000-0000-00000000e002");
    private static readonly Guid Profile = Guid.Parse("50000000-0000-0000-0000-00000000e001");
    private static readonly DateOnly Today = new(2026, 11, 13);
    private static readonly DateTimeOffset CloseAt = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);

    private static Kilograms Kg(decimal value) => new(value);

    /// <summary>The reviewed version-0 catalog: fire maximum 5 %, mass-balance tolerance 2 % unless stated.</summary>
    private static ClosureTolerancePolicySnapshotV1 Catalog(decimal fire = 5m, decimal? massBalance = 2m) =>
        new(Company, Profile, 0, ManufacturingProcess.Slitting, fire, true, new DateOnly(2026, 11, 1), null, Today,
            massBalance, ClosureTolerancePolicySnapshotV1.CatalogSource);

    private static ClosureTolerancePolicySnapshotV1 Published(decimal? massBalance) =>
        new(Company, Guid.NewGuid(), 1, ManufacturingProcess.Slitting, 5m, true, new DateOnly(2026, 11, 1), null, Today,
            massBalance, ClosureTolerancePolicySnapshotV1.SettingsSource);

    private static ProductionOrder Order(params Guid[] rolls)
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            rolls.Select(roll => new PlannedInput(roll, Kg(10_000m))));
        plan.Approve(plan.Version, Planner, DateTimeOffset.UnixEpoch);
        return ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
    }

    private static void Realize(ProductionOrder order, Guid roll, decimal issued, decimal scrap, decimal good)
    {
        order.Issue(roll, Kg(issued));
        order.Declare(roll, OutputClass.Scrap, Kg(scrap));
        order.Declare(roll, OutputClass.GoodProduct, Kg(good));
    }

    /// <summary>1000 kg issued to one roll, 30 kg scrap (3 % fire), <paramref name="good"/> kg good.</summary>
    private static ProductionOrder OneRoll(decimal good, decimal scrap = 30m)
    {
        var order = Order(RollA);
        Realize(order, RollA, 1_000m, scrap, good);
        return order;
    }

    private static ClosureExceptionApprovalV1 Approval(ProductionOrder order, ClosureTolerancePolicySnapshotV1 policy) =>
        ClosureExceptionApprovalV1.Create(Company, order.Id, Closer, "SENTETIK teyitli sapma", order.Version,
            policy.PolicyId, policy.PolicyVersion, CloseAt);

    private static void AssertStillOpen(ProductionOrder order) =>
        Assert.Equal((OrderStatus.Open, (ClosureTolerancePolicySnapshotV1?)null, (ClosureExceptionApprovalV1?)null,
                (Guid?)null),
            (order.Status, order.ClosurePolicy, order.ClosureExceptionApproval, order.ClosedByUserId));

    [Theory]
    [InlineData(0.001)]
    [InlineData(15)]
    [InlineData(20)] // + 2 % exactly: the old inclusive bound accepted it
    public void Output_above_input_within_two_percent_is_refused_with_no_tolerance_and_no_approval_path(double excess)
    {
        var order = OneRoll(970m + (decimal)excess);
        var catalog = Catalog();

        Assert.True(order.BalanceOf(RollA).OutputExceedsInput);
        var assessment = order.AssessClosure(catalog);
        Assert.Equal((ClosureDispositionV1.Refused, ClosureAssessmentCodesV1.OutputExceedsInput, (string?)null),
            (assessment.Disposition, assessment.Code, assessment.RefusedParameter));
        var roll = Assert.Single(assessment.Rolls);
        Assert.Equal((ClosureDispositionV1.Refused, -(decimal)excess, (decimal?)null, (decimal?)null),
            (roll.Disposition, roll.DifferenceKg, roll.DifferencePercent, roll.FirePercent));

        Assert.Equal(ClosureAssessmentCodesV1.OutputExceedsInput, Assert.Throws<ManufacturingRuleException>(
            () => order.CloseWithPolicy(order.Version, Closer, CloseAt, catalog, null)).Code);
        Assert.Equal(ClosureAssessmentCodesV1.OutputExceedsInput, Assert.Throws<ManufacturingRuleException>(
            () => order.CloseWithPolicy(order.Version, Closer, CloseAt, catalog, Approval(order, catalog))).Code);
        AssertStillOpen(order);
    }

    [Theory]
    [InlineData(20.001, 30)]
    [InlineData(50, 30)]
    [InlineData(50, 60)] // fire above the maximum as well: still the same refusal, not an approval
    public void Output_above_input_beyond_two_percent_is_refused_even_with_a_matching_exception_approval(
        double excess, double scrap)
    {
        var order = OneRoll(1_000m - (decimal)scrap + (decimal)excess, (decimal)scrap);
        var catalog = Catalog();
        var approval = Approval(order, catalog);
        Assert.True(approval.Matches(Company, order.Id, Closer, order.Version, catalog));

        var assessment = order.AssessClosure(catalog);
        Assert.Equal((ClosureDispositionV1.Refused, ClosureAssessmentCodesV1.OutputExceedsInput),
            (assessment.Disposition, assessment.Code));
        Assert.Equal(-(decimal)excess, Assert.Single(assessment.Rolls).DifferenceKg);

        Assert.Equal(ClosureAssessmentCodesV1.OutputExceedsInput, Assert.Throws<ManufacturingRuleException>(
            () => order.CloseWithPolicy(order.Version, Closer, CloseAt, catalog, approval)).Code);
        AssertStillOpen(order);
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(20)] // - 2 % exactly: inside (inclusive bound), unchanged
    public void A_shortfall_within_two_percent_still_closes_without_approval(double shortfall)
    {
        var order = OneRoll(970m - (decimal)shortfall);
        var catalog = Catalog();

        Assert.False(order.BalanceOf(RollA).OutputExceedsInput);
        var assessment = order.AssessClosure(catalog);
        var roll = Assert.Single(assessment.Rolls);
        Assert.Equal((ClosureDispositionV1.Balanced, (string?)null, (decimal)shortfall, (decimal?)((decimal)shortfall / 10m)),
            (assessment.Disposition, roll.Code, roll.DifferenceKg, roll.DifferencePercent));

        order.CloseWithPolicy(order.Version, Closer, CloseAt, catalog, null);
        Assert.Equal((OrderStatus.Closed, catalog, (ClosureExceptionApprovalV1?)null),
            (order.Status, order.ClosurePolicy, order.ClosureExceptionApproval));
    }

    [Fact]
    public void A_shortfall_beyond_two_percent_still_needs_the_exception_approval()
    {
        var order = OneRoll(940m); // 30 kg short: 3 %
        var catalog = Catalog();

        var assessment = order.AssessClosure(catalog);
        Assert.Equal((ClosureDispositionV1.ApprovalRequired, ClosureAssessmentCodesV1.MassDifferenceOutsideTolerance, 30m),
            (assessment.Disposition, assessment.Code, assessment.Rolls[0].DifferenceKg));
        Assert.Equal(ClosureAssessmentCodesV1.ApprovalRequired, Assert.Throws<ManufacturingRuleException>(
            () => order.CloseWithPolicy(order.Version, Closer, CloseAt, catalog, null)).Code);
        AssertStillOpen(order);

        var approval = Approval(order, catalog);
        order.CloseWithPolicy(order.Version, Closer, CloseAt, catalog, approval);
        Assert.Equal((OrderStatus.Closed, catalog, approval),
            (order.Status, order.ClosurePolicy, order.ClosureExceptionApproval));
    }

    [Fact]
    public void Output_above_input_on_one_roll_refuses_the_close_even_when_the_order_total_is_short()
    {
        // URT-D42 (1): roll A has 10 kg more output than input, roll B is 20 kg (2 %) short. The order total is 10 kg
        // short and B alone would close inside the tolerance; A still refuses the close, with or without an approval.
        var order = Order(RollA, RollB);
        Realize(order, RollA, 1_000m, 30m, 980m);
        Realize(order, RollB, 1_000m, 30m, 950m);
        var catalog = Catalog();

        Assert.Equal(10m, order.BalanceOf(RollA).Unexplained + order.BalanceOf(RollB).Unexplained);
        Assert.Equal((true, false),
            (order.BalanceOf(RollA).OutputExceedsInput, order.BalanceOf(RollB).OutputExceedsInput));

        var assessment = order.AssessClosure(catalog);
        Assert.Equal((ClosureDispositionV1.Refused, ClosureAssessmentCodesV1.OutputExceedsInput),
            (assessment.Disposition, assessment.Code));
        Assert.Equal(
            new (Guid, ClosureDispositionV1, decimal, string?)[]
            {
                (RollA, ClosureDispositionV1.Refused, -10m, ClosureAssessmentCodesV1.OutputExceedsInput),
                (RollB, ClosureDispositionV1.Balanced, 20m, null),
            },
            assessment.Rolls.Select(roll => (roll.ParentRollId, roll.Disposition, roll.DifferenceKg, roll.Code)));

        Assert.Equal(ClosureAssessmentCodesV1.OutputExceedsInput, Assert.Throws<ManufacturingRuleException>(
            () => order.CloseWithPolicy(order.Version, Closer, CloseAt, catalog, null)).Code);
        Assert.Equal(ClosureAssessmentCodesV1.OutputExceedsInput, Assert.Throws<ManufacturingRuleException>(
            () => order.CloseWithPolicy(order.Version, Closer, CloseAt, catalog, Approval(order, catalog))).Code);
        AssertStillOpen(order);
    }

    [Fact]
    public void Without_a_known_mass_tolerance_output_above_input_is_refused_not_sent_to_review()
    {
        var order = OneRoll(975m); // 5 kg more output than input
        var published = Published(null);

        var assessment = order.AssessClosure(published);
        Assert.Equal((ClosureDispositionV1.Refused, ClosureAssessmentCodesV1.OutputExceedsInput, -5m),
            (assessment.Disposition, assessment.Code, assessment.Rolls[0].DifferenceKg));
        Assert.Equal(ClosureAssessmentCodesV1.OutputExceedsInput, Assert.Throws<ManufacturingRuleException>(
            () => order.CloseWithPolicy(order.Version, Closer, CloseAt, published, null)).Code);
        AssertStillOpen(order);
    }

    [Fact]
    public void Usable_remnant_and_the_latest_wip_reading_count_on_the_output_side_until_DEC_PRDCT_006_says_otherwise()
    {
        // Fail-closed reading (DEC-PRDCT-006 open): 30 scrap + 940 good + 20 usable remnant + 15 WIP = 1005 kg against
        // 1000 kg issued. Good + second + scrap + process loss alone (970) would be short; the close is still refused.
        var order = Order(RollA);
        Realize(order, RollA, 1_000m, 30m, 940m);
        order.Declare(RollA, OutputClass.UsableRemnant, Kg(20m));
        order.MeasureWip(RollA, Kg(15m));

        var assessment = order.AssessClosure(Catalog());
        Assert.Equal((ClosureDispositionV1.Refused, ClosureAssessmentCodesV1.OutputExceedsInput, -5m),
            (assessment.Disposition, assessment.Code, assessment.Rolls[0].DifferenceKg));
    }

    [Theory]
    [InlineData(15, false)] // within the earlier ± tolerance: closed without approval
    [InlineData(50, true)] // beyond it: closed with the earlier exception approval
    public void A_close_stored_under_the_earlier_rule_with_output_above_input_restores_unchanged_and_a_new_close_is_refused(
        double excess, bool approved)
    {
        // URT-D42 (3): "Kural yeni kapanışlara uygulanır". A stored close is replayed under the rule it was written with,
        // so the order stays closed and readable exactly as stored; the same facts on an open order are refused.
        var order = OneRoll(970m + (decimal)excess);
        var catalog = Catalog();
        var approval = approved ? Approval(order, catalog) : null;

        var restored = ProductionOrder.Restore(order.Id, order.PlanId, Company, WorkCenter, ManufacturingProcess.Slitting,
            order.Target, order.PlannedInputs, order.Issues, order.Returns, order.Outputs, order.WipMeasurements,
            Closer, CloseAt, order.Version + 1, catalog, approval);

        Assert.Equal(
            (OrderStatus.Closed, order.Version + 1, catalog, approval, (Guid?)Closer, (DateTimeOffset?)CloseAt),
            (restored.Status, restored.Version, restored.ClosurePolicy, restored.ClosureExceptionApproval,
                restored.ClosedByUserId, restored.ClosedAtUtc));
        Assert.Equal((true, -(decimal)excess),
            (restored.BalanceOf(RollA).OutputExceedsInput, restored.BalanceOf(RollA).Unexplained));

        Assert.Equal(ClosureAssessmentCodesV1.OutputExceedsInput, Assert.Throws<ManufacturingRuleException>(
            () => order.CloseWithPolicy(order.Version, Closer, CloseAt, catalog, approval)).Code);
        AssertStillOpen(order);
    }
}
