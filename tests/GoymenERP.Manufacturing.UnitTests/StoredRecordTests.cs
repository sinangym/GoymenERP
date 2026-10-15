using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// MFG-5: a stored plan or order is rebuilt through the rules that wrote it. A faithful record
/// comes back with the same state and the same answers (the MFG-4 close rule included); a record
/// that breaks a rule is refused, never repaired. The canonical content hash treats 5.00 kg and
/// 5 kg as one content and a different instant, roll or class as another.
/// </summary>
public sealed class StoredRecordTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000c001");
    private static readonly Guid RollA = Guid.Parse("30000000-0000-0000-0000-00000000a001");
    private static readonly Guid RollB = Guid.Parse("30000000-0000-0000-0000-00000000a002");
    private static readonly Guid Planner = Guid.Parse("40000000-0000-0000-0000-00000000b001");
    private static readonly Guid Closer = Guid.Parse("40000000-0000-0000-0000-00000000b002");
    private static readonly DateTimeOffset T0 = new(2026, 9, 18, 8, 0, 0, TimeSpan.Zero);

    private static Kilograms Kg(decimal value) => new(value);

    private static PlannedInput[] Inputs() => [new(RollA, Kg(10_000m)), new(RollB, Kg(5_000m))];

    private static ProductionOrder Restore(
        IEnumerable<InputIssue>? issues = null,
        IEnumerable<InputReturn>? returns = null,
        IEnumerable<OutputDeclaration>? outputs = null,
        IEnumerable<WipMeasurement>? wips = null,
        Guid? closedBy = null,
        DateTimeOffset? closedAt = null,
        long version = 1) =>
        ProductionOrder.Restore(Guid.NewGuid(), Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            new ProductionTarget(TargetBasis.InputMass, Kg(15_000m)), Inputs(),
            issues ?? [], returns ?? [], outputs ?? [], wips ?? [], closedBy, closedAt, version);

    [Fact]
    public void A_stored_plan_comes_back_draft_or_approved_with_its_version()
    {
        var draft = ProductionPlan.Restore(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting, Inputs(), null,
            null, null, 1);
        Assert.Equal((PlanStatus.Draft, 1L), (draft.Status, draft.Version));

        var approved = ProductionPlan.Restore(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting, Inputs(), null,
            Planner, T0, 2);
        Assert.Equal((PlanStatus.Approved, 2L, Planner, T0), (approved.Status, approved.Version, approved.ApprovedByUserId,
            approved.ApprovedAtUtc));
    }

    [Theory]
    [InlineData(ManufacturingProcess.ColdRolling)]
    [InlineData(ManufacturingProcess.TubeProfile)]
    [InlineData(ManufacturingProcess.Paint)]
    [InlineData(ManufacturingProcess.CutToLength)]
    public void A_new_non_slitting_plan_cannot_carry_a_slitting_spec(ManufacturingProcess process)
    {
        var error = Assert.Throws<ArgumentException>(() => new ProductionPlan(
            Guid.NewGuid(), Company, WorkCenter, process, Inputs(), spec: SlittingSpec()));

        Assert.Equal("spec", error.ParamName);
    }

    [Theory]
    [InlineData(ManufacturingProcess.ColdRolling)]
    [InlineData(ManufacturingProcess.TubeProfile)]
    [InlineData(ManufacturingProcess.Paint)]
    [InlineData(ManufacturingProcess.CutToLength)]
    public void A_stored_non_slitting_plan_with_a_slitting_spec_is_refused_not_repaired(ManufacturingProcess process)
    {
        var error = Assert.Throws<ManufacturingRuleException>(() => ProductionPlan.Restore(
            Guid.NewGuid(), Company, WorkCenter, process, Inputs(), target: null,
            approvedByUserId: null, approvedAtUtc: null, version: 1, spec: SlittingSpec()));

        Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid, error.Code);
    }

    [Fact]
    public void Slitting_with_its_spec_and_another_process_without_one_remain_valid()
    {
        var spec = SlittingSpec();
        var slitting = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            Inputs(), spec: spec);
        var coldRolling = ProductionPlan.Restore(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.ColdRolling,
            Inputs(), target: null, approvedByUserId: null, approvedAtUtc: null, version: 1);

        Assert.Same(spec, slitting.Spec);
        Assert.Null(coldRolling.Spec);
        Assert.Equal(ManufacturingProcess.ColdRolling, coldRolling.Process);
    }

    [Fact]
    public void Cut_to_length_lines_keep_client_identity_explicit_non_contiguous_sequence_and_input_order()
    {
        var first = Guid.Parse("50000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("50000000-0000-0000-0000-000000000002");
        var spec = new CutToLengthSpec(
        [
            new CutToLengthLine(first, 40, 6000.000m, 3),
            new CutToLengthLine(second, 7, 2500.5m, 2),
        ]);

        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.CutToLength,
            Inputs(), cutToLengthSpec: spec);
        var restored = ProductionPlan.Restore(plan.Id, Company, WorkCenter, ManufacturingProcess.CutToLength,
            Inputs(), target: null, approvedByUserId: null, approvedAtUtc: null, version: 1,
            cutToLengthSpec: spec);

        Assert.Equal(
            [(first, 40, 6000m, 3), (second, 7, 2500.5m, 2)],
            plan.CutToLengthSpec!.Lines.Select(line => (line.LineId, line.Sequence, line.LengthMm, line.Count)));
        Assert.Equal(plan.CutToLengthSpec.Lines, restored.CutToLengthSpec!.Lines);
    }

    [Theory]
    [InlineData(ManufacturingProcess.Slitting)]
    [InlineData(ManufacturingProcess.ColdRolling)]
    [InlineData(ManufacturingProcess.TubeProfile)]
    [InlineData(ManufacturingProcess.Paint)]
    public void Another_process_cannot_carry_cut_to_length_content(ManufacturingProcess process)
    {
        var spec = CutToLengthSpec();

        Assert.Equal("cutToLengthSpec", Assert.Throws<ArgumentException>(() => new ProductionPlan(
            Guid.NewGuid(), Company, WorkCenter, process, Inputs(), cutToLengthSpec: spec)).ParamName);
        Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid,
            Assert.Throws<ManufacturingRuleException>(() => ProductionPlan.Restore(
                Guid.NewGuid(), Company, WorkCenter, process, Inputs(), target: null,
                approvedByUserId: null, approvedAtUtc: null, version: 1,
                cutToLengthSpec: spec)).Code);
    }

    [Fact]
    public void Legacy_cut_to_length_plan_keeps_absent_lines_absent()
    {
        var restored = ProductionPlan.Restore(Guid.NewGuid(), Company, WorkCenter,
            ManufacturingProcess.CutToLength, Inputs(), target: null,
            approvedByUserId: null, approvedAtUtc: null, version: 1);

        Assert.Null(restored.CutToLengthSpec);
    }

    [Theory]
    [InlineData("approver-without-instant")]
    [InlineData("instant-without-approver")]
    [InlineData("draft-with-version-2")]
    [InlineData("approved-with-version-1")]
    public void A_stored_plan_that_breaks_a_rule_is_refused(string fault)
    {
        (Guid? By, DateTimeOffset? At, long Version) stored = fault switch
        {
            "approver-without-instant" => (Planner, null, 2L),
            "instant-without-approver" => (null, T0, 2L),
            "draft-with-version-2" => (null, null, 2L),
            _ => (Planner, T0, 1L),
        };
        var (by, at, version) = stored;

        var error = Assert.Throws<ManufacturingRuleException>(() => ProductionPlan.Restore(Guid.NewGuid(), Company, WorkCenter,
            ManufacturingProcess.Slitting, Inputs(), null, by, at, version));

        Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid, error.Code);
    }

    [Fact]
    public void A_stored_order_replays_its_facts_and_answers_as_it_did()
    {
        // Facts arrive in any order from storage; issues are replayed first, the rest in time order.
        var issueA = new InputIssue(Guid.NewGuid(), RollA, Kg(10_000m), T0);
        var issueB = new InputIssue(Guid.NewGuid(), RollB, Kg(5_000m), T0.AddMinutes(1));
        var back = new InputReturn(Guid.NewGuid(), RollB, Kg(1_000m), T0.AddHours(2));
        var goodA = new OutputDeclaration(Guid.NewGuid(), RollA, OutputClass.GoodProduct, Kg(9_700m), T0.AddHours(1),
            CostResult.NotCalculated);
        var scrapA = new OutputDeclaration(Guid.NewGuid(), RollA, OutputClass.Scrap, Kg(300m), T0.AddHours(1).AddMinutes(1),
            CostResult.NotCalculated);
        var goodB = new OutputDeclaration(Guid.NewGuid(), RollB, OutputClass.GoodProduct, Kg(2_500m), T0.AddHours(3),
            CostResult.NotCalculated);
        var wipB = new WipMeasurement(Guid.NewGuid(), RollB, Kg(1_500m), T0.AddHours(4));

        var order = Restore([issueB, issueA], [back], [goodB, scrapA, goodA], [wipB], Closer, T0.AddHours(5), version: 9);

        Assert.Equal((OrderStatus.Closed, 9L, Closer, T0.AddHours(5)), (order.Status, order.Version, order.ClosedByUserId,
            order.ClosedAtUtc));
        Assert.Equal(0m, order.BalanceOf(RollA).Unexplained);
        Assert.Equal((1_500m, 0m), (order.BalanceOf(RollB).WorkInProgress.Value, order.BalanceOf(RollB).Unexplained));
        Assert.Equal(ManufacturingRuleCodes.OrderClosed, Assert.Throws<ManufacturingRuleException>(
            () => order.IssueInput(order.Version, Guid.NewGuid(), RollA, Kg(1m), T0.AddHours(6))).Code);
    }

    [Fact]
    public void A_stored_close_must_still_reconcile_roll_by_roll()
    {
        // A "closed" order whose roll does not reconcile is not what the domain could have closed.
        var issue = new InputIssue(Guid.NewGuid(), RollA, Kg(10_000m), T0);
        var good = new OutputDeclaration(Guid.NewGuid(), RollA, OutputClass.GoodProduct, Kg(9_000m), T0.AddHours(1),
            CostResult.NotCalculated);

        var error = Assert.Throws<ManufacturingRuleException>(() => Restore([issue], outputs: [good], closedBy: Closer,
            closedAt: T0.AddHours(2), version: 4));

        Assert.Equal(ManufacturingRuleCodes.UnexplainedDifference, error.Code);
    }

    [Theory]
    [InlineData("version-too-high")]
    [InlineData("version-too-low")]
    [InlineData("closer-without-instant")]
    [InlineData("cost-the-domain-does-not-write")]
    public void A_stored_order_that_breaks_a_rule_is_refused_not_repaired(string fault)
    {
        var issue = new InputIssue(Guid.NewGuid(), RollA, Kg(10_000m), T0);
        var good = new OutputDeclaration(Guid.NewGuid(), RollA, OutputClass.GoodProduct, Kg(10_000m), T0.AddHours(1),
            fault == "cost-the-domain-does-not-write" ? CostResult.WaitingPolicy : CostResult.NotCalculated);

        var error = Assert.Throws<ManufacturingRuleException>(() => fault switch
        {
            "version-too-high" => Restore([issue], outputs: [good], version: 4),
            "version-too-low" => Restore([issue], outputs: [good], version: 2),
            "closer-without-instant" => Restore([issue], outputs: [good], closedBy: Closer, version: 3),
            _ => Restore([issue], outputs: [good], version: 3),
        });

        Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid, error.Code);
    }

    [Fact]
    public void Stored_facts_that_break_a_rule_are_refused_with_that_rule()
    {
        var issue = new InputIssue(Guid.NewGuid(), RollA, Kg(1_000m), T0);

        Assert.Equal(ManufacturingRuleCodes.ReturnExceedsIssued, Assert.Throws<ManufacturingRuleException>(() => Restore([issue],
            returns: [new InputReturn(Guid.NewGuid(), RollA, Kg(1_001m), T0.AddHours(1))], version: 3)).Code);
        Assert.Equal(ManufacturingRuleCodes.InputAlreadyIssued, Assert.Throws<ManufacturingRuleException>(
            () => Restore([issue, issue with { IssuedAtUtc = T0.AddMinutes(1) }], version: 3)).Code);
        Assert.Equal(ManufacturingRuleCodes.RollNotIssued, Assert.Throws<ManufacturingRuleException>(() => Restore([issue],
            wips: [new WipMeasurement(Guid.NewGuid(), RollB, Kg(1m), T0.AddHours(1))], version: 3)).Code);
    }

    [Fact]
    public void The_fact_content_hash_is_canonical()
    {
        var order = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 9, 18, 11, 0, 0, TimeSpan.FromHours(3));
        var hash = ManufacturingStore.FactHash(FactKind.OutputDeclaration, order, RollA, Kg(5.00m), at, OutputClass.Scrap);

        // Trailing zeros and the offset of the same instant are not content.
        Assert.Equal(hash, ManufacturingStore.FactHash(FactKind.OutputDeclaration, order, RollA, Kg(5m), at.ToUniversalTime(),
            OutputClass.Scrap));
        Assert.Equal(64, hash.Length);

        // Everything else is.
        Assert.NotEqual(hash, ManufacturingStore.FactHash(FactKind.OutputDeclaration, order, RollA, Kg(5.001m), at, OutputClass.Scrap));
        Assert.NotEqual(hash, ManufacturingStore.FactHash(FactKind.OutputDeclaration, order, RollB, Kg(5m), at, OutputClass.Scrap));
        Assert.NotEqual(hash, ManufacturingStore.FactHash(FactKind.OutputDeclaration, order, RollA, Kg(5m), at.AddSeconds(1),
            OutputClass.Scrap));
        Assert.NotEqual(hash, ManufacturingStore.FactHash(FactKind.OutputDeclaration, order, RollA, Kg(5m), at,
            OutputClass.ProcessLoss));
        Assert.NotEqual(hash, ManufacturingStore.FactHash(FactKind.OutputDeclaration, Guid.NewGuid(), RollA, Kg(5m), at,
            OutputClass.Scrap));
        Assert.NotEqual(hash, ManufacturingStore.FactHash(FactKind.WipMeasurement, order, RollA, Kg(5m), at, null));
    }

    [Fact]
    public void The_plan_content_hash_is_canonical()
    {
        var id = Guid.NewGuid();
        var plan = new ProductionPlan(id, Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(RollA, Kg(10_000.000m))], new ProductionTarget(TargetBasis.InputMass, Kg(10_000.0m)));
        var same = new ProductionPlan(id, Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(RollA, Kg(10_000m))], new ProductionTarget(TargetBasis.InputMass, Kg(10_000m)));
        var other = new ProductionPlan(id, Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(RollA, Kg(10_000m))], new ProductionTarget(TargetBasis.GoodOutputMass, Kg(10_000m)));

        Assert.Equal(ManufacturingStore.PlanHash(plan), ManufacturingStore.PlanHash(same));
        Assert.NotEqual(ManufacturingStore.PlanHash(plan), ManufacturingStore.PlanHash(other));
    }

    [Fact]
    public void The_inventory_label_version_is_positive_durable_plan_content()
    {
        var planId = Guid.NewGuid();
        var input = new PlannedInput(RollA, Kg(10_000m), 7);
        var same = new ProductionPlan(planId, Company, WorkCenter, ManufacturingProcess.Slitting,
            [input], new ProductionTarget(TargetBasis.InputMass, Kg(10_000m)));
        var newer = new ProductionPlan(planId, Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(RollA, Kg(10_000m), 8)], new ProductionTarget(TargetBasis.InputMass, Kg(10_000m)));

        Assert.Equal(7, input.ExpectedLabelVersion);
        Assert.NotEqual(ManufacturingStore.PlanHash(same), ManufacturingStore.PlanHash(newer));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlannedInput(RollA, Kg(10_000m), 0));
    }

    [Fact]
    public void A_plan_without_a_slitting_spec_keeps_its_MFG5_hash_so_every_stored_plan_keeps_its_replay_identity()
    {
        // The MFG-5 canonical content, written out: MFG-6 must not change it for a plan without a spec.
        const string mfg5Canonical = "manufacturing.plan.v1|10000000-0000-0000-0000-000000000001|"
            + "20000000-0000-0000-0000-00000000c001|1|1:10000|30000000-0000-0000-0000-00000000a001:10000;";
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(RollA, Kg(10_000.000m))], new ProductionTarget(TargetBasis.InputMass, Kg(10_000m)));

        Assert.Equal(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(mfg5Canonical)))
                .ToLowerInvariant(),
            ManufacturingStore.PlanHash(plan));
    }

    [Fact]
    public void Cut_to_length_lines_are_canonical_content_and_legacy_absence_keeps_the_old_hash()
    {
        var planId = Guid.NewGuid();
        var lineA = Guid.Parse("50000000-0000-0000-0000-000000000001");
        var lineB = Guid.Parse("50000000-0000-0000-0000-000000000002");
        ProductionPlan Plan(params CutToLengthLine[] lines) => new(
            planId, Company, WorkCenter, ManufacturingProcess.CutToLength,
            [new PlannedInput(RollA, Kg(10_000m))], cutToLengthSpec: new CutToLengthSpec(lines));

        var captured = Plan(new CutToLengthLine(lineA, 40, 6000.000m, 3),
            new CutToLengthLine(lineB, 7, 2500.5m, 2));
        var same = Plan(new CutToLengthLine(lineA, 40, 6000m, 3),
            new CutToLengthLine(lineB, 7, 2500.500m, 2));

        Assert.Equal(ManufacturingStore.PlanHash(captured), ManufacturingStore.PlanHash(same));
        Assert.NotEqual(ManufacturingStore.PlanHash(captured), ManufacturingStore.PlanHash(
            Plan(new CutToLengthLine(lineB, 7, 2500.5m, 2), new CutToLengthLine(lineA, 40, 6000m, 3))));
        Assert.NotEqual(ManufacturingStore.PlanHash(captured), ManufacturingStore.PlanHash(
            Plan(new CutToLengthLine(Guid.NewGuid(), 40, 6000m, 3), new CutToLengthLine(lineB, 7, 2500.5m, 2))));
        Assert.NotEqual(ManufacturingStore.PlanHash(captured), ManufacturingStore.PlanHash(
            Plan(new CutToLengthLine(lineA, 41, 6000m, 3), new CutToLengthLine(lineB, 7, 2500.5m, 2))));
        Assert.NotEqual(ManufacturingStore.PlanHash(captured), ManufacturingStore.PlanHash(
            Plan(new CutToLengthLine(lineA, 40, 6000.001m, 3), new CutToLengthLine(lineB, 7, 2500.5m, 2))));
        Assert.NotEqual(ManufacturingStore.PlanHash(captured), ManufacturingStore.PlanHash(
            Plan(new CutToLengthLine(lineA, 40, 6000m, 4), new CutToLengthLine(lineB, 7, 2500.5m, 2))));

        const string legacyCanonical = "manufacturing.plan.v1|10000000-0000-0000-0000-000000000001|"
            + "20000000-0000-0000-0000-00000000c001|5|-|30000000-0000-0000-0000-00000000a001:10000;";
        var legacy = new ProductionPlan(planId, Company, WorkCenter, ManufacturingProcess.CutToLength,
            [new PlannedInput(RollA, Kg(10_000m))]);
        var legacyExpected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(legacyCanonical))).ToLowerInvariant();
        Assert.Equal(legacyExpected, ManufacturingStore.PlanHash(legacy));
    }

    [Fact]
    public void A_captured_technical_snapshot_is_versioned_plan_content_while_legacy_hash_bytes_stay_exact()
    {
        var planId = Guid.NewGuid();
        var legacy = new ProductionPlan(planId, Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(RollA, Kg(10_000m), 7)]);
        var captured = new ProductionPlan(planId, Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(RollA, Kg(10_000m), 7, Snapshot(2m, 1500m))]);
        var changedWidth = new ProductionPlan(planId, Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(RollA, Kg(10_000m), 7, Snapshot(2m, 1499m))]);

        const string legacyCanonical = "manufacturing.plan.v1|10000000-0000-0000-0000-000000000001|"
            + "20000000-0000-0000-0000-00000000c001|1|-|30000000-0000-0000-0000-00000000a001:10000@7;";
        var legacyExpected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(legacyCanonical))).ToLowerInvariant();

        Assert.Equal(legacyExpected, ManufacturingStore.PlanHash(legacy));
        Assert.NotEqual(legacyExpected, ManufacturingStore.PlanHash(captured));
        Assert.NotEqual(ManufacturingStore.PlanHash(captured), ManufacturingStore.PlanHash(changedWidth));
    }

    [Fact]
    public void A_commercial_context_is_length_prefixed_plan_content_while_null_keeps_legacy_and_technical_hash_bytes()
    {
        var planId = Guid.NewGuid();
        var technical = Snapshot(2m, 1500m);
        var context = Commercial(" party:v01 ", "account:v02/α");
        ProductionPlan Plan(PlanCommercialContextSnapshotV1? commercial) => new(
            planId, Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(RollA, Kg(10_000m), 7, technical, commercial)]);

        var withoutCommercial = Plan(null);
        var captured = Plan(context);

        Assert.Equal(ManufacturingStore.PlanHash(new ProductionPlan(
            planId, Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(RollA, Kg(10_000m), 7, technical)])),
            ManufacturingStore.PlanHash(withoutCommercial));
        Assert.NotEqual(ManufacturingStore.PlanHash(withoutCommercial), ManufacturingStore.PlanHash(captured));
        Assert.NotEqual(ManufacturingStore.PlanHash(captured),
            ManufacturingStore.PlanHash(Plan(Commercial("party:v01", "account:v02/α"))));
        Assert.NotEqual(ManufacturingStore.PlanHash(captured),
            ManufacturingStore.PlanHash(Plan(new(context.PartyId, Guid.NewGuid(), context.PartyVersion,
                context.AccountVersion))));
        Assert.NotEqual(ManufacturingStore.PlanHash(captured),
            ManufacturingStore.PlanHash(Plan(new(Guid.NewGuid(), context.CompanyPartyAccountId,
                context.PartyVersion, context.AccountVersion))));
        Assert.NotEqual(ManufacturingStore.PlanHash(captured),
            ManufacturingStore.PlanHash(Plan(Commercial(context.PartyVersion, "account:v03/α"))));
    }

    private static PlanCommercialContextSnapshotV1 Commercial(string partyVersion, string accountVersion) => new(
        Guid.Parse("60000000-0000-0000-0000-000000000001"),
        Guid.Parse("60000000-0000-0000-0000-000000000002"),
        partyVersion,
        accountVersion);

    private static PlanTechnicalParameterSnapshotV1 Snapshot(decimal thicknessMm, decimal widthMm) => new(
        Guid.Parse("50000000-0000-0000-0000-000000000001"),
        "2",
        Guid.Parse("50000000-0000-0000-0000-000000000002"),
        "COIL",
        "family-r1",
        Guid.Parse("50000000-0000-0000-0000-000000000003"),
        "3",
        "COIL-SHEET",
        "template-r3",
        Company,
        Guid.Parse("50000000-0000-0000-0000-000000000004"),
        "kg",
        Guid.Parse("50000000-0000-0000-0000-000000000005"),
        "1",
        new DateOnly(2026, 9, 30),
        thicknessMm,
        widthMm);

    [Fact]
    public void A_slitting_spec_is_plan_content_its_lines_order_counts_and_winding_target_all_count()
    {
        var id = Guid.NewGuid();
        ProductionPlan With(SlittingSpec? spec) => new(id, Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(RollA, Kg(28_750m))], target: null, spec);
        var spec = new SlittingSpec([new SlitLine(150m, 4), new SlitLine(240m, 3)], Kg(2000m));
        var hash = ManufacturingStore.PlanHash(With(spec));

        Assert.Equal(hash, ManufacturingStore.PlanHash(With(
            new SlittingSpec([new SlitLine(150.000m, 4), new SlitLine(240.0m, 3)], Kg(2000.000m)))));
        Assert.NotEqual(hash, ManufacturingStore.PlanHash(With(null)));
        Assert.NotEqual(hash, ManufacturingStore.PlanHash(With(new SlittingSpec([new SlitLine(240m, 3), new SlitLine(150m, 4)], Kg(2000m)))));
        Assert.NotEqual(hash, ManufacturingStore.PlanHash(With(new SlittingSpec([new SlitLine(150m, 4), new SlitLine(240m, 2)], Kg(2000m)))));
        Assert.NotEqual(hash, ManufacturingStore.PlanHash(With(new SlittingSpec([new SlitLine(150m, 4), new SlitLine(240m, 3)]))));
        Assert.NotEqual(hash, ManufacturingStore.PlanHash(With(new SlittingSpec([new SlitLine(150m, 4), new SlitLine(240m, 3)], Kg(2001m)))));
    }

    [Fact]
    public void A_plan_with_a_slitting_spec_is_restored_with_it()
    {
        var spec = new SlittingSpec([new SlitLine(150m, 4), new SlitLine(240m, 3)], Kg(2000m));
        var restored = ProductionPlan.Restore(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(RollA, Kg(28_750m))], target: null, approvedByUserId: null, approvedAtUtc: null, version: 1, spec);

        Assert.Equal((PlanStatus.Draft, spec), (restored.Status, restored.Spec));
    }

    private static SlittingSpec SlittingSpec() =>
        new([new SlitLine(150m, 4), new SlitLine(240m, 3)], Kg(2_000m));

    private static CutToLengthSpec CutToLengthSpec() =>
        new([new CutToLengthLine(Guid.Parse("50000000-0000-0000-0000-000000000001"), 40, 6000m, 3)]);
}
