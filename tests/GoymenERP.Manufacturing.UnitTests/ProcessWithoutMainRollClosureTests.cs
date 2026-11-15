using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Balance;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-D48..D50 on the close: the output side of "output above input" is the whole BAL-003 accounted mass (usable
/// remnant, the latest WIP reading and corrections included); a process without a main roll input (paint) compares the
/// order's total net issued kg with its total accounted kg, while the roll-based processes keep the per-roll rule of
/// URT-D42 (1); outputs enter as their weighed kilograms whatever their counting unit. The refusal itself is unchanged
/// (URT-D41/D42): no tolerance, no approval path, a stored close replays under the rule it was written with.
/// </summary>
public sealed class ProcessWithoutMainRollClosureTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000d049");
    private static readonly Guid InputA = Guid.Parse("30000000-0000-0000-0000-00000000d001");
    private static readonly Guid InputB = Guid.Parse("30000000-0000-0000-0000-00000000d002");
    private static readonly Guid Planner = Guid.Parse("40000000-0000-0000-0000-00000000d001");
    private static readonly Guid Closer = Guid.Parse("40000000-0000-0000-0000-00000000d002");
    private static readonly Guid Profile = Guid.Parse("50000000-0000-0000-0000-00000000d001");
    private static readonly DateOnly Today = new(2026, 11, 15);
    private static readonly DateTimeOffset CloseAt = new(2026, 11, 15, 15, 0, 0, TimeSpan.Zero);

    private static Kilograms Kg(decimal value) => new(value);

    /// <summary>The reviewed version-0 catalog of <paramref name="process"/>: fire maximum 5 %, mass balance ± 2 %.</summary>
    private static ClosureTolerancePolicySnapshotV1 Catalog(ManufacturingProcess process, decimal? massBalance = 2m) =>
        new(Company, Profile, 0, process, 5m, true, new DateOnly(2026, 11, 1), null, Today, massBalance,
            ClosureTolerancePolicySnapshotV1.CatalogSource);

    private static ProductionOrder Order(ManufacturingProcess process, params Guid[] inputs)
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, process,
            inputs.Select(input => new PlannedInput(input, Kg(10_000m))));
        plan.Approve(plan.Version, Planner, DateTimeOffset.UnixEpoch);
        return ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
    }

    private static void Realize(ProductionOrder order, Guid input, decimal issued, decimal scrap, decimal good)
    {
        order.Issue(input, Kg(issued));
        order.Declare(input, OutputClass.Scrap, Kg(scrap));
        order.Declare(input, OutputClass.GoodProduct, Kg(good));
    }

    private static ClosureExceptionApprovalV1 Approval(ProductionOrder order, ClosureTolerancePolicySnapshotV1 policy) =>
        ClosureExceptionApprovalV1.Create(Company, order.Id, Closer, "SENTETIK teyitli sapma", order.Version,
            policy.PolicyId, policy.PolicyVersion, CloseAt);

    private static void AssertStillOpen(ProductionOrder order) =>
        Assert.Equal((OrderStatus.Open, (ClosureTolerancePolicySnapshotV1?)null, (ClosureExceptionApprovalV1?)null,
                (Guid?)null),
            (order.Status, order.ClosurePolicy, order.ClosureExceptionApproval, order.ClosedByUserId));

    private static void AssertRefusedWithAndWithoutApproval(ProductionOrder order, ClosureTolerancePolicySnapshotV1 policy)
    {
        Assert.Equal(ClosureAssessmentCodesV1.OutputExceedsInput, Assert.Throws<ManufacturingRuleException>(
            () => order.CloseWithPolicy(order.Version, Closer, CloseAt, policy, null)).Code);
        Assert.Equal(ClosureAssessmentCodesV1.OutputExceedsInput, Assert.Throws<ManufacturingRuleException>(
            () => order.CloseWithPolicy(order.Version, Closer, CloseAt, policy, Approval(order, policy))).Code);
        AssertStillOpen(order);
    }

    [Theory]
    [InlineData(ManufacturingProcess.Slitting, true)]
    [InlineData(ManufacturingProcess.ColdRolling, true)]
    [InlineData(ManufacturingProcess.TubeProfile, true)]
    [InlineData(ManufacturingProcess.CutToLength, true)]
    [InlineData(ManufacturingProcess.Paint, false)]
    public void Only_paint_runs_without_a_main_roll_input_and_the_order_reports_the_scope_it_measures(
        ManufacturingProcess process, bool perRoll)
    {
        Assert.Equal(perRoll, ManufacturingProcessMainInputV1.HasMainRollInput(process));
        Assert.Equal(perRoll, Order(process, InputA).MeasuresOutputAboveInputPerRoll);
    }

    [Fact]
    public void An_undefined_process_has_no_input_shape()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ManufacturingProcessMainInputV1.HasMainRollInput((ManufacturingProcess)999));
    }

    [Fact]
    public void A_paint_order_whose_total_is_short_closes_even_with_one_input_above_its_own_input()
    {
        // URT-D49 (1): input A accounts 10 kg more than it was issued, input B 20 kg (2 %) less. The order total is 10 kg
        // short, so a paint order is not above input; each input stays inside the ± 2 % tolerance and the close succeeds.
        var order = Order(ManufacturingProcess.Paint, InputA, InputB);
        Realize(order, InputA, 1_000m, 30m, 980m);
        Realize(order, InputB, 1_000m, 30m, 950m);
        var catalog = Catalog(ManufacturingProcess.Paint);

        Assert.Equal((true, false), (order.BalanceOf(InputA).OutputExceedsInput, order.TotalBalance().OutputExceedsInput));
        Assert.Equal((2_000m, 10m), (order.TotalBalance().NetIssued.Value, order.TotalBalance().Unexplained));
        Assert.False(order.HasOutputAboveInput());

        var assessment = order.AssessClosure(catalog);
        Assert.Equal((ClosureDispositionV1.Balanced, (string?)null), (assessment.Disposition, assessment.Code));
        Assert.Equal(
            new (Guid, ClosureDispositionV1, decimal, string?)[]
            {
                (InputA, ClosureDispositionV1.Balanced, -10m, null),
                (InputB, ClosureDispositionV1.Balanced, 20m, null),
            },
            assessment.Rolls.Select(roll => (roll.ParentRollId, roll.Disposition, roll.DifferenceKg, roll.Code)));

        order.CloseWithPolicy(order.Version, Closer, CloseAt, catalog, null);
        Assert.Equal((OrderStatus.Closed, catalog, (ClosureExceptionApprovalV1?)null),
            (order.Status, order.ClosurePolicy, order.ClosureExceptionApproval));
    }

    [Theory]
    [InlineData(ManufacturingProcess.Slitting)]
    [InlineData(ManufacturingProcess.ColdRolling)]
    [InlineData(ManufacturingProcess.TubeProfile)]
    [InlineData(ManufacturingProcess.CutToLength)]
    public void The_same_facts_on_a_roll_based_process_are_refused_on_the_roll_above_input(ManufacturingProcess process)
    {
        // URT-D49 (2), URT-D42 (1): a roll-based process measures each parent roll on its own; B's shortfall never
        // offsets A.
        var order = Order(process, InputA, InputB);
        Realize(order, InputA, 1_000m, 30m, 980m);
        Realize(order, InputB, 1_000m, 30m, 950m);
        var catalog = Catalog(process);

        Assert.Equal((false, true), (order.TotalBalance().OutputExceedsInput, order.HasOutputAboveInput()));

        var assessment = order.AssessClosure(catalog);
        Assert.Equal((ClosureDispositionV1.Refused, ClosureAssessmentCodesV1.OutputExceedsInput),
            (assessment.Disposition, assessment.Code));
        Assert.Equal(
            new (Guid, ClosureDispositionV1, string?)[]
            {
                (InputA, ClosureDispositionV1.Refused, ClosureAssessmentCodesV1.OutputExceedsInput),
                (InputB, ClosureDispositionV1.Balanced, null),
            },
            assessment.Rolls.Select(roll => (roll.ParentRollId, roll.Disposition, roll.Code)));
        AssertRefusedWithAndWithoutApproval(order, catalog);
    }

    [Theory]
    [InlineData(2.0)] // the earlier ± 2 % tolerance known
    [InlineData(null)] // no known tolerance: still a refusal, never a review
    public void A_paint_order_whose_total_is_above_input_is_refused_on_the_input_above_its_own_input(double? tolerance)
    {
        // URT-D49 (1), URT-D41 (1)-(2): A is 10 kg above, B 5 kg short; the order total is 5 kg above input. A carries the
        // refusal, B stays balanced, and neither the tolerance nor an approval opens the close.
        var order = Order(ManufacturingProcess.Paint, InputA, InputB);
        Realize(order, InputA, 1_000m, 30m, 980m);
        Realize(order, InputB, 1_000m, 30m, 965m);
        var catalog = Catalog(ManufacturingProcess.Paint, (decimal?)tolerance);

        Assert.Equal((-5m, true, true),
            (order.TotalBalance().Unexplained, order.TotalBalance().OutputExceedsInput, order.HasOutputAboveInput()));

        var assessment = order.AssessClosure(catalog);
        Assert.Equal((ClosureDispositionV1.Refused, ClosureAssessmentCodesV1.OutputExceedsInput, (string?)null),
            (assessment.Disposition, assessment.Code, assessment.RefusedParameter));
        var first = assessment.Rolls[0];
        Assert.Equal((InputA, ClosureDispositionV1.Refused, -10m, ClosureAssessmentCodesV1.OutputExceedsInput),
            (first.ParentRollId, first.Disposition, first.DifferenceKg, first.Code));
        Assert.Equal((InputB, 5m), (assessment.Rolls[1].ParentRollId, assessment.Rolls[1].DifferenceKg));
        Assert.NotEqual(ClosureDispositionV1.Refused, assessment.Rolls[1].Disposition);
        AssertRefusedWithAndWithoutApproval(order, catalog);
    }

    [Fact]
    public void Usable_remnant_and_the_latest_wip_reading_push_a_paint_order_total_above_input_and_refuse_the_close()
    {
        // URT-D48 (1)-(2): usable remnant and the latest WIP reading are on the output side. Without them the total is
        // 15 kg short; with 10 kg WIP on A and 10 kg usable remnant on B it is 5 kg above input.
        var order = Order(ManufacturingProcess.Paint, InputA, InputB);
        Realize(order, InputA, 1_000m, 30m, 960m);
        Realize(order, InputB, 1_000m, 30m, 965m);
        var catalog = Catalog(ManufacturingProcess.Paint);
        Assert.Equal((15m, ClosureDispositionV1.Balanced),
            (order.TotalBalance().Unexplained, order.AssessClosure(catalog).Disposition));

        order.MeasureWip(InputA, Kg(4m));
        order.MeasureWip(InputA, Kg(10m)); // the latest reading replaces the earlier one
        order.Declare(InputB, OutputClass.UsableRemnant, Kg(10m));

        var total = order.TotalBalance();
        Assert.Equal((2_000m, -5m, true), (total.NetIssued.Value, total.Unexplained, total.OutputExceedsInput));
        var assessment = order.AssessClosure(catalog);
        Assert.Equal((ClosureDispositionV1.Refused, ClosureAssessmentCodesV1.OutputExceedsInput),
            (assessment.Disposition, assessment.Code));
        Assert.Equal((InputB, -5m, ClosureAssessmentCodesV1.OutputExceedsInput),
            (assessment.Rolls[1].ParentRollId, assessment.Rolls[1].DifferenceKg, assessment.Rolls[1].Code));
        AssertRefusedWithAndWithoutApproval(order, catalog);
    }

    [Fact]
    public void A_tube_profile_output_counted_in_pieces_enters_as_its_weighed_kilograms_and_is_refused_per_roll()
    {
        // URT-D50 (2): a tube/profile output is counted in pieces or metres on the floor but declared as the weighed kg;
        // the close reads only that weight. 1003 kg weighed good product and 30 kg scrap against 1000 kg issued: 33 kg
        // above input on the one roll.
        var order = Order(ManufacturingProcess.TubeProfile, InputA);
        Realize(order, InputA, 1_000m, 30m, 1_003m);
        var catalog = Catalog(ManufacturingProcess.TubeProfile);

        Assert.Equal((1_003m, -33m), (order.BalanceOf(InputA).OutputOf(OutputClass.GoodProduct).Value,
            order.BalanceOf(InputA).Unexplained));
        var assessment = order.AssessClosure(catalog);
        Assert.Equal((ClosureDispositionV1.Refused, ClosureAssessmentCodesV1.OutputExceedsInput, -33m),
            (assessment.Disposition, assessment.Code, Assert.Single(assessment.Rolls).DifferenceKg));
        AssertRefusedWithAndWithoutApproval(order, catalog);
    }

    [Fact]
    public void A_paint_close_stored_under_the_earlier_rule_with_its_total_above_input_restores_unchanged()
    {
        // URT-D42 (3): the stored close replays the rule it was written under; the same facts on an open order are refused.
        var order = Order(ManufacturingProcess.Paint, InputA, InputB);
        Realize(order, InputA, 1_000m, 30m, 980m);
        Realize(order, InputB, 1_000m, 30m, 965m);
        var catalog = Catalog(ManufacturingProcess.Paint);

        var restored = ProductionOrder.Restore(order.Id, order.PlanId, Company, WorkCenter, ManufacturingProcess.Paint,
            order.Target, order.PlannedInputs, order.Issues, order.Returns, order.Outputs, order.WipMeasurements,
            Closer, CloseAt, order.Version + 1, catalog, null);

        Assert.Equal((OrderStatus.Closed, order.Version + 1, catalog, (Guid?)Closer, (DateTimeOffset?)CloseAt),
            (restored.Status, restored.Version, restored.ClosurePolicy, restored.ClosedByUserId, restored.ClosedAtUtc));
        Assert.Equal((-5m, true), (restored.TotalBalance().Unexplained, restored.HasOutputAboveInput()));
        AssertRefusedWithAndWithoutApproval(order, catalog);
    }

    [Fact]
    public void The_order_total_adds_every_input_category_by_category_through_the_same_calculator()
    {
        // A: 1000 issued, 100 returned, 30 scrap, 850 good, 10 usable remnant, 5 WIP -> 900 net, 895 accounted, 5 short.
        // B: 500 issued, 370 good, 100 second quality, 20 process loss -> 500 net, 490 accounted, 10 short.
        var order = Order(ManufacturingProcess.Paint, InputA, InputB);
        Realize(order, InputA, 1_000m, 30m, 850m);
        order.Return(InputA, Kg(100m));
        order.Declare(InputA, OutputClass.UsableRemnant, Kg(10m));
        order.MeasureWip(InputA, Kg(5m));
        order.Issue(InputB, Kg(500m));
        order.Declare(InputB, OutputClass.GoodProduct, Kg(370m));
        order.Declare(InputB, OutputClass.SecondQuality, Kg(100m));
        order.Declare(InputB, OutputClass.ProcessLoss, Kg(20m));

        var total = order.TotalBalance();
        Assert.Equal((1_400m, 1_385m, 15m, MassBalanceEngineV1.FormulaVersion),
            (total.NetIssued.Value, total.Reconciliation.AccountedKg, total.Unexplained,
                total.Reconciliation.FormulaVersion));
        Assert.Equal(order.BalanceOf(InputA).Unexplained + order.BalanceOf(InputB).Unexplained, total.Unexplained);
        Assert.Equal(total, OrderMassTotalV1.Of([order.BalanceOf(InputA), order.BalanceOf(InputB)]));
        Assert.Equal((false, false), (total.OutputExceedsInput, order.HasOutputAboveInput()));
    }

    [Fact]
    public void An_order_total_is_made_only_of_input_balances()
    {
        Assert.Throws<ArgumentNullException>(() => OrderMassTotalV1.Of(null!));
        Assert.Throws<ArgumentException>(() => OrderMassTotalV1.Of([null!]));
    }
}
