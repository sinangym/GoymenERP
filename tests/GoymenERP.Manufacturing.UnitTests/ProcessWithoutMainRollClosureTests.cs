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
/// (URT-D41/D42): no tolerance, no approval path, a stored close replays under the rule it was written with. URT-D68:
/// a paint order measures its shortfall tolerance and fire percent on the order total, with the approval path of
/// URT-D42 (2) kept outside it; a paint close stored under the earlier per-input rule still restores. URT-D69: only paint
/// runs without a coil.
/// </summary>
public sealed class ProcessWithoutMainRollClosureTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000d049");
    private static readonly Guid InputA = Guid.Parse("30000000-0000-0000-0000-00000000d001");
    private static readonly Guid InputB = Guid.Parse("30000000-0000-0000-0000-00000000d002");
    private static readonly Guid InputC = Guid.Parse("30000000-0000-0000-0000-00000000d003");
    private static readonly Guid Planner = Guid.Parse("40000000-0000-0000-0000-00000000d001");
    private static readonly Guid Closer = Guid.Parse("40000000-0000-0000-0000-00000000d002");
    private static readonly Guid Profile = Guid.Parse("50000000-0000-0000-0000-00000000d001");
    private static readonly DateOnly Today = new(2026, 11, 15);
    private static readonly DateTimeOffset CloseAt = new(2026, 11, 15, 15, 0, 0, TimeSpan.Zero);

    private static Kilograms Kg(decimal value) => new(value);

    /// <summary>The reviewed version-0 catalog of <paramref name="process"/>: fire maximum 5 %, mass balance ± 2 %.</summary>
    private static ClosureTolerancePolicySnapshotV1 Catalog(ManufacturingProcess process, decimal? massBalance = 2m,
        decimal maximumFire = 5m) =>
        new(Company, Profile, 0, process, maximumFire, true, new DateOnly(2026, 11, 1), null, Today, massBalance,
            ClosureTolerancePolicySnapshotV1.CatalogSource);

    private static ProductionOrder Order(ManufacturingProcess process, params Guid[] inputs)
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, process,
            inputs.Select(input => new PlannedInput(input, Kg(10_000m))));
        plan.WithDueDate().Approve(plan.Version, Planner, DateTimeOffset.UnixEpoch);
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

    /// <summary>
    /// URT-D68 golden: three packages of 1000 kg each; the 60 kg of scrap are weighed on package A only and everything
    /// else is good product. Package A alone would be a 6 % fire; on the order total it is 60 / 3000 = 2 %.
    /// </summary>
    private static ProductionOrder GoldenThreePackages(ManufacturingProcess process)
    {
        var order = Order(process, InputA, InputB, InputC);
        Realize(order, InputA, 1_000m, 60m, 940m);
        foreach (var input in new[] { InputB, InputC })
        {
            order.Issue(input, Kg(1_000m));
            order.Declare(input, OutputClass.GoodProduct, Kg(1_000m));
        }

        return order;
    }

    [Fact]
    public void Golden_a_paint_order_measures_the_fire_percent_on_the_order_total_and_closes_without_approval()
    {
        // URT-D68: 3 x 1000 kg in, 60 kg scrap -> 2 % on the order total; with a 2 % fire maximum the close needs no
        // approval. Each input row carries only its own difference; the order total row carries the decision.
        var order = GoldenThreePackages(ManufacturingProcess.Paint);
        var catalog = Catalog(ManufacturingProcess.Paint, maximumFire: 2m);

        var assessment = order.AssessClosure(catalog);

        Assert.Equal((ClosureDispositionV1.Balanced, (string?)null), (assessment.Disposition, assessment.Code));
        Assert.NotNull(assessment.OrderTotal);
        Assert.Equal((Guid.Empty, ClosureDispositionV1.Balanced, 0m, (decimal?)2m, (string?)null),
            (assessment.OrderTotal!.ParentRollId, assessment.OrderTotal.Disposition, assessment.OrderTotal.DifferenceKg,
                assessment.OrderTotal.FirePercent, assessment.OrderTotal.Code));
        Assert.Equal(
            new (Guid, ClosureDispositionV1, decimal, decimal?)[]
            {
                (InputA, ClosureDispositionV1.Balanced, 0m, null),
                (InputB, ClosureDispositionV1.Balanced, 0m, null),
                (InputC, ClosureDispositionV1.Balanced, 0m, null),
            },
            assessment.Rolls.Select(roll => (roll.ParentRollId, roll.Disposition, roll.DifferenceKg, roll.FirePercent)));

        order.CloseWithPolicy(order.Version, Closer, CloseAt, catalog, null);
        Assert.Equal((OrderStatus.Closed, catalog, (ClosureExceptionApprovalV1?)null),
            (order.Status, order.ClosurePolicy, order.ClosureExceptionApproval));
    }

    [Theory]
    [InlineData(ManufacturingProcess.Slitting)]
    [InlineData(ManufacturingProcess.ColdRolling)]
    [InlineData(ManufacturingProcess.TubeProfile)]
    [InlineData(ManufacturingProcess.CutToLength)]
    public void The_same_three_packages_on_a_coil_process_are_measured_per_coil_and_need_approval(
        ManufacturingProcess process)
    {
        // URT-D69 with URT-D42 (1): every process but paint runs per coil; coil A is a 6 % fire over the 2 % maximum.
        var order = GoldenThreePackages(process);
        var catalog = Catalog(process, maximumFire: 2m);

        var assessment = order.AssessClosure(catalog);

        Assert.Null(assessment.OrderTotal);
        Assert.Equal((ClosureDispositionV1.ApprovalRequired, ClosureAssessmentCodesV1.FireAboveMaximum),
            (assessment.Disposition, assessment.Code));
        Assert.Equal((InputA, (decimal?)6m), (assessment.Rolls[0].ParentRollId, assessment.Rolls[0].FirePercent));
        Assert.Equal(ClosureAssessmentCodesV1.ApprovalRequired, Assert.Throws<ManufacturingRuleException>(
            () => order.CloseWithPolicy(order.Version, Closer, CloseAt, catalog, null)).Code);
        AssertStillOpen(order);
    }

    [Fact]
    public void A_paint_order_whose_total_fire_is_above_the_maximum_keeps_the_approval_path()
    {
        // URT-D68 with URT-D42 (2): 60 kg over 3000 kg is 2 %; under a 1.5 % maximum the order total needs the exception
        // approval, and closes with it.
        var order = GoldenThreePackages(ManufacturingProcess.Paint);
        var catalog = Catalog(ManufacturingProcess.Paint, maximumFire: 1.5m);

        var assessment = order.AssessClosure(catalog);

        Assert.Equal((ClosureDispositionV1.ApprovalRequired, ClosureAssessmentCodesV1.FireAboveMaximum),
            (assessment.Disposition, assessment.Code));
        Assert.Equal(ClosureAssessmentCodesV1.ApprovalRequired, Assert.Throws<ManufacturingRuleException>(
            () => order.CloseWithPolicy(order.Version, Closer, CloseAt, catalog, null)).Code);
        AssertStillOpen(order);

        var approval = Approval(order, catalog);
        order.CloseWithPolicy(order.Version, Closer, CloseAt, catalog, approval);
        Assert.Equal((OrderStatus.Closed, approval), (order.Status, order.ClosureExceptionApproval));
    }

    [Fact]
    public void A_paint_order_whose_total_shortfall_is_outside_the_tolerance_needs_approval()
    {
        // URT-D68: 1000 kg in on A and B; A is 10 kg short and B 40 kg short. Per input B (4 %) alone would be outside
        // the 2 % tolerance and A inside; the order total, 50 kg over 2000 kg = 2.5 %, decides: outside, approval.
        var order = Order(ManufacturingProcess.Paint, InputA, InputB);
        Realize(order, InputA, 1_000m, 20m, 970m);
        Realize(order, InputB, 1_000m, 20m, 940m);
        var catalog = Catalog(ManufacturingProcess.Paint);

        var assessment = order.AssessClosure(catalog);

        Assert.Equal((ClosureDispositionV1.ApprovalRequired, ClosureAssessmentCodesV1.MassDifferenceOutsideTolerance),
            (assessment.Disposition, assessment.Code));
        Assert.Equal((50m, ClosureDispositionV1.ApprovalRequired),
            (assessment.OrderTotal!.DifferenceKg, assessment.OrderTotal.Disposition));
        Assert.All(assessment.Rolls, roll => Assert.Equal(ClosureDispositionV1.Balanced, roll.Disposition));
    }

    [Fact]
    public void A_paint_close_stored_with_a_per_input_approval_the_order_total_no_longer_needs_restores_unchanged()
    {
        // URT-D68 with URT-D42 (3): before URT-D68 package A's 6 % fire needed an approval under the 5 % maximum and the
        // close was stored with it. The order total (2 %) needs none, so a new close with that approval is refused; the
        // stored close still restores under the per-input rule it was written with.
        var order = GoldenThreePackages(ManufacturingProcess.Paint);
        var catalog = Catalog(ManufacturingProcess.Paint);
        var approval = Approval(order, catalog);

        var restored = ProductionOrder.Restore(order.Id, order.PlanId, Company, WorkCenter, ManufacturingProcess.Paint,
            order.Target, order.PlannedInputs, order.Issues, order.Returns, order.Outputs, order.WipMeasurements,
            Closer, CloseAt, order.Version + 1, catalog, approval);

        Assert.Equal((OrderStatus.Closed, catalog, approval),
            (restored.Status, restored.ClosurePolicy, restored.ClosureExceptionApproval));
        Assert.Equal(ClosureAssessmentCodesV1.ApprovalUnexpected, Assert.Throws<ManufacturingRuleException>(
            () => order.CloseWithPolicy(order.Version, Closer, CloseAt, catalog, approval)).Code);
        AssertStillOpen(order);
        order.CloseWithPolicy(order.Version, Closer, CloseAt, catalog, null);
        Assert.Equal(OrderStatus.Closed, order.Status);
    }
}
