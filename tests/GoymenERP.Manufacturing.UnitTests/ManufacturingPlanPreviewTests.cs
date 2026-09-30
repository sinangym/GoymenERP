using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Engines;
using GoymenERP.Modules.Manufacturing.Domain.Engines.ColdRolling;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Slitting;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// AT-061 / AT-062 (DEC-PRDCT-005) without a database or HTTP. The calculator: one deterministic composition of the
/// versioned engines; every outside value unknown today is NAMED, never defaulted (OPEN-007); a supplied value computes
/// exactly what the engine computes. The service: the preview runs the save's own checks in the save's order and the
/// ONE calculator, writes nothing, and answers for a content exactly what the calculator answers for the plan the save
/// stores from the same content.
/// </summary>
public sealed class ManufacturingPlanPreviewTests
{
    private const string Permission = "manufacturing.plans.write";
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Actor = Guid.Parse("a1000000-0000-0000-0000-00000000000a");
    private static readonly Guid WorkCenter = Guid.Parse("c1000000-0000-0000-0000-00000000000c");
    private static readonly Guid RollA = Guid.Parse("b1000000-0000-0000-0000-00000000000b");
    private static readonly Guid RollB = Guid.Parse("b2000000-0000-0000-0000-00000000000b");
    private static readonly DateTimeOffset At = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---- Calculator ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(ManufacturingProcess.Slitting, new[] { "SLT-001…007", "SLT-010" },
        new[] { "densityKgPerCubicMetre", "lineSpeedMetresPerMinute", "lines[0].bandWidthMm", "lines[0].count",
            "netUsableWidthMm", "parentWidthMm", "thicknessMm" })]
    [InlineData(ManufacturingProcess.ColdRolling, new[] { "CR-001…005" },
        new[] { "densityKgPerCubicMetre", "inputThicknessMm", "scrapKg", "targetThicknessMm", "widthMm" })]
    [InlineData(ManufacturingProcess.CutToLength, new[] { "CUT-001…006" },
        new[] { "densityKgPerCubicMetre", "lines[0].count", "lines[0].targetLengthMm", "thicknessMm", "widthMm" })]
    public void Every_roll_formula_names_what_is_unknown_and_nothing_is_defaulted(ManufacturingProcess process,
        string[] formulas, string[] missing)
    {
        var calculation = ProductionPlanCalculatorV1.Calculate(Plan(process));

        Assert.Equal(ProductionPlanCalculatorV1.Version, calculation.CalculatorVersion);
        Assert.Equal((2, 1250.5m), (calculation.PlannedRollCount, calculation.PlannedInputMass.Value));
        Assert.Empty(calculation.PlanAnswers);
        Assert.All(calculation.Rolls, roll =>
        {
            Assert.Equal(formulas, roll.Answers.Select(answer => answer.Formula));
            Assert.All(roll.Answers, answer =>
            {
                Assert.Equal(EngineOutcome.Unavailable, answer.Outcome);
                Assert.Empty(answer.Values);
                Assert.NotEmpty(answer.MissingParameters);
            });
        });
        Assert.Equal(missing, calculation.MissingParameters);
        Assert.False(calculation.HasRefusal);
        // The planned mass is the engine's own input mass: it is never named missing.
        Assert.DoesNotContain(calculation.MissingParameters, name => name is "parentKg" or "inputKg");
    }

    [Theory]
    [InlineData(ManufacturingProcess.TubeProfile, new[] { "TUB-001…003", "TUB-004" },
        new[] { "lineSpeedMetresPerMinute", "packageCount", "pieceLengthMm", "piecesPerPackage", "totalPieces", "unitKg" })]
    [InlineData(ManufacturingProcess.Paint, new[] { "PNT-001…004", "PNT-005", "PNT-006" },
        new[] { "areaSquareMetres", "filmMicrons", "lineSpeedMetresPerMinute", "packageCount", "perimeterMm", "pieces",
            "piecesPerPackage", "profileLengthMm", "scrapFactor", "simultaneousProfiles", "solidsRatio" })]
    public void Plan_level_formulas_name_their_inputs_and_take_no_roll_mass(ManufacturingProcess process,
        string[] formulas, string[] missing)
    {
        var calculation = ProductionPlanCalculatorV1.Calculate(Plan(process));

        Assert.Equal(formulas, calculation.PlanAnswers.Select(answer => answer.Formula));
        Assert.All(calculation.PlanAnswers, answer => Assert.Equal(EngineOutcome.Unavailable, answer.Outcome));
        Assert.All(calculation.Rolls, roll => Assert.Empty(roll.Answers));
        Assert.Equal(missing, calculation.MissingParameters);
    }

    [Fact]
    public void The_target_is_compared_only_in_its_own_basis_and_a_good_output_target_is_not_estimated()
    {
        var input = ProductionPlanCalculatorV1.Calculate(Plan(ManufacturingProcess.ColdRolling,
            new ProductionTarget(TargetBasis.InputMass, new Kilograms(1200m))));
        var over = ProductionPlanCalculatorV1.Calculate(Plan(ManufacturingProcess.ColdRolling,
            new ProductionTarget(TargetBasis.InputMass, new Kilograms(1300.75m))));
        var good = ProductionPlanCalculatorV1.Calculate(Plan(ManufacturingProcess.ColdRolling,
            new ProductionTarget(TargetBasis.GoodOutputMass, new Kilograms(1000m))));

        // Signed: target − planned. More planned than the target is reported negative, never clamped.
        Assert.Equal((EngineOutcome.Computed, -50.5m), (input.Target!.Outcome, input.Target.TargetMinusPlannedInputKg));
        Assert.Equal(50.25m, over.Target!.TargetMinusPlannedInputKg);
        Assert.Equal((EngineOutcome.Unavailable, (decimal?)null), (good.Target!.Outcome, good.Target.TargetMinusPlannedInputKg));
        Assert.Equal([ProductionPlanCalculatorV1.TheoreticalGoodOutputParameter], good.Target.MissingParameters);
        Assert.Contains(ProductionPlanCalculatorV1.TheoreticalGoodOutputParameter, good.MissingParameters);
        Assert.Null(ProductionPlanCalculatorV1.Calculate(Plan(ManufacturingProcess.ColdRolling)).Target);
    }

    [Fact]
    public void A_supplied_parameter_computes_exactly_what_the_engine_computes()
    {
        // Test inputs, not production parameters (the master's §28.8 example; density 7850 kg/m³ is its implied input).
        var parameters = new PlanCalculationParameters(Plan: new Dictionary<string, decimal>
        {
            ["widthMm"] = 1500m,
            ["inputThicknessMm"] = 2m,
            ["targetThicknessMm"] = 1.5m,
            ["scrapKg"] = 0m,
            ["densityKgPerCubicMetre"] = 7850m,
        });
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.ColdRolling,
            [new PlannedInput(RollA, new Kilograms(28750m), 3)]);

        var calculation = ProductionPlanCalculatorV1.Calculate(plan, parameters);

        var answer = Assert.Single(Assert.Single(calculation.Rolls).Answers);
        var engine = ColdRollingEngineV1.Roll(new ColdRollingInputV1(28750m, 1500m, 2m, 1.5m, 0m, 7850m)).Value!;
        Assert.Equal(EngineOutcome.Computed, answer.Outcome);
        Assert.Equal(
            [("inputMetres", engine.InputMetres), ("outputKg", engine.OutputKg), ("elongationRatio", engine.ElongationRatio),
                ("outputMetres", engine.OutputMetres), ("reductionPercent", engine.ReductionPercent)],
            answer.Values.Select(value => (value.Name, value.Value)));
        Assert.Equal(1220.807m, decimal.Round(answer.Values[0].Value, 3));
        Assert.Equal(25m, answer.Values[4].Value);
        Assert.Empty(calculation.MissingParameters);
    }

    [Fact]
    public void A_roll_value_wins_over_the_plan_value_and_a_spec_feeds_the_slitting_lines()
    {
        var parameters = new PlanCalculationParameters(
            Rolls: new Dictionary<Guid, IReadOnlyDictionary<string, decimal>>
            {
                [RollA] = new Dictionary<string, decimal> { ["netUsableWidthMm"] = 1500m },
            },
            Plan: new Dictionary<string, decimal>
            {
                ["netUsableWidthMm"] = 1000m,
                ["thicknessMm"] = 2m,
                ["parentWidthMm"] = 1500m,
                ["densityKgPerCubicMetre"] = 7850m,
                ["lineSpeedMetresPerMinute"] = 100m,
            });
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(RollA, new Kilograms(28750m), 3)], null, new SlittingSpec([new SlitLine(150m, 4)]));

        var roll = Assert.Single(ProductionPlanCalculatorV1.Calculate(plan, parameters).Rolls);

        var slitting = SlittingEngineV1.Plan(new SlittingPlanInputV1(28750m, 1500m, [new SlitLineInputV1(150m, 4)])).Value!;
        Assert.All(roll.Answers, answer => Assert.Equal(EngineOutcome.Computed, answer.Outcome));
        Assert.Equal(slitting.PlanKg, roll.Answers[0].Values.Single(value => value.Name == "planKg").Value);
        Assert.Equal(11500m, roll.Answers[0].Values.Single(value => value.Name == "planKg").Value);
        var lineTime = SlittingEngineV1.LineTime(new LineTimeInputV1(28750m, 2m, 1500m, 7850m, 100m)).Value!;
        Assert.Equal(lineTime.Minutes, roll.Answers[1].Values.Single(value => value.Name == "minutes").Value);
    }

    [Fact]
    public void A_supplied_wrong_value_is_a_refusal_not_a_number()
    {
        var parameters = new PlanCalculationParameters(Plan: new Dictionary<string, decimal>
        {
            ["widthMm"] = 1500m, ["inputThicknessMm"] = 1.5m, ["targetThicknessMm"] = 2m, ["scrapKg"] = 0m,
            ["densityKgPerCubicMetre"] = 7850m,
        });

        var calculation = ProductionPlanCalculatorV1.Calculate(Plan(ManufacturingProcess.ColdRolling), parameters);

        Assert.True(calculation.HasRefusal);
        Assert.All(calculation.Rolls, roll =>
        {
            var answer = Assert.Single(roll.Answers);
            Assert.Equal((EngineOutcome.Refused, EngineCodes.TargetThicknessNotBelowInput, "targetThicknessMm"),
                (answer.Outcome, answer.RefusalCode, answer.RefusedParameter));
            Assert.Empty(answer.Values);
        });
    }

    [Fact]
    public void The_same_content_always_answers_the_same()
    {
        foreach (var process in Enum.GetValues<ManufacturingProcess>())
        {
            var target = new ProductionTarget(TargetBasis.InputMass, new Kilograms(900m));
            var first = ManufacturingCommandService.PlanPreviewView(WorkCenter,
                ProductionPlanCalculatorV1.Calculate(Plan(process, target)));
            var second = ManufacturingCommandService.PlanPreviewView(WorkCenter,
                ProductionPlanCalculatorV1.Calculate(Plan(process, target)));
            Assert.Equal(JsonSerializer.Serialize(first, Json), JsonSerializer.Serialize(second, Json));
        }
    }

    // ---- Service ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Slitting")]
    [InlineData("ColdRolling")]
    [InlineData("TubeProfile")]
    [InlineData("Paint")]
    [InlineData("CutToLength")]
    public async Task The_preview_equals_the_calculation_of_the_plan_the_save_stores_from_the_same_content(string kind)
    {
        var fixture = new Fixture();
        fixture.WorkCenters.Kind = kind;
        var inputs = new[]
        {
            new PlannedInputRequestV1(RollA, "1000.250", "7"),
            new PlannedInputRequestV1(RollB, "250", "12"),
        };
        var target = new TargetRequestV1("InputMass", "1300");

        var preview = await fixture.Service.PreviewPlanAsync(new PlanPreviewRequestV1(WorkCenter, inputs, target),
            Permission, CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Ok, preview.Status);
        // A preview writes nothing: no store write, no command record, no key lookup.
        Assert.Empty(fixture.Store.Writes);
        Assert.Empty(fixture.Store.Commands);
        Assert.DoesNotContain("find-command", fixture.Calls);

        var saved = await fixture.Service.SavePlanAsync(new SavePlanRequestV1(Guid.NewGuid(), WorkCenter, inputs, target),
            "key-1", Permission, CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Created, saved.Status);
        var stored = fixture.Store.Plans[saved.Resource!.PlanId];

        var ofStored = ManufacturingCommandService.PlanPreviewView(stored.WorkCenterId,
            ProductionPlanCalculatorV1.Calculate(stored, PlanCalculationParameters.Unknown));
        Assert.Equal(JsonSerializer.Serialize(ofStored, Json), JsonSerializer.Serialize(preview.Resource, Json));

        var view = preview.Resource!;
        Assert.Equal((kind, 2, "1250.25"), (view.Process, view.PlannedRollCount, view.PlannedInputKg));
        Assert.Equal(new[] { "1000.25", "250" }, view.Rolls.Select(roll => roll.PlannedKg));
        Assert.Equal(("InputMass", "1300", "Computed", "49.75"),
            (view.Target!.Basis, view.Target.Kg, view.Target.Outcome, view.Target.TargetMinusPlannedInputKg));
        Assert.NotEmpty(view.MissingParameters);
    }

    [Fact]
    public async Task The_preview_runs_the_save_checks_in_the_save_order()
    {
        var fixture = new Fixture();

        var preview = await fixture.Service.PreviewPlanAsync(Request("10"), Permission, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, preview.Status);
        Assert.Equal(["authority", "work-center", "settings", "parent-roll", "authority"], fixture.Calls);
        Assert.Equal(new InventoryParentRollSelectionV1(RollA, "7"), Assert.Single(fixture.ParentRolls.Selections));
    }

    [Fact]
    public async Task A_refusal_is_the_save_answer_for_the_same_content_and_names_its_field()
    {
        var fixture = new Fixture();

        var scale = await fixture.Service.PreviewPlanAsync(Request("10.0001"), Permission, CancellationToken.None);
        var grammar = await fixture.Service.PreviewPlanAsync(Request("1,5"), Permission, CancellationToken.None);
        var twice = await fixture.Service.PreviewPlanAsync(new PlanPreviewRequestV1(WorkCenter,
            [new PlannedInputRequestV1(RollA, "10", "7"), new PlannedInputRequestV1(RollA, "20", "7")], null),
            Permission, CancellationToken.None);
        var noRoll = await fixture.Service.PreviewPlanAsync(new PlanPreviewRequestV1(WorkCenter, [], null),
            Permission, CancellationToken.None);
        var badTarget = await fixture.Service.PreviewPlanAsync(Request("10") with { Target = new TargetRequestV1("Kg", "1") },
            Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unprocessable, ManufacturingMassCodes.ScaleExceeded, "inputs.plannedKg"),
            (scale.Status, scale.Code, scale.Field));
        Assert.Equal((ManufacturingApiStatus.Invalid, "inputs.plannedKg"), (grammar.Status, grammar.Field));
        Assert.Equal((ManufacturingApiStatus.Unprocessable, ManufacturingRuleCodes.DuplicateRollInPlan), (twice.Status, twice.Code));
        Assert.Equal((ManufacturingApiStatus.Invalid, "plan"), (noRoll.Status, noRoll.Field));
        Assert.Equal((ManufacturingApiStatus.Invalid, "target.basis"), (badTarget.Status, badTarget.Field));

        var saveScale = await fixture.Service.SavePlanAsync(
            new SavePlanRequestV1(Guid.NewGuid(), WorkCenter, [new PlannedInputRequestV1(RollA, "10.0001", "7")], null),
            "key-2", Permission, CancellationToken.None);
        Assert.Equal((scale.Status, scale.Code, scale.Field), (saveScale.Status, saveScale.Code, saveScale.Field));
        Assert.Empty(fixture.Store.Writes);
    }

    [Fact]
    public async Task A_moved_label_and_a_missing_owner_are_the_save_answers()
    {
        var fixture = new Fixture();
        fixture.ParentRolls.Exception = new InventoryParentRollException(409, InventoryParentRollCodesV1.VersionConflict);

        var moved = await fixture.Service.PreviewPlanAsync(Request("10"), Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Conflict, InventoryParentRollCodesV1.VersionConflict), (moved.Status, moved.Code));

        var withoutOrganization = new Fixture(withWorkCenters: false);
        var unavailable = await withoutOrganization.Service.PreviewPlanAsync(Request("10"), Permission, CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Unavailable, unavailable.Status);
    }

    [Theory]
    [InlineData(409, InventoryParentRollCodesV1.ReadConflict, ManufacturingApiStatus.Conflict,
        InventoryParentRollCodesV1.ReadConflict)]
    [InlineData(404, InventoryParentRollCodesV1.InvalidReference, ManufacturingApiStatus.NotFound, null)]
    [InlineData(422, InventoryParentRollCodesV1.ContextRequired, ManufacturingApiStatus.Unprocessable,
        InventoryParentRollCodesV1.ContextRequired)]
    [InlineData(422, InventoryParentRollCodesV1.NoPhysicalPosition, ManufacturingApiStatus.Unprocessable,
        InventoryParentRollCodesV1.NoPhysicalPosition)]
    public async Task Parent_roll_read_refusals_keep_the_save_boundary_without_writing(
        int ownerStatus, string ownerCode, ManufacturingApiStatus expectedStatus, string? expectedCode)
    {
        var fixture = new Fixture();
        fixture.ParentRolls.Exception = new InventoryParentRollException(ownerStatus, ownerCode);

        var preview = await fixture.Service.PreviewPlanAsync(Request("10"), Permission, CancellationToken.None);

        Assert.Equal((expectedStatus, expectedCode, (string?)null), (preview.Status, preview.Code, preview.Field));
        Assert.Equal(["authority", "work-center", "settings", "parent-roll"], fixture.Calls);
        Assert.Empty(fixture.Store.Writes);
        Assert.Empty(fixture.Store.Commands);
        Assert.DoesNotContain("find-command", fixture.Calls);
    }

    [Fact]
    public async Task Without_a_fresh_company_wide_grant_nothing_is_asked()
    {
        var siteScoped = new Fixture();
        siteScoped.Authority.CompanyWide = false;
        var otherCompany = new Fixture();
        otherCompany.User.Companies.Clear();

        var denied = await siteScoped.Service.PreviewPlanAsync(Request("10"), Permission, CancellationToken.None);
        var fenced = await otherCompany.Service.PreviewPlanAsync(Request("10"), Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Denied, ManufacturingApiCodes.AuthorityDenied), (denied.Status, denied.Code));
        Assert.Equal(["authority"], siteScoped.Calls);
        Assert.Equal(ManufacturingApiStatus.Denied, fenced.Status);
        Assert.Empty(otherCompany.Calls);
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------

    private static ProductionPlan Plan(ManufacturingProcess process, ProductionTarget? target = null) =>
        new(Guid.NewGuid(), Company, WorkCenter, process,
            [new PlannedInput(RollA, new Kilograms(1000.5m), 7), new PlannedInput(RollB, new Kilograms(250m), 12)], target);

    private static PlanPreviewRequestV1 Request(string kg) =>
        new(WorkCenter, [new PlannedInputRequestV1(RollA, kg, "7")], null);

    private sealed class Fixture
    {
        public Fixture(bool withWorkCenters = true)
        {
            var company = new CompanyContext();
            company.Set(Company);
            var correlation = new CorrelationContext();
            correlation.Set("corr-plan-preview", null, "Web", null, null);
            Store = new FakeStore();
            Authority = new AuthorityPort(Calls);
            ParentRolls = new ParentRollPort(Calls);
            WorkCenters = new WorkCenterPort(Calls);
            Service = new ManufacturingCommandService(Store, Store, Store, new ManufacturingMassVerifier(new SettingsPort(Calls)),
                Authority, company, User, correlation, new FixedTime(), ParentRolls, withWorkCenters ? WorkCenters : null);
            Store.Calls = Calls;
        }

        public List<string> Calls { get; } = [];

        public FakeStore Store { get; }

        public AuthorityPort Authority { get; }

        public ParentRollPort ParentRolls { get; }

        public WorkCenterPort WorkCenters { get; }

        public User User { get; } = new();

        public ManufacturingCommandService Service { get; }
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => At;
    }

    private sealed class User : ICurrentUser
    {
        public HashSet<Guid> Companies { get; } = [Company];

        public bool IsAuthenticated => true;

        public string? UserId => Actor.ToString("D");

        public IReadOnlySet<Guid> AllowedCompanyIds => Companies;

        public bool CanAccessCompany(Guid companyId) => Companies.Contains(companyId);
    }

    private sealed class AuthorityPort(List<string> calls) : IErpAuthorityRevalidationService
    {
        public bool CompanyWide { get; set; } = true;

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            calls.Add("authority");
            return Task.FromResult(new PermissionScope(true, CompanyWide,
                new HashSet<string>(["PRODUCTION_PLANNER"], StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(),
                new HashSet<Guid>()));
        }
    }

    private sealed class ParentRollPort(List<string> calls) : IInventoryParentRollValidationV1
    {
        public Exception? Exception { get; set; }

        public List<InventoryParentRollSelectionV1> Selections { get; } = [];

        public Task<InventoryParentRollReferenceV1> ValidateAsync(InventoryParentRollSelectionV1 selection,
            CancellationToken cancellationToken = default)
        {
            calls.Add("parent-roll");
            Selections.Add(selection);
            if (Exception is { } exception)
            {
                throw exception;
            }

            return Task.FromResult(new InventoryParentRollReferenceV1(
                Company, selection.LabelId, selection.ExpectedLabelVersion, "LBL-1", Guid.NewGuid(),
                new InventoryLotV1(Guid.NewGuid(), null, null), null, null, "Complete", [], [], "10", "10", "0", [],
                DraftSelectable: true, DraftRefusalCode: null, ProductionUseRequiresRevalidation: true));
        }
    }

    private sealed class WorkCenterPort(List<string> calls) : IOrganizationWorkCenterPlanningReferenceV1
    {
        public string Kind { get; set; } = "Slitting";

        public Task<OrganizationWorkCenterPlanningReferenceV1?> ReadAsync(Guid companyId, Guid workCenterId,
            CancellationToken cancellationToken = default)
        {
            calls.Add("work-center");
            return Task.FromResult<OrganizationWorkCenterPlanningReferenceV1?>(
                new OrganizationWorkCenterPlanningReferenceV1(workCenterId, Kind));
        }
    }

    private sealed class SettingsPort(List<string> calls) : ISettingsPrerequisiteProvider
    {
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default)
        {
            if (!calls.Contains("settings"))
            {
                calls.Add("settings");
            }

            return Task.FromResult(new UnitProfileV1(Company, SettingsUnitIdentitiesV1.Kilogram, "kg", "mass", true,
                new ProfileVersionV1(Guid.Parse("4a790fdc-2a6b-5126-835f-d3a13d28b71c"), 1), 3, true,
                new DateOnly(2026, 9, 18), null));
        }

        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>The store's contract in memory: a save stores the domain's plan and joins its command record.</summary>
    private sealed class FakeStore : IManufacturingPlanStore, IManufacturingOrderStore, IManufacturingCommandLog
    {
        public List<string> Calls { get; set; } = [];

        public Dictionary<Guid, ProductionPlan> Plans { get; } = [];

        public Dictionary<(string Command, string KeyHash), ManufacturingCommandRecord> Commands { get; } = [];

        public List<ManufacturingCommandMetadata> Writes { get; } = [];

        public Task<ManufacturingWriteOutcome> SavePlanAsync(ProductionPlan plan, ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("save");
            if (metadata.Completion is { } completion)
            {
                var record = completion(plan);
                Commands.Add((record.CommandName, record.IdempotencyKeyHash), record);
            }

            Writes.Add(metadata);
            Plans[plan.Id] = plan;
            return Task.FromResult(ManufacturingWriteOutcome.Recorded);
        }

        public Task<ProductionPlan?> FindPlanAsync(Guid planId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Plans.GetValueOrDefault(planId));

        public Task<ManufacturingCommandRecord?> FindCommandAsync(string commandName, string idempotencyKeyHash,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("find-command");
            return Task.FromResult(Commands.GetValueOrDefault((commandName, idempotencyKeyHash)));
        }

        public Task RecordCommandAsync(ManufacturingCommandRecord record, CancellationToken cancellationToken = default)
        {
            Commands.Add((record.CommandName, record.IdempotencyKeyHash), record);
            return Task.CompletedTask;
        }

        public Task<ProductionPlan> ApprovePlanAsync(Guid planId, long expectedVersion, Guid approvedByUserId,
            DateTimeOffset approvedAt, ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ManufacturingWriteOutcome> CreateOrderAsync(Guid orderId, Guid planId, ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ManufacturingWriteOutcome> IssueInputAsync(Guid orderId, long expectedVersion, Guid issueId,
            Guid parentRollId, Kilograms mass, DateTimeOffset issuedAt, ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ManufacturingWriteOutcome> ReturnUnusedInputAsync(Guid orderId, long expectedVersion, Guid returnId,
            Guid parentRollId, Kilograms mass, DateTimeOffset returnedAt, ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ManufacturingWriteOutcome> DeclareOutputAsync(Guid orderId, long expectedVersion, Guid outputId,
            Guid parentRollId, OutputClass outputClass, Kilograms mass, DateTimeOffset declaredAt,
            ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ManufacturingWriteOutcome> RecordWorkInProgressAsync(Guid orderId, long expectedVersion, Guid measurementId,
            Guid parentRollId, Kilograms mass, DateTimeOffset measuredAt, ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task CloseOrderAsync(Guid orderId, long expectedVersion, Guid closedByUserId, DateTimeOffset closedAt,
            ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProductionOrder?> FindOrderAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ManufacturingOrderPage> ListOrdersAsync(int pageSize, string? cursor, OrderStatus? status,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
