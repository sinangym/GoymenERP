using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Xunit.Abstractions;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// MFG-PLAN-EDIT without a database or HTTP. The domain: only a Draft is revised, as a whole, at the version its caller
/// read; an approved plan, a plan with a slitting spec (AT-044 open), a move to another process and a change of nothing
/// are refused with nothing applied; a stored plan replays its revisions before its approval, and the order made from a
/// revised plan snapshots the revised content. The service: the request grammar, the stored plan answering before any
/// other owner is asked, the owners' checks (Organization's process, Settings' kilogram, Inventory's exact label
/// version), the fresh fence, ADR-011 replay and the history read. Its own fakes; the shared fixtures are not widened.
/// </summary>
public sealed class ManufacturingPlanRevisionTests(ITestOutputHelper output)
{
    private const string Permission = "manufacturing.plans.write";
    // ProductStockCandidateFixture creates draft v1 and activates it; c161e0e7 publishes that exact accepted v2.
    private const string AcceptedProductFixtureVersion = "2";
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Actor = Guid.Parse("a1000000-0000-0000-0000-00000000000a");
    private static readonly Guid WorkCenter = Guid.Parse("c1000000-0000-0000-0000-00000000000c");
    private static readonly Guid OtherLine = Guid.Parse("c2000000-0000-0000-0000-00000000000c");
    private static readonly Guid RollA = Guid.Parse("b1000000-0000-0000-0000-00000000000b");
    private static readonly Guid RollB = Guid.Parse("b2000000-0000-0000-0000-00000000000b");
    private static readonly Guid Party = Guid.Parse("d1000000-0000-0000-0000-00000000000d");
    private static readonly Guid PartyAccount = Guid.Parse("d2000000-0000-0000-0000-00000000000d");
    private static readonly DateTimeOffset At = new(2026, 9, 27, 9, 0, 0, TimeSpan.Zero);

    // ---- Domain -------------------------------------------------------------------------------------------------

    [Fact]
    public void A_draft_is_revised_as_a_whole_and_its_order_snapshots_the_revised_content()
    {
        var plan = Draft();

        plan.Revise(1, OtherLine, ManufacturingProcess.Slitting,
            [new PlannedInput(RollB, new Kilograms(800m), 4), new PlannedInput(RollA, new Kilograms(150.5m), 3)],
            new ProductionTarget(TargetBasis.GoodOutputMass, new Kilograms(700m)));

        Assert.Equal((2L, 1, PlanStatus.Draft, OtherLine), (plan.Version, plan.RevisionCount, plan.Status, plan.WorkCenterId));
        Assert.Equal([RollB, RollA], plan.Inputs.Select(input => input.ParentRollId));
        Assert.Equal(new ProductionTarget(TargetBasis.GoodOutputMass, new Kilograms(700m)), plan.Target);
        Assert.Equal(ManufacturingProcess.Slitting, plan.Process);

        // The next step reads the edited plan: approval, then an order whose own snapshot is the revised content.
        plan.Approve(plan.Version, Actor, At);
        var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
        Assert.Equal((OtherLine, 3L), (order.WorkCenterId, plan.Version));
        Assert.Equal([(RollB, 800m, (long?)4), (RollA, 150.5m, (long?)3)],
            order.PlannedInputs.Select(input => (input.ParentRollId, input.PlannedMass.Value, input.ExpectedLabelVersion)));
        Assert.Equal(new ProductionTarget(TargetBasis.GoodOutputMass, new Kilograms(700m)), order.Target);
    }

    [Fact]
    public void A_stale_version_is_a_conflict_before_any_rule_and_nothing_changes()
    {
        var plan = Draft();

        var conflict = Assert.Throws<ManufacturingVersionConflictException>(() =>
            plan.Revise(2, OtherLine, ManufacturingProcess.Slitting, Inputs(RollA, 5m), null));

        Assert.Equal((2L, 1L), (conflict.ExpectedVersion, conflict.CurrentVersion));
        AssertUnchanged(plan);
    }

    [Fact]
    public void An_approved_plan_is_never_revised_because_it_has_a_downstream_decision()
    {
        var plan = Draft();
        plan.Approve(1, Actor, At);

        var refused = Assert.Throws<ManufacturingRuleException>(() =>
            plan.Revise(2, WorkCenter, ManufacturingProcess.Slitting, Inputs(RollA, 5m), null));

        Assert.Equal(ManufacturingRuleCodes.PlanNotDraft, refused.Code);
        Assert.Equal((2L, 0, PlanStatus.Approved), (plan.Version, plan.RevisionCount, plan.Status));
        Assert.Equal(1000m, Assert.Single(plan.Inputs).PlannedMass.Value);
    }

    [Fact]
    public void A_plan_with_a_slitting_spec_is_not_revised_while_its_coil_balance_is_open()
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            Inputs(RollA, 1000m), null, new SlittingSpec([new SlitLine(150m, 4)]));

        var refused = Assert.Throws<ManufacturingRuleException>(() =>
            plan.Revise(1, WorkCenter, ManufacturingProcess.Slitting, Inputs(RollA, 900m), null));

        Assert.Equal(ManufacturingRuleCodes.PlanSlittingSpecFixed, refused.Code);
        AssertUnchanged(plan);
    }

    [Fact]
    public void The_process_is_fixed_and_a_revision_of_nothing_is_refused()
    {
        var plan = Draft();

        var otherProcess = Assert.Throws<ManufacturingRuleException>(() =>
            plan.Revise(1, OtherLine, ManufacturingProcess.CutToLength, Inputs(RollA, 900m), null));
        // 1000.000 kg is 1000 kg, and the same label version: the same content, nothing to record.
        var nothing = Assert.Throws<ManufacturingRuleException>(() =>
            plan.Revise(1, WorkCenter, ManufacturingProcess.Slitting,
                [new PlannedInput(RollA, new Kilograms(1000.000m), 3)], null));
        var noRoll = Assert.Throws<ManufacturingRuleException>(() =>
            plan.Revise(1, WorkCenter, ManufacturingProcess.Slitting, [], null));
        var twice = Assert.Throws<ManufacturingRuleException>(() =>
            plan.Revise(1, WorkCenter, ManufacturingProcess.Slitting,
                [.. Inputs(RollA, 1m), .. Inputs(RollA, 2m)], null));

        Assert.Equal(
            [ManufacturingRuleCodes.PlanProcessFixed, ManufacturingRuleCodes.PlanRevisionNoChange,
                ManufacturingRuleCodes.PlanWithoutInputs, ManufacturingRuleCodes.DuplicateRollInPlan],
            [otherProcess.Code, nothing.Code, noRoll.Code, twice.Code]);
        AssertUnchanged(plan);
    }

    [Fact]
    public void A_changed_label_version_alone_is_a_real_change()
    {
        var plan = Draft();

        plan.Revise(1, WorkCenter, ManufacturingProcess.Slitting, [new PlannedInput(RollA, new Kilograms(1000m), 4)], null);

        Assert.Equal((2L, (long?)4), (plan.Version, Assert.Single(plan.Inputs).ExpectedLabelVersion));
    }

    [Fact]
    public void A_stored_plan_replays_its_revisions_before_its_approval()
    {
        var revisions = new[]
        {
            new PlanRevisionContent(OtherLine, Inputs(RollA, 900m), null),
            new PlanRevisionContent(OtherLine, [.. Inputs(RollA, 900m), .. Inputs(RollB, 50m)],
                new ProductionTarget(TargetBasis.InputMass, new Kilograms(950m))),
        };

        var draft = Restore(revisions, approved: false, version: 3);
        var approved = Restore(revisions, approved: true, version: 4);

        Assert.Equal((3L, 2, OtherLine, 2), (draft.Version, draft.RevisionCount, draft.WorkCenterId, draft.Inputs.Count));
        Assert.Equal((4L, PlanStatus.Approved, 950m), (approved.Version, approved.Status, approved.Target!.Mass.Value));
        // The version must be the one the plan's own changes add up to: a revision too few or too many is refused.
        Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid,
            Assert.Throws<ManufacturingRuleException>(() => Restore(revisions, approved: false, version: 2)).Code);
        Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid,
            Assert.Throws<ManufacturingRuleException>(() => Restore(revisions, approved: true, version: 3)).Code);
        // A stored revision the rule would refuse (a change of nothing) is refused, never repaired.
        Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid, Assert.Throws<ManufacturingRuleException>(() =>
            Restore([new PlanRevisionContent(WorkCenter, [new PlannedInput(RollA, new Kilograms(1000m), 3)], null)],
                approved: false, version: 2)).Code);
    }

    // ---- Service -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_revision_checks_every_owner_behind_the_stored_plan_and_answers_the_revised_plan()
    {
        var fixture = new Fixture();
        var plan = fixture.StoreDraft();
        var observedStatus = "not_observed";

        try
        {
            var result = await fixture.Service.RevisePlanAsync(plan.Id, Request(1, OtherLine, "800.500", "7"), "key-1",
                Permission, CancellationToken.None);
            observedStatus = result.Status.ToString().ToLowerInvariant();

            Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
            var view = result.Resource!;
            Assert.Equal((2L, "Draft", OtherLine, "Slitting"), (view.Version, view.Status, view.WorkCenterId, view.Process));
            Assert.Equal("800.5", Assert.Single(view.Inputs).PlannedKg);
            Assert.Equal(new TargetViewV1("GoodOutputMass", "700"), view.Target);
            // Every awaited owner answer is followed by a fresh captured-company/actor fence. The final pair is the
            // verified-content fence followed by the new pre-write fence immediately before the revision is stored.
            Assert.Equal(["authority", "find-plan", "work-center", "authority", "settings", "parent-roll", "authority",
                    "product", "authority", "authority", "authority", "revise"],
                fixture.Calls.Where(call => call != "find-command"));
            Assert.Equal((OtherLine, ManufacturingProcess.Slitting, "Tolerans dışı bobin için yeniden planlandı"),
                (fixture.Store.LastRevision!.Value.WorkCenterId, fixture.Store.LastRevision.Value.Process,
                    fixture.Store.LastRevision.Value.Reason));
            Assert.Equal(new InventoryParentRollSelectionV1(RollA, "7"), Assert.Single(fixture.ParentRolls.Selections));
            // The envelope: the plan save's permission, the Web channel, the key; the record joins the business save.
            var metadata = Assert.Single(fixture.Store.Writes);
            Assert.Equal((Permission, "Web", "key-1"), (metadata.PermissionCode, metadata.SourceChannel, metadata.IdempotencyKey));
            Assert.Single(fixture.Store.Commands);
        }
        catch
        {
            output.WriteLine(
                $"ci_safe_diagnostic:phase=mfg_plan_revision_owner_checks;status_expected=ok;status_actual={observedStatus};calls_expected=12;calls_actual={fixture.Calls.Count(call => call != "find-command")};writes_expected=1;writes_actual={fixture.Store.Writes.Count};commands_expected=1;commands_actual={fixture.Store.Commands.Count}");
            throw;
        }
    }

    [Fact]
    public async Task A_revision_captures_and_publishes_the_exact_authorized_commercial_context()
    {
        var fixture = new Fixture();
        var plan = fixture.StoreDraft();
        fixture.PartyAccounts.Account = new CounterpartyManufacturingPartyAccountV1(
            Party, PartyAccount, Company, true, true, false, true, " party-v1 ", "account-v1/α");
        var request = Request(1, OtherLine, "800.500", "7") with
        {
            Inputs = [new PlannedInputRequestV1(RollA, "800.500", "7",
                new PlanCommercialContextRequestV1(Party, PartyAccount, " party-v1 ", "account-v1/α"))],
        };
        var observedStatus = "not_observed";

        try
        {
            var result = await fixture.Service.RevisePlanAsync(
                plan.Id, request, "commercial-revision", Permission, CancellationToken.None);
            observedStatus = result.Status.ToString().ToLowerInvariant();

            Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
            var stored = Assert.Single(fixture.Store.Plans[plan.Id].Inputs).CommercialContext!;
            Assert.Equal((Party, PartyAccount, " party-v1 ", "account-v1/α"),
                (stored.PartyId, stored.CompanyPartyAccountId, stored.PartyVersion, stored.AccountVersion));
            Assert.Equal(new CapturedPlanCommercialContextViewV1(
                Party, PartyAccount, " party-v1 ", "account-v1/α"),
                Assert.Single(result.Resource!.Inputs).CommercialContext);
            Assert.Empty(fixture.ParentRolls.Selections);
            var selection = Assert.Single(fixture.ParentRolls.ContextualSelections);
            Assert.Equal((StockPartyKind.Counterparty, Party, StockPartyKind.Company, Company),
                (selection.PositionContext.OwnerKind, selection.PositionContext.OwnerId,
                    selection.PositionContext.CustodianKind, selection.PositionContext.CustodianId));
            // The last authority call is the fresh pre-write fence after all owner snapshots have been verified.
            Assert.Equal(["authority", "find-plan", "work-center", "authority", "settings", "party-account", "authority",
                    "parent-roll-context", "authority", "product", "authority", "authority", "authority", "revise"],
                fixture.Calls.Where(call => call != "find-command"));
        }
        catch
        {
            output.WriteLine(
                $"ci_safe_diagnostic:phase=mfg_plan_revision_commercial_context;status_expected=ok;status_actual={observedStatus};calls_expected=14;calls_actual={fixture.Calls.Count(call => call != "find-command")};contextual_selections_expected=1;contextual_selections_actual={fixture.ParentRolls.ContextualSelections.Count};plain_selections_expected=0;plain_selections_actual={fixture.ParentRolls.Selections.Count}");
            throw;
        }
    }

    [Fact]
    public async Task A_revision_captures_a_numerically_exact_trailing_zero_measure_and_refuses_a_real_fourth_decimal()
    {
        var accepted = new Fixture();
        accepted.ProductSnapshots.ThicknessMm = 2.0000m;
        var plan = accepted.StoreDraft();

        var result = await accepted.Service.RevisePlanAsync(plan.Id, Request(1, OtherLine, "800", "7"),
            "key-exact", Permission, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        var captured = Assert.Single(accepted.Store.Plans[plan.Id].Inputs).TechnicalSnapshot!;
        Assert.Equal(2m, captured.ThicknessMm);
        Assert.Equal((byte)4, captured.ThicknessMm.Scale);

        var refused = new Fixture();
        refused.ProductSnapshots.ThicknessMm = 2.0001m;
        var unchanged = refused.StoreDraft();
        var invalid = await refused.Service.RevisePlanAsync(unchanged.Id, Request(1, OtherLine, "800", "7"),
            "key-inexact", Permission, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Unavailable, invalid.Status);
        Assert.Equal(1L, unchanged.Version);
        Assert.Empty(refused.Store.Writes);
    }

    [Fact]
    public async Task The_same_key_replays_the_answer_and_another_request_under_it_is_a_conflict()
    {
        var fixture = new Fixture();
        var plan = fixture.StoreDraft();
        var request = Request(1, WorkCenter, "900", "7");

        var first = await fixture.Service.RevisePlanAsync(plan.Id, request, "key-1", Permission, CancellationToken.None);
        var replay = await fixture.Service.RevisePlanAsync(plan.Id, request with { Reason = "  " + request.Reason + " " },
            "key-1", Permission, CancellationToken.None);
        var other = await fixture.Service.RevisePlanAsync(plan.Id, request with { Reason = "başka gerekçe" }, "key-1",
            Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Ok, false), (first.Status, first.IsReplay));
        // The reason is trimmed before it is hashed, so the same words are the same request.
        Assert.Equal((ManufacturingApiStatus.Ok, true, 2L), (replay.Status, replay.IsReplay, replay.Resource!.Version));
        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingApiCodes.IdempotencyConflict), (other.Status, other.Code));
        Assert.Single(fixture.Store.Writes);
        Assert.Equal(1, fixture.Store.Plans[plan.Id].RevisionCount);
    }

    [Fact]
    public async Task A_stale_version_is_the_keys_terminal_conflict_and_no_other_owner_is_asked()
    {
        var fixture = new Fixture();
        var plan = fixture.StoreDraft();

        var stale = await fixture.Service.RevisePlanAsync(plan.Id, Request(2, WorkCenter, "900", "7"), "key-1",
            Permission, CancellationToken.None);
        var again = await fixture.Service.RevisePlanAsync(plan.Id, Request(2, WorkCenter, "900", "7"), "key-1",
            Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingRuleCodes.VersionConflict, 2L, 1L),
            (stale.Status, stale.Code, stale.ExpectedVersion, stale.CurrentVersion));
        Assert.Equal((ManufacturingApiStatus.Conflict, true), (again.Status, again.IsReplay));
        Assert.DoesNotContain("work-center", fixture.Calls);
        Assert.DoesNotContain("parent-roll", fixture.Calls);
        Assert.Empty(fixture.Store.Writes);
    }

    [Fact]
    public async Task An_approved_plan_is_refused_as_not_a_draft_before_any_owner_is_asked_and_the_refusal_is_not_stored()
    {
        var fixture = new Fixture();
        var plan = fixture.StoreDraft();
        plan.Approve(1, Actor, At);

        var result = await fixture.Service.RevisePlanAsync(plan.Id, Request(2, WorkCenter, "900", "7"), "key-1",
            Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unprocessable, ManufacturingRuleCodes.PlanNotDraft), (result.Status, result.Code));
        Assert.DoesNotContain("work-center", fixture.Calls);
        Assert.DoesNotContain("settings", fixture.Calls);
        Assert.DoesNotContain("parent-roll", fixture.Calls);
        Assert.Empty(fixture.Store.Writes);
        Assert.Empty(fixture.Store.Commands);
    }

    [Fact]
    public async Task A_work_center_of_another_process_is_refused_by_the_plans_own_rule()
    {
        var fixture = new Fixture();
        var plan = fixture.StoreDraft();
        fixture.WorkCenters.Kind = "CutToLength";

        var result = await fixture.Service.RevisePlanAsync(plan.Id, Request(1, OtherLine, "900", "7"), "key-1",
            Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unprocessable, ManufacturingRuleCodes.PlanProcessFixed), (result.Status, result.Code));
        Assert.Equal((1L, WorkCenter), (fixture.Store.Plans[plan.Id].Version, fixture.Store.Plans[plan.Id].WorkCenterId));
    }

    [Fact]
    public async Task A_roll_whose_label_moved_on_is_refused_by_inventory_and_nothing_is_revised()
    {
        var fixture = new Fixture();
        var plan = fixture.StoreDraft();
        fixture.ParentRolls.Exception = new InventoryParentRollException(409, InventoryParentRollCodesV1.VersionConflict);

        var result = await fixture.Service.RevisePlanAsync(plan.Id, Request(1, WorkCenter, "900", "7"), "key-1",
            Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Conflict, InventoryParentRollCodesV1.VersionConflict), (result.Status, result.Code));
        Assert.Empty(fixture.Store.Writes);
        Assert.Equal(1L, fixture.Store.Plans[plan.Id].Version);
    }

    [Fact]
    public async Task An_unknown_plan_is_the_codeless_not_found_and_without_the_revision_port_the_answer_is_retryable()
    {
        var fixture = new Fixture();
        var withoutPort = new Fixture(withRevisions: false);
        var plan = withoutPort.StoreDraft();

        var missing = await fixture.Service.RevisePlanAsync(Guid.NewGuid(), Request(1, WorkCenter, "900", "7"), "key-1",
            Permission, CancellationToken.None);
        var unavailable = await withoutPort.Service.RevisePlanAsync(plan.Id, Request(1, WorkCenter, "900", "7"), "key-1",
            Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.NotFound, (string?)null), (missing.Status, missing.Code));
        Assert.Equal(ManufacturingApiStatus.Unavailable, unavailable.Status);
        Assert.Equal(1L, withoutPort.Store.Plans[plan.Id].Version);
    }

    [Theory]
    [InlineData(null, "reason")]
    [InlineData("   ", "reason")]
    [InlineData("satır\nsonu", "reason")]
    [InlineData("TOO-LONG", "reason")]
    public async Task A_missing_or_unstorable_reason_is_invalid_before_anything_is_read(string? reason, string field)
    {
        var fixture = new Fixture();
        var plan = fixture.StoreDraft();
        var text = reason == "TOO-LONG" ? new string('a', ManufacturingStoreCodes.PlanRevisionReasonMaximumLength + 1) : reason;

        var result = await fixture.Service.RevisePlanAsync(plan.Id, Request(1, WorkCenter, "900", "7") with { Reason = text },
            "key-1", Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, ManufacturingApiCodes.RequestInvalid, field),
            (result.Status, result.Code, result.Field));
        Assert.Empty(fixture.Calls);
    }

    [Fact]
    public async Task The_content_grammar_is_the_plan_saves_and_is_refused_before_anything_is_read()
    {
        var fixture = new Fixture();
        var plan = fixture.StoreDraft();
        var valid = Request(1, WorkCenter, "900", "7");

        var cases = new (RevisePlanRequestV1 Request, string Field)[]
        {
            (valid with { ExpectedVersion = 0 }, "expectedVersion"),
            (valid with { WorkCenterId = Guid.Empty }, "plan"),
            (valid with { Inputs = [] }, "plan"),
            (valid with { Inputs = [new PlannedInputRequestV1(RollA, "9e2", "7")] }, "inputs.plannedKg"),
            (valid with { Inputs = [new PlannedInputRequestV1(RollA, "900", "07")] }, "inputs[0].expectedLabelVersion"),
            (valid with { Target = new TargetRequestV1("goodOutputMass", "1") }, "target.basis"),
            (valid with { Target = new TargetRequestV1("InputMass", "-1") }, "target.kg"),
        };
        foreach (var (request, field) in cases)
        {
            var result = await fixture.Service.RevisePlanAsync(plan.Id, request, "key-1", Permission, CancellationToken.None);
            Assert.Equal((ManufacturingApiStatus.Invalid, field), (result.Status, result.Field));
        }

        Assert.Empty(fixture.Calls);
    }

    [Fact]
    public async Task A_grant_that_is_not_company_wide_writes_nothing()
    {
        var fixture = new Fixture();
        var plan = fixture.StoreDraft();
        fixture.Authority.CompanyWide = false;

        var result = await fixture.Service.RevisePlanAsync(plan.Id, Request(1, WorkCenter, "900", "7"), "key-1",
            Permission, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Denied, result.Status);
        Assert.Empty(fixture.Store.Writes);
    }

    [Fact]
    public async Task A_grant_lost_at_the_revision_final_fence_writes_nothing()
    {
        var fixture = new Fixture();
        var plan = fixture.StoreDraft();
        for (var granted = 0; granted < 4; granted++)
        {
            fixture.Authority.Scopes.Enqueue(fixture.Authority.GrantedScope());
        }
        fixture.Authority.Scopes.Enqueue(PermissionScope.Denied);

        var result = await fixture.Service.RevisePlanAsync(
            plan.Id, Request(1, OtherLine, "900", "7"), "final-fence", Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Denied, ManufacturingApiCodes.AuthorityDenied),
            (result.Status, result.Code));
        Assert.Equal(1L, plan.Version);
        Assert.Empty(fixture.Store.Writes);
        Assert.Empty(fixture.Store.Commands);
    }

    [Fact]
    public async Task The_history_is_the_stores_entries_as_lossless_text_under_the_read_rules()
    {
        var fixture = new Fixture();
        var plan = fixture.StoreDraft();
        fixture.Store.History[plan.Id] =
        [
            new ManufacturingPlanRevision(0, 1, WorkCenter, [new PlannedInput(RollA, new Kilograms(1000.000m), null)], null,
                null, null, At),
            new ManufacturingPlanRevision(1, 2, OtherLine,
                [new PlannedInput(RollA, new Kilograms(800.500m), 7, commercialContext:
                    new PlanCommercialContextSnapshotV1(Party, PartyAccount, " party-v1 ", "account-v1/α"))],
                new ProductionTarget(TargetBasis.InputMass, new Kilograms(810m)), "Hat değişti", Actor.ToString("D"),
                At.AddHours(1)),
        ];
        var primaryStatus = "not_observed";
        var missingStatus = "not_observed";
        var withoutPortStatus = "not_observed";
        var deniedStatus = "not_observed";
        var revisionCount = -1;
        var assertionStage = "before_service_calls";

        try
        {
            var result = await fixture.Service.ListPlanRevisionsAsync(plan.Id, CancellationToken.None);
            primaryStatus = result.Status.ToString().ToLowerInvariant();
            var missing = await fixture.Service.ListPlanRevisionsAsync(Guid.NewGuid(), CancellationToken.None);
            missingStatus = missing.Status.ToString().ToLowerInvariant();
            var withoutPort = await new Fixture(withRevisions: false).Service.ListPlanRevisionsAsync(plan.Id, CancellationToken.None);
            withoutPortStatus = withoutPort.Status.ToString().ToLowerInvariant();
            var noContext = new Fixture();
            noContext.User.Companies.Clear();
            var denied = await noContext.Service.ListPlanRevisionsAsync(plan.Id, CancellationToken.None);
            deniedStatus = denied.Status.ToString().ToLowerInvariant();

            assertionStage = "primary_status";
            Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
            var history = result.Resource!;
            revisionCount = history.Revisions.Count;
            assertionStage = "history_identity";
            Assert.Equal(plan.Id, history.PlanId);
            assertionStage = "revision_indices";
            Assert.Equal([0, 1], history.Revisions.Select(revision => revision.Revision));
            assertionStage = "created_input";
            Assert.Equal(new PlanRevisionInputViewV1(RollA, "1000", null), Assert.Single(history.Revisions[0].Inputs));
            assertionStage = "revised_input";
            Assert.Equal(new PlanRevisionInputViewV1(RollA, "800.5", "7",
                new CapturedPlanCommercialContextViewV1(Party, PartyAccount, " party-v1 ", "account-v1/α")),
                Assert.Single(history.Revisions[1].Inputs));
            assertionStage = "revision_metadata";
            Assert.Equal((2L, OtherLine, "Hat değişti", Actor.ToString("D")),
                (history.Revisions[1].PlanVersion, history.Revisions[1].WorkCenterId, history.Revisions[1].Reason,
                    history.Revisions[1].RevisedBy));
            assertionStage = "revision_target";
            Assert.Equal(new TargetViewV1("InputMass", "810"), history.Revisions[1].Target);
            assertionStage = "read_outcomes";
            Assert.Equal((ManufacturingApiStatus.NotFound, ManufacturingApiStatus.Unavailable, ManufacturingApiStatus.Denied),
                (missing.Status, withoutPort.Status, denied.Status));
            // A read: no authority revalidation, no Settings or Inventory, no command record.
            assertionStage = "read_authority_effect";
            Assert.DoesNotContain("authority", fixture.Calls);
            assertionStage = "read_command_effect";
            Assert.Empty(fixture.Store.Commands);
        }
        catch
        {
            output.WriteLine(
                $"ci_safe_diagnostic:phase=mfg_plan_revision_history_read;assertion_stage={assertionStage};primary_status_expected=ok;primary_status_actual={primaryStatus};missing_status_expected=notfound;missing_status_actual={missingStatus};without_port_status_expected=unavailable;without_port_status_actual={withoutPortStatus};denied_status_expected=denied;denied_status_actual={deniedStatus};revisions_expected=2;revisions_actual={revisionCount};commands_expected=0;commands_actual={fixture.Store.Commands.Count}");
            throw;
        }
    }

    // ---- Helpers -----------------------------------------------------------------------------------------------

    private static ProductionPlan Draft() =>
        new(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(RollA, new Kilograms(1000m), 3)]);

    private static PlannedInput[] Inputs(Guid roll, decimal kg) => [new PlannedInput(roll, new Kilograms(kg), 3)];

    private static void AssertUnchanged(ProductionPlan plan)
    {
        Assert.Equal((1L, 0, WorkCenter, (ProductionTarget?)null), (plan.Version, plan.RevisionCount, plan.WorkCenterId, plan.Target));
        Assert.Equal(1000m, Assert.Single(plan.Inputs).PlannedMass.Value);
    }

    private static ProductionPlan Restore(IEnumerable<PlanRevisionContent> revisions, bool approved, long version) =>
        ProductionPlan.Restore(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(RollA, new Kilograms(1000m), 3)], null, approved ? Actor : null, approved ? At : null,
            version, spec: null, revisions);

    private static RevisePlanRequestV1 Request(long expectedVersion, Guid workCenter, string kg, string labelVersion) =>
        new(expectedVersion, workCenter, [new PlannedInputRequestV1(RollA, kg, labelVersion)],
            new TargetRequestV1("GoodOutputMass", "700"), "Tolerans dışı bobin için yeniden planlandı");

    private sealed class Fixture
    {
        public Fixture(bool withRevisions = true)
        {
            var company = new CompanyContext();
            company.Set(Company);
            var correlation = new CorrelationContext();
            correlation.Set("corr-plan-edit", null, "Web", null, null);
            Store = new FakeStore(Calls);
            Authority = new AuthorityPort(Calls);
            ParentRolls = new ParentRollPort(Calls);
            WorkCenters = new WorkCenterPort(Calls);
            ProductSnapshots = new ProductSnapshotPort(Calls);
            PartyAccounts = new PartyAccountPort(Calls);
            Service = new ManufacturingCommandService(Store, Store, Store, new ManufacturingMassVerifier(new SettingsPort(Calls)),
                Authority, company, User, correlation, new FixedTime(), ParentRolls, WorkCenters,
                planRevisions: withRevisions ? Store : null, productTechnicalSnapshots: ProductSnapshots,
                partyAccounts: PartyAccounts, contextualParentRolls: ParentRolls);
        }

        public List<string> Calls { get; } = [];

        public FakeStore Store { get; }

        public AuthorityPort Authority { get; }

        public ParentRollPort ParentRolls { get; }

        public WorkCenterPort WorkCenters { get; }

        public ProductSnapshotPort ProductSnapshots { get; }

        public PartyAccountPort PartyAccounts { get; }

        public User User { get; } = new();

        public ManufacturingCommandService Service { get; }

        public ProductionPlan StoreDraft()
        {
            var plan = Draft();
            Store.Plans[plan.Id] = plan;
            return plan;
        }
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

        public Queue<PermissionScope> Scopes { get; } = new();

        public PermissionScope GrantedScope() => new(true, true,
            new HashSet<string>(["PRODUCTION_PLANNER"], StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(),
            new HashSet<Guid>());

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            calls.Add("authority");
            return Task.FromResult(Scopes.TryDequeue(out var scope)
                ? scope
                : new PermissionScope(true, CompanyWide,
                    new HashSet<string>(["PRODUCTION_PLANNER"], StringComparer.Ordinal), new HashSet<Guid>(),
                    new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }

    private sealed class ParentRollPort(List<string> calls)
        : IInventoryParentRollValidationV1, IInventoryContextualParentRollReferencesV1
    {
        public Exception? Exception { get; set; }

        public List<InventoryParentRollSelectionV1> Selections { get; } = [];

        public List<InventoryContextualParentRollSelectionV1> ContextualSelections { get; } = [];

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
                DraftSelectable: true, DraftRefusalCode: null, ProductionUseRequiresRevalidation: true)
            { ProductVersion = AcceptedProductFixtureVersion });
        }

        public Task<InventoryParentRollCandidatesV1> SearchAsync(InventoryContextualParentRollQueryV1 query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        Task<InventoryParentRollReferenceV1> IInventoryContextualParentRollReferencesV1.ValidateAsync(
            InventoryContextualParentRollSelectionV1 selection, CancellationToken cancellationToken)
        {
            calls.Add("parent-roll-context");
            ContextualSelections.Add(selection);
            if (Exception is { } exception)
            {
                throw exception;
            }

            return Task.FromResult(new InventoryParentRollReferenceV1(
                Company, selection.LabelId, selection.ExpectedLabelVersion, "LBL-1", Guid.NewGuid(),
                new InventoryLotV1(Guid.NewGuid(), null, null), null, null, "Complete", [], [], "10", "10", "0", [],
                DraftSelectable: true, DraftRefusalCode: null, ProductionUseRequiresRevalidation: true)
            { ProductVersion = AcceptedProductFixtureVersion });
        }
    }

    private sealed class PartyAccountPort(List<string> calls) : ICounterpartyManufacturingPartyAccountSourceV1
    {
        public CounterpartyManufacturingPartyAccountV1? Account { get; set; }

        public Task<CounterpartyManufacturingPartyAccountSearchAnswerV1> SearchAuthorizedAsync(
            CounterpartyManufacturingPartyAccountSearchV1 query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CounterpartyManufacturingPartyAccountAnswerV1> GetAuthorizedAsync(Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken = default)
        {
            calls.Add("party-account");
            return Task.FromResult(new CounterpartyManufacturingPartyAccountAnswerV1(
                CounterpartyManufacturingPartyAccountOutcomeV1.Found,
                Account ?? new CounterpartyManufacturingPartyAccountV1(
                    partyId, companyAccountId, Company, true, true, true, false, "party-v1", "account-v1")));
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

    private sealed class ProductSnapshotPort(List<string> calls) : IProductManufacturingTechnicalSnapshotV1
    {
        public decimal ThicknessMm { get; set; } = 2m;

        public Task<ProductManufacturingTechnicalSnapshotResultV1> ReadAsync(
            ProductManufacturingTechnicalSnapshotSelectionV1 selection, CancellationToken cancellationToken)
        {
            calls.Add("product");
            return Task.FromResult(new ProductManufacturingTechnicalSnapshotResultV1(
                new ProductManufacturingTechnicalSnapshotV1(
                    selection.ProductId,
                    selection.ExpectedProductVersion,
                    new ProductManufacturingFamilyRevisionV1(Guid.Parse("f1000000-0000-0000-0000-000000000001"),
                        "COIL", "family-r1"),
                    new ProductManufacturingTemplateRevisionV1(Guid.Parse("f2000000-0000-0000-0000-000000000002"),
                        "3", "COIL-SHEET", "template-r3"),
                    ThicknessMm,
                    1500m,
                    new ProductManufacturingKilogramUnitV1(Company, SettingsUnitIdentitiesV1.Kilogram, "kg",
                        Guid.Parse("4a790fdc-2a6b-5126-835f-d3a13d28b71c"), "1"),
                    selection.EffectiveOn),
                []));
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

    /// <summary>The store's contract in memory: the domain decides, and the completion's record joins the write.</summary>
    private sealed class FakeStore(List<string> calls)
        : IManufacturingPlanStore, IManufacturingPlanRevisionStore, IManufacturingOrderStore, IManufacturingCommandLog
    {
        public Dictionary<Guid, ProductionPlan> Plans { get; } = [];

        public Dictionary<Guid, IReadOnlyList<ManufacturingPlanRevision>> History { get; } = [];

        public Dictionary<(string Command, string KeyHash), ManufacturingCommandRecord> Commands { get; } = [];

        public List<ManufacturingCommandMetadata> Writes { get; } = [];

        public (Guid WorkCenterId, ManufacturingProcess Process, string Reason)? LastRevision { get; private set; }

        public Task<ProductionPlan> RevisePlanAsync(Guid planId, long expectedVersion, Guid workCenterId,
            ManufacturingProcess workCenterProcess, IReadOnlyList<PlannedInput> inputs, ProductionTarget? target,
            string reason, ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            calls.Add("revise");
            var plan = Plans.GetValueOrDefault(planId)
                ?? throw new ManufacturingStoreException(ManufacturingStoreCodes.PlanNotFound, "yok");
            plan.Revise(expectedVersion, workCenterId, workCenterProcess, inputs, target);
            LastRevision = (workCenterId, workCenterProcess, reason);
            if (metadata.Completion is { } completion)
            {
                var record = completion(plan);
                Commands.Add((record.CommandName, record.IdempotencyKeyHash), record);
            }

            Writes.Add(metadata);
            return Task.FromResult(plan);
        }

        public Task<IReadOnlyList<ManufacturingPlanRevision>?> ListPlanRevisionsAsync(Guid planId,
            CancellationToken cancellationToken = default)
        {
            calls.Add("history");
            return Task.FromResult(History.GetValueOrDefault(planId));
        }

        public Task<ProductionPlan?> FindPlanAsync(Guid planId, CancellationToken cancellationToken = default)
        {
            calls.Add("find-plan");
            return Task.FromResult(Plans.GetValueOrDefault(planId));
        }

        public Task<ManufacturingCommandRecord?> FindCommandAsync(string commandName, string idempotencyKeyHash,
            CancellationToken cancellationToken = default)
        {
            calls.Add("find-command");
            return Task.FromResult(Commands.GetValueOrDefault((commandName, idempotencyKeyHash)));
        }

        public Task RecordCommandAsync(ManufacturingCommandRecord record, CancellationToken cancellationToken = default)
        {
            Commands.Add((record.CommandName, record.IdempotencyKeyHash), record);
            return Task.CompletedTask;
        }

        public Task<ManufacturingWriteOutcome> SavePlanAsync(ProductionPlan plan, ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

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
