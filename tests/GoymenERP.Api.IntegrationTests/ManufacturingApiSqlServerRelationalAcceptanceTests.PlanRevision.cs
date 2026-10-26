using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// MFG-PLAN-EDIT through the real host on a REAL SQL Server: the Draft edit of a plan and its history, under the plan
/// save's own grant and the plan read's, with the real Organization process read, Settings' kilogram profile and
/// Inventory's exact label revalidation. Synthetic grants and labels in this test's own database only.
/// </summary>
public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_draft_plan_is_edited_reopened_with_its_history_and_the_next_steps_read_the_edit()
    {
        await using var run = await Run.StartAsync();
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, [.. AllWrites, ErpPermissions.ManufacturingPlansRead]);
        using var client = run.ClientFor(user, CompanyA);
        var correlation = client.DefaultRequestHeaders.GetValues("X-Correlation-Id").Single();
        var rollA = await run.StockedLabelAsync("SYN-MFG-EDIT-A");
        var rollB = await run.StockedLabelAsync("SYN-MFG-EDIT-B");
        var created = (await (await client.PostWithKeyAsync($"{Base}/plans", new SavePlanRequestV1(Guid.NewGuid(), workCenter,
            [new PlannedInputRequestV1(rollA.Id, "1000", rollA.Version)], null))).Content.ReadFromJsonAsync<PlanViewV1>())!;

        // The edit: the whole content — two rolls, a target — at the version read, with a reason.
        var edit = new RevisePlanRequestV1(1, workCenter,
            [new PlannedInputRequestV1(rollA.Id, "800.500", rollA.Version), new PlannedInputRequestV1(rollB.Id, "199.5", rollB.Version)],
            new TargetRequestV1("GoodOutputMass", "900"), "  Bobin kalitesi nedeniyle bölündü ");
        var revised = await client.PostWithKeyAsync($"{Base}/plans/{created.PlanId}/revision", edit, "revise-1");
        Assert.Equal(HttpStatusCode.OK, revised.StatusCode);
        Assert.True(revised.Headers.CacheControl is { Private: true, NoStore: true });
        var view = (await revised.Content.ReadFromJsonAsync<PlanViewV1>())!;
        Assert.Equal(("Draft", 2L, workCenter, "Slitting"), (view.Status, view.Version, view.WorkCenterId, view.Process));
        Assert.Equal([(rollA.Id, "800.5"), (rollB.Id, "199.5")], view.Inputs.Select(input => (input.ParentRollId, input.PlannedKg)));
        Assert.Equal(new TargetViewV1("GoodOutputMass", "900"), view.Target);

        // Idempotent replay: the same key and body is the stored answer, with the first correlation.
        var replay = await client.PostWithKeyAsync($"{Base}/plans/{created.PlanId}/revision", edit, "revise-1");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(("true", correlation), (replay.Headers.GetValues("Idempotency-Replayed").Single(),
            replay.Headers.GetValues("Idempotency-Original-Correlation-Id").Single()));

        // Close and reopen: the plan read answers the edit from storage, identical to the command's answer.
        var reopened = (await client.GetFromJsonAsync<PlanViewV1>($"{Base}/plans/{created.PlanId}"))!;
        Assert.Equal(JsonSerializer.Serialize(view), JsonSerializer.Serialize(reopened));

        // The history: revision 0 as created, revision 1 with its reason (trimmed), actor, version and complete content.
        var historyResponse = await client.GetAsync($"{Base}/plans/{created.PlanId}/revisions");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        Assert.True(historyResponse.Headers.CacheControl is { Private: true, NoStore: true });
        var history = (await historyResponse.Content.ReadFromJsonAsync<PlanRevisionListViewV1>())!;
        Assert.Equal(created.PlanId, history.PlanId);
        Assert.Equal([0, 1], history.Revisions.Select(revision => revision.Revision));
        Assert.Equal((1L, (string?)null, (string?)null), (history.Revisions[0].PlanVersion, history.Revisions[0].Reason,
            history.Revisions[0].RevisedBy));
        var createdRevisionInput = Assert.Single(history.Revisions[0].Inputs);
        Assert.Equal((rollA.Id, "1000", rollA.Version),
            (createdRevisionInput.ParentRollId, createdRevisionInput.PlannedKg, createdRevisionInput.ExpectedLabelVersion));
        var createdSnapshot = Assert.IsType<CapturedPlanTechnicalSnapshotViewV1>(createdRevisionInput.TechnicalSnapshot);
        Assert.Equal(Assert.IsType<CapturedPlanTechnicalSnapshotViewV1>(Assert.Single(created.Inputs).TechnicalSnapshot),
            createdSnapshot);
        Assert.Equal((run.ProductFixture.ProductId, run.ProductFixture.ProductVersion, CompanyA,
                run.ProductFixture.UnitId, run.ProductFixture.ProfileId, run.ProductFixture.ProfileVersion, "1", "1000"),
            (createdSnapshot.ProductId, createdSnapshot.ProductVersion, createdSnapshot.UnitKg.CompanyId,
                createdSnapshot.UnitKg.UnitId, createdSnapshot.UnitKg.ProfileId,
                createdSnapshot.UnitKg.ProfileVersion, createdSnapshot.ThicknessMm, createdSnapshot.WidthMm));
        Assert.Null(createdSnapshot.LengthMm);
        Assert.Equal((2L, "Bobin kalitesi nedeniyle bölündü"), (history.Revisions[1].PlanVersion, history.Revisions[1].Reason));
        Assert.Equal([(rollA.Id, "800.5", rollA.Version), (rollB.Id, "199.5", rollB.Version)],
            history.Revisions[1].Inputs.Select(input =>
                (input.ParentRollId, input.PlannedKg, input.ExpectedLabelVersion)));
        var revisedSnapshots = history.Revisions[1].Inputs.Select(input =>
            Assert.IsType<CapturedPlanTechnicalSnapshotViewV1>(input.TechnicalSnapshot)).ToArray();
        Assert.Equal(view.Inputs.Select(input => Assert.IsType<CapturedPlanTechnicalSnapshotViewV1>(input.TechnicalSnapshot)),
            revisedSnapshots);
        Assert.All(revisedSnapshots, snapshot =>
            Assert.Equal((run.ProductFixture.ProductId, run.ProductFixture.ProductVersion, "1", "1000"),
                (snapshot.ProductId, snapshot.ProductVersion, snapshot.ThicknessMm, snapshot.WidthMm)));

        // The plan list reads the edited content.
        var list = (await client.GetFromJsonAsync<PlanListViewV1>($"{Base}/plans?pageSize=100"))!;
        var summary = Assert.Single(list.Plans, plan => plan.PlanId == created.PlanId);
        Assert.Equal((2L, 2, "1000", "900"), (summary.Version, summary.PlannedRollCount, summary.PlannedInputKg, summary.Target!.Kg));

        // A stale edit is 409 with both versions and records nothing.
        var stale = await client.PostWithKeyAsync($"{Base}/plans/{created.PlanId}/revision",
            edit with { Target = null, Reason = "eski sürüm" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal((ManufacturingRuleCodes.VersionConflict, "1", "2"), await Problem(stale));

        // The next step reads the edited plan: the approval names version 2 and the order snapshots the edit.
        var approval = await client.PostWithKeyAsync($"{Base}/plans/{created.PlanId}/approval", new ApprovePlanRequestV1(2));
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        var approved = (await approval.Content.ReadFromJsonAsync<PlanViewV1>())!;
        Assert.Equal(3L, approved.Version);
        // The revision's actor is the same server-resolved user that then approved.
        Assert.Equal(approved.ApprovedByUserId, Guid.Parse(history.Revisions[1].RevisedBy!));
        var orderId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/orders",
            new CreateOrderRequestV1(orderId, created.PlanId))).StatusCode);
        var order = (await client.GetFromJsonAsync<OrderViewV1>($"{Base}/orders/{orderId}"))!;
        Assert.Equal([(rollA.Id, "800.5"), (rollB.Id, "199.5")], order.Rolls.Select(roll => (roll.ParentRollId, roll.PlannedKg)));
        Assert.Equal(new TargetViewV1("GoodOutputMass", "900"), order.Target);

        // Fail closed once a downstream effect exists: the approved plan (with its order) is not edited.
        var afterApproval = await client.PostWithKeyAsync($"{Base}/plans/{created.PlanId}/revision",
            edit with { ExpectedVersion = 3, Reason = "onaydan sonra" });
        Assert.Equal((HttpStatusCode.UnprocessableEntity, ManufacturingRuleCodes.PlanNotDraft),
            (afterApproval.StatusCode, (await Problem(afterApproval)).Code));

        // Storage: the plan row and its inputs are still the plan as created; the edit is one appended revision.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanInputs WHERE PlanId = @p", ("@p", created.PlanId)));
        Assert.Equal(1000m, await run.ScalarAsync<decimal>(
            "SELECT PlannedKg FROM manufacturing.PlanInputs WHERE PlanId = @p", ("@p", created.PlanId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Plans WHERE Id = @p AND TargetKg IS NOT NULL", ("@p", created.PlanId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanRevisions WHERE PlanId = @p", ("@p", created.PlanId)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanRevisionInputs WHERE PlanId = @p AND RevisionNo = 1", ("@p", created.PlanId)));
        // ADR-011: the success and the terminal stale conflict are each their key's answer; the 422 is not recorded.
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Commands WHERE CommandName = N'manufacturing.plan.revise.v1'"));

        // One audit event for the one edit: the plan save's permission, the Web channel, the user's reason, 1 -> 2.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.plan.revised.v1'"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.plan.revised.v1' "
            + "AND EntityId = @p AND PermissionCode = @permission AND SourceChannel = @channel AND Reason = @reason "
            + "AND EntityVersionBefore = 1 AND EntityVersionAfter = 2 AND CorrelationId = @correlation AND CompanyId = @c",
            ("@p", created.PlanId.ToString("D")), ("@permission", ErpPermissions.ManufacturingPlansWrite),
            ("@channel", SourceChannels.Web), ("@reason", "Bobin kalitesi nedeniyle bölündü"),
            ("@correlation", correlation), ("@c", CompanyA)));
    }

    [SqlServerFact]
    public async Task A_plan_edit_is_fenced_by_company_grant_process_and_label_and_a_refusal_stores_nothing()
    {
        await using var run = await Run.StartAsync();
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var planner = await run.GrantAsync(TestUsers.CompanyA, CompanyA, [.. AllWrites, ErpPermissions.ManufacturingPlansRead]);
        var readerOnly = await run.GrantAsync(TestUsers.NoAssignment, CompanyA, ErpPermissions.ManufacturingPlansRead);
        var writerWithoutRead = await run.GrantAsync(TestUsers.Unprovisioned, CompanyA, AllWrites);
        var userB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, [.. AllWrites, ErpPermissions.ManufacturingPlansRead]);
        var roll = await run.StockedLabelAsync("SYN-MFG-EDIT-FENCE");
        using var client = run.ClientFor(planner, CompanyA);
        var plan = (await (await client.PostWithKeyAsync($"{Base}/plans", new SavePlanRequestV1(Guid.NewGuid(), workCenter,
            [new PlannedInputRequestV1(roll.Id, "1000", roll.Version)], null))).Content.ReadFromJsonAsync<PlanViewV1>())!;
        var edit = new RevisePlanRequestV1(1, workCenter, [new PlannedInputRequestV1(roll.Id, "900", roll.Version)], null,
            "Kg azaltıldı");
        var path = $"{Base}/plans/{plan.PlanId}/revision";

        // Grant: the plan read alone does not edit; every write grant without the read does not see the history.
        using (var reader = run.ClientFor(readerOnly, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostWithKeyAsync(path, edit)).StatusCode);
        }

        using (var writer = run.ClientFor(writerWithoutRead, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await writer.GetAsync($"{Base}/plans/{plan.PlanId}/revisions")).StatusCode);
        }

        // Company fence: another company's user with its own grants gets the code-less 404, never a 403 or the plan.
        using (var other = run.ClientFor(userB, CompanyB))
        {
            var foreign = await other.PostWithKeyAsync(path, edit);
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
            Assert.DoesNotContain("\"code\"", await foreign.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{Base}/plans/{plan.PlanId}/revisions")).StatusCode);
        }

        // The process is fixed: a work center of another process is refused by the plan's own rule.
        var coldRolling = await run.WorkCenterOfKindAsync(CompanyA, "ColdRolling");
        var otherProcess = await client.PostWithKeyAsync(path, edit with { WorkCenterId = coldRolling });
        Assert.Equal((HttpStatusCode.UnprocessableEntity, ManufacturingRuleCodes.PlanProcessFixed),
            (otherProcess.StatusCode, (await Problem(otherProcess)).Code));

        // Another company's work center is not this company's.
        var foreignWorkCenter = await client.PostWithKeyAsync(path, edit with { WorkCenterId = await run.WorkCenterOfAsync(CompanyB) });
        Assert.Equal((HttpStatusCode.UnprocessableEntity, ManufacturingStoreCodes.WorkCenterNotFound),
            (foreignWorkCenter.StatusCode, (await Problem(foreignWorkCenter)).Code));

        // A label version that is not Inventory's current one is its owner's 409.
        var staleLabel = await client.PostWithKeyAsync(path, edit with
        {
            Inputs = [new PlannedInputRequestV1(roll.Id, "900", (long.Parse(roll.Version, System.Globalization.CultureInfo.InvariantCulture) + 1)
                .ToString(System.Globalization.CultureInfo.InvariantCulture))],
        });
        Assert.Equal((HttpStatusCode.Conflict, InventoryParentRollCodesV1.VersionConflict),
            (staleLabel.StatusCode, (await Problem(staleLabel)).Code));

        // A change of nothing and a missing reason.
        var nothing = await client.PostWithKeyAsync(path, edit with { Inputs = [new PlannedInputRequestV1(roll.Id, "1000.000", roll.Version)] });
        Assert.Equal((HttpStatusCode.UnprocessableEntity, ManufacturingRuleCodes.PlanRevisionNoChange),
            (nothing.StatusCode, (await Problem(nothing)).Code));
        var noReason = await client.PostWithKeyAsync(path, edit with { Reason = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        using (var document = JsonDocument.Parse(await noReason.Content.ReadAsStringAsync()))
        {
            Assert.Equal("reason", document.RootElement.GetProperty("field").GetString());
        }

        // Without a company context nothing runs.
        using (var noContext = run.Factory.CreateClient())
        {
            noContext.DefaultRequestHeaders.Add("X-Development-User", planner);
            noContext.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.BadRequest, (await noContext.PostWithKeyAsync(path, edit)).StatusCode);
        }

        // Every refusal stored nothing: no revision, no audit event, the plan still at version 1 as created.
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanRevisions"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.plan.revised.v1'"));
        Assert.Equal(1L, await run.ScalarAsync<long>(
            "SELECT Version FROM manufacturing.Plans WHERE Id = @p", ("@p", plan.PlanId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "UPDATE manufacturing.PlanInputs SET TechnicalProductId = NULL, TechnicalProductVersion = NULL, "
            + "TechnicalFamilyId = NULL, TechnicalFamilyCode = NULL, TechnicalFamilySourceRevision = NULL, "
            + "TechnicalTemplateId = NULL, TechnicalTemplateVersion = NULL, TechnicalTemplateCode = NULL, "
            + "TechnicalTemplateSourceRevision = NULL, TechnicalKilogramUnitCompanyId = NULL, "
            + "TechnicalKilogramUnitId = NULL, TechnicalKilogramUnitCode = NULL, TechnicalKilogramProfileId = NULL, "
            + "TechnicalKilogramProfileVersion = NULL, TechnicalEffectiveOn = NULL, TechnicalThicknessMm = NULL, "
            + "TechnicalWidthMm = NULL WHERE PlanId = @p; SELECT @@ROWCOUNT;", ("@p", plan.PlanId)));
        var history = (await client.GetFromJsonAsync<PlanRevisionListViewV1>($"{Base}/plans/{plan.PlanId}/revisions"))!;
        var legacyInput = Assert.Single(Assert.Single(history.Revisions).Inputs);
        Assert.Equal(0, Assert.Single(history.Revisions).Revision);
        Assert.Null(legacyInput.TechnicalSnapshot);

        // And the edit itself goes through for the planner afterwards.
        Assert.Equal(HttpStatusCode.OK, (await client.PostWithKeyAsync(path, edit)).StatusCode);
    }
}
