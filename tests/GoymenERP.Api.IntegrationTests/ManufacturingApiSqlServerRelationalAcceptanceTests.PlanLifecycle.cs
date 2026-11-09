using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Üretim U2-a against REAL SQL Server through the REAL host (URT-D1, URT-D2, URT-D9; plan v1 §5.2 API-10/11/15):
/// the rejection keeps a Draft with the reason recorded and replays under its key; the plan-entered Tube / Profile line
/// speed is saved, revised and kept; one approved plan opens exactly one order; the cancellation needs its own grant,
/// keeps the plan, refuses a plan with an order and leaves the active list; the slitting approval reads the plan's net
/// usable width and line speed and each roll's current Inventory label; another company sees none of it.
/// </summary>
public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    private static readonly string[] LifecycleGrants =
        [.. AllWrites, ErpPermissions.ManufacturingPlansRead, ErpPermissions.ManufacturingPlansCancel];

    [SqlServerFact]
    public async Task Plan_rejection_line_speed_one_order_and_cancellation_hold_on_real_sql()
    {
        await using var run = await Run.StartAsync(useSqlSettings: true, clockAt: CatalogDay);
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, LifecycleGrants);
        using var client = run.ClientFor(user, CompanyA);
        var tube = await run.WorkCenterOfKindAsync(CompanyA, "TubeProfile");
        var paint = await run.WorkCenterOfKindAsync(CompanyA, "Paint");
        var roll = await run.StockedLabelAsync($"SYN-MFG-U2A-{Guid.NewGuid():N}");
        var inputs = new[] { new PlannedInputRequestV1(roll.Id, "1000", roll.Version) };

        // URT-D9: a line speed belongs on a Slitting or Tube / Profile plan only, as lossless positive text.
        using (var painted = await client.PostWithKeyAsync($"{Base}/plans",
                   new SavePlanRequestV1(Guid.NewGuid(), paint, inputs, null, LineSpeedMetresPerMinute: "20")))
        {
            Assert.Equal((HttpStatusCode.BadRequest, "lineSpeedMetresPerMinute"),
                (painted.StatusCode, await FieldAsync(painted)));
        }

        using (var malformed = await client.PostWithKeyAsync($"{Base}/plans",
                   new SavePlanRequestV1(Guid.NewGuid(), tube, inputs, null, LineSpeedMetresPerMinute: "-3")))
        {
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        }

        var planId = Guid.NewGuid();
        var created = await PlanAsync(client, HttpStatusCode.Created, $"{Base}/plans",
            new SavePlanRequestV1(planId, tube, inputs, null, LineSpeedMetresPerMinute: "30.500"));
        Assert.Equal(("Draft", 1L, "30.5"), (created.Status, created.Version, created.LineSpeedMetresPerMinute));
        Assert.Equal(30.5m, await run.ScalarAsync<decimal>(
            "SELECT LineSpeedMetresPerMinute FROM manufacturing.Plans WHERE CompanyId = @c AND Id = @p",
            ("@c", CompanyA), ("@p", planId)));

        // URT-D2: the approver's rejection keeps the Draft with its reason; the same key replays it.
        using (var noReason = await (await run.ApproverAsync(CompanyA)).PostWithKeyAsync($"{Base}/plans/{planId}/rejection",
                   new RejectPlanRequestV1(1, "  ")))
        {
            Assert.Equal((HttpStatusCode.BadRequest, "reason"), (noReason.StatusCode, await FieldAsync(noReason)));
        }

        var rejected = await PlanAsync(await run.ApproverAsync(CompanyA), HttpStatusCode.OK, $"{Base}/plans/{planId}/rejection",
            new RejectPlanRequestV1(1, " Hat hızı yüksek "), "u2a-reject");
        Assert.Equal(("Draft", 2L), (rejected.Status, rejected.Version));
        var rejection = Assert.Single(rejected.Rejections!);
        Assert.Equal((1, "Hat hızı yüksek", 2L), (rejection.Number, rejection.Reason, rejection.PlanVersion));
        using (var replay = await (await run.ApproverAsync(CompanyA)).PostWithKeyAsync($"{Base}/plans/{planId}/rejection",
                   new RejectPlanRequestV1(1, " Hat hızı yüksek "), "u2a-reject"))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanRejections WHERE CompanyId = @c AND PlanId = @p",
            ("@c", CompanyA), ("@p", planId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.plan.rejected.v1' AND EntityId = @p",
            ("@p", planId.ToString("D"))));

        // The planner revises at the version after the rejection; the line speed is part of the revision.
        var revised = await PlanAsync(client, HttpStatusCode.OK, $"{Base}/plans/{planId}/revision",
            new RevisePlanRequestV1(2, tube, inputs, null, "Hız düşürüldü", LineSpeedMetresPerMinute: "25"));
        Assert.Equal((3L, "25"), (revised.Version, revised.LineSpeedMetresPerMinute));
        var history = (await client.GetFromJsonAsync<PlanRevisionListViewV1>($"{Base}/plans/{planId}/revisions"))!;
        Assert.Equal(["30.5", "25"], history.Revisions.Select(revision => revision.LineSpeedMetresPerMinute));
        var reopened = (await client.GetFromJsonAsync<PlanViewV1>($"{Base}/plans/{planId}"))!;
        Assert.Equal((3L, "25", 1), (reopened.Version, reopened.LineSpeedMetresPerMinute, reopened.Rejections!.Count));

        // URT-D1: one approved plan opens exactly one order.
        var approved = await PlanAsync(await run.ApproverAsync(CompanyA), HttpStatusCode.OK, $"{Base}/plans/{planId}/approval",
            new ApprovePlanRequestV1(3));
        Assert.Equal(("Approved", 4L), (approved.Status, approved.Version));
        using (var rejectApproved = await (await run.ApproverAsync(CompanyA)).PostWithKeyAsync($"{Base}/plans/{planId}/rejection",
                   new RejectPlanRequestV1(4, "geç")))
        {
            Assert.Equal((HttpStatusCode.UnprocessableEntity, ManufacturingRuleCodes.PlanNotDraft),
                (rejectApproved.StatusCode, (await Problem(rejectApproved)).Code));
        }

        var orderId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/orders",
            new CreateOrderRequestV1(orderId, planId), "u2a-order")).StatusCode);
        using (var replayedOrder = await client.PostWithKeyAsync($"{Base}/orders",
                   new CreateOrderRequestV1(orderId, planId), "u2a-order"))
        {
            Assert.Equal(HttpStatusCode.Created, replayedOrder.StatusCode);
        }

        using (var second = await client.PostWithKeyAsync($"{Base}/orders", new CreateOrderRequestV1(Guid.NewGuid(), planId)))
        {
            Assert.Equal((HttpStatusCode.UnprocessableEntity, ManufacturingRuleCodes.PlanOrderExists),
                (second.StatusCode, (await Problem(second)).Code));
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Orders WHERE CompanyId = @c AND PlanId = @p",
            ("@c", CompanyA), ("@p", planId)));
        Assert.Equal(orderId, (await client.GetFromJsonAsync<PlanViewV1>($"{Base}/plans/{planId}"))!.OrderId);
        // The database holds the rule too, whatever the application does.
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => run.ExecuteAsync(
            "INSERT INTO manufacturing.Orders (Id, CompanyId, PlanId, WorkCenterId, Process, Status, Version, "
            + "CreatedAtUtc, UpdatedAtUtc) SELECT NEWID(), CompanyId, PlanId, WorkCenterId, Process, Status, 1, "
            + "CreatedAtUtc, UpdatedAtUtc FROM manufacturing.Orders WHERE Id = @o", ("@o", orderId)));

        // The cancellation is its own grant; a plan with an order is refused.
        var writerWithoutCancel = await run.GrantAsync(TestUsers.NoAssignment, CompanyA,
            [.. AllWrites, ErpPermissions.ManufacturingPlansRead]);
        using (var noGrant = run.ClientFor(writerWithoutCancel, CompanyA))
        using (var forbidden = await noGrant.PostWithKeyAsync($"{Base}/plans/{planId}/cancellation",
                   new CancelPlanRequestV1(4, "iptal")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using (var hasOrder = await client.PostWithKeyAsync($"{Base}/plans/{planId}/cancellation",
                   new CancelPlanRequestV1(4, "iptal")))
        {
            Assert.Equal((HttpStatusCode.UnprocessableEntity, ManufacturingRuleCodes.PlanHasOrder),
                (hasOrder.StatusCode, (await Problem(hasOrder)).Code));
        }

        // A Draft and an approved plan without an order are cancelled and kept; the active list leaves them out.
        // U3-a: the first plan's approval allocated its roll's kilograms, so these plans plan another roll.
        var other = await run.StockedLabelAsync($"SYN-MFG-U2A-B-{Guid.NewGuid():N}");
        inputs = [new PlannedInputRequestV1(other.Id, "1000", other.Version)];
        var draftId = Guid.NewGuid();
        await PlanAsync(client, HttpStatusCode.Created, $"{Base}/plans", new SavePlanRequestV1(draftId, tube, inputs, null));
        var cancelledDraft = await PlanAsync(client, HttpStatusCode.OK, $"{Base}/plans/{draftId}/cancellation",
            new CancelPlanRequestV1(1, "Sipariş iptal edildi"), "u2a-cancel");
        Assert.Equal(("Cancelled", 2L, "Sipariş iptal edildi"),
            (cancelledDraft.Status, cancelledDraft.Version, cancelledDraft.Cancellation!.Reason));
        using (var again = await client.PostWithKeyAsync($"{Base}/plans/{draftId}/cancellation",
                   new CancelPlanRequestV1(2, "yine")))
        {
            Assert.Equal((HttpStatusCode.UnprocessableEntity, ManufacturingRuleCodes.PlanCancelled),
                (again.StatusCode, (await Problem(again)).Code));
        }

        using (var approveCancelled = await (await run.ApproverAsync(CompanyA)).PostWithKeyAsync($"{Base}/plans/{draftId}/approval",
                   new ApprovePlanRequestV1(2)))
        {
            Assert.Equal((HttpStatusCode.UnprocessableEntity, ManufacturingRuleCodes.PlanNotDraft),
                (approveCancelled.StatusCode, (await Problem(approveCancelled)).Code));
        }

        using (var orderCancelled = await client.PostWithKeyAsync($"{Base}/orders",
                   new CreateOrderRequestV1(Guid.NewGuid(), draftId)))
        {
            Assert.Equal((HttpStatusCode.UnprocessableEntity, ManufacturingRuleCodes.PlanNotApproved),
                (orderCancelled.StatusCode, (await Problem(orderCancelled)).Code));
        }

        var approvedId = Guid.NewGuid();
        await PlanAsync(client, HttpStatusCode.Created, $"{Base}/plans", new SavePlanRequestV1(approvedId, tube, inputs, null));
        await PlanAsync(await run.ApproverAsync(CompanyA), HttpStatusCode.OK, $"{Base}/plans/{approvedId}/approval", new ApprovePlanRequestV1(1));
        var cancelledApproved = await PlanAsync(client, HttpStatusCode.OK, $"{Base}/plans/{approvedId}/cancellation",
            new CancelPlanRequestV1(2, "Hat arızası"));
        Assert.Equal(("Cancelled", 3L), (cancelledApproved.Status, cancelledApproved.Version));
        Assert.NotNull(cancelledApproved.ApprovedAtUtc);

        var active = (await client.GetFromJsonAsync<PlanListViewV1>($"{Base}/plans"))!;
        Assert.Contains(active.Plans, plan => plan.PlanId == planId);
        Assert.DoesNotContain(active.Plans, plan => plan.PlanId == draftId || plan.PlanId == approvedId);
        var cancelledList = (await client.GetFromJsonAsync<PlanListViewV1>($"{Base}/plans?status=Cancelled"))!;
        Assert.Contains(cancelledList.Plans, plan => plan.PlanId == draftId);
        Assert.Contains(cancelledList.Plans, plan => plan.PlanId == approvedId);
        Assert.All(cancelledList.Plans, plan => Assert.Equal("Cancelled", plan.Status));
        Assert.All(cancelledList.Plans, plan => Assert.NotNull(plan.CancelledAtUtc));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Plans WHERE CompanyId = @c AND Status = 3 AND CancellationReason IS NOT NULL",
            ("@c", CompanyA)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.plan.cancelled.v1' AND CompanyId = @c",
            ("@c", CompanyA)));

        // Company isolation: B cannot reject or cancel A's plan (code-less 404) and lists none of it.
        var foreignUser = await run.GrantAsync(TestUsers.CompanyB, CompanyB, LifecycleGrants);
        using var foreign = run.ClientFor(foreignUser, CompanyB);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.PostWithKeyAsync($"{Base}/plans/{planId}/rejection",
            new RejectPlanRequestV1(4, "yabancı"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.PostWithKeyAsync($"{Base}/plans/{draftId}/cancellation",
            new CancelPlanRequestV1(2, "yabancı"))).StatusCode);
        Assert.Empty((await foreign.GetFromJsonAsync<PlanListViewV1>($"{Base}/plans?status=Cancelled"))!.Plans);
    }

    [SqlServerFact]
    public async Task Slitting_approval_reads_the_plans_net_width_and_line_speed_and_the_current_label()
    {
        await using var run = await Run.StartAsync(useSqlSettings: true, clockAt: CatalogDay);
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, LifecycleGrants);
        using var client = run.ClientFor(user, CompanyA);
        var slitting = await run.WorkCenterOfKindAsync(CompanyA, "Slitting");
        // The synthetic Product snapshot captures 1 mm × 1000 mm coils; the label holds 1000.125 kg of free stock.
        var roll = await run.StockedLabelAsync($"SYN-MFG-U2A-SLT-{Guid.NewGuid():N}");
        var rollVersion = roll.Version;
        PlannedInputRequestV1[] Inputs(string kg) => [new PlannedInputRequestV1(roll.Id, kg, rollVersion)];
        SlittingSpecRequestV1 Spec(string netWidth) => new([new SlitLineRequestV1("490", 2)], null, netWidth);

        using (var tooWide = await client.PostWithKeyAsync($"{Base}/plans",
                   new SavePlanRequestV1(Guid.NewGuid(), slitting, Inputs("1000"), null, Spec("1000.001"), null, "50")))
        {
            Assert.Equal((HttpStatusCode.UnprocessableEntity, ManufacturingRuleCodes.PlanNetWidthExceedsParent),
                (tooWide.StatusCode, (await Problem(tooWide)).Code));
        }

        using var previewResponse = await client.PostAsJsonAsync($"{Base}/plans/preview",
            new PlanPreviewRequestV1(slitting, Inputs("1000"), null, Spec("980"), null, "50"));
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<PlanPreviewViewV1>())!;
        Assert.DoesNotContain("netUsableWidthMm", preview.MissingParameters);
        Assert.DoesNotContain("lineSpeedMetresPerMinute", preview.MissingParameters);

        var planId = Guid.NewGuid();
        var created = await PlanAsync(client, HttpStatusCode.Created, $"{Base}/plans",
            new SavePlanRequestV1(planId, slitting, Inputs("1000"), null, Spec("980.000"), null, "50"));
        Assert.Equal(("980", "50"), (created.SlittingSpec!.NetUsableWidthMm, created.LineSpeedMetresPerMinute));

        var approved = await PlanAsync(await run.ApproverAsync(CompanyA), HttpStatusCode.OK, $"{Base}/plans/{planId}/approval",
            new ApprovePlanRequestV1(1));
        Assert.Equal("Approved", approved.Status);
        var speed = approved.ParameterSnapshot!.Values.Single(value => value.Key == "lineSpeedMetresPerMinute");
        Assert.Equal(("50", "Plan"), (speed.Number, speed.Source));
        Assert.Equal("Plan", await run.ScalarAsync<string>(
            "SELECT ValueSource FROM manufacturing.PlanParameterSnapshotValues WHERE CompanyId = @c AND PlanId = @p "
            + "AND ParameterKey = 'lineSpeedMetresPerMinute'", ("@c", CompanyA), ("@p", planId)));
        Assert.Equal(980m, await run.ScalarAsync<decimal>(
            "SELECT NetUsableWidthMm FROM manufacturing.Plans WHERE CompanyId = @c AND Id = @p",
            ("@c", CompanyA), ("@p", planId)));

        // A roll whose free stock does not cover the planned kg is not usable: the approval is refused, nothing changes.
        // U3-a: the approval above allocated the roll and moved its label version; the next plan names the current one.
        rollVersion = await run.CurrentLabelVersionAsync(roll.Id);
        var shortId = Guid.NewGuid();
        await PlanAsync(client, HttpStatusCode.Created, $"{Base}/plans",
            new SavePlanRequestV1(shortId, slitting, Inputs("1500"), null, Spec("980"), null, "50"));
        using (var refused = await (await run.ApproverAsync(CompanyA)).PostWithKeyAsync($"{Base}/plans/{shortId}/approval", new ApprovePlanRequestV1(1)))
        {
            Assert.Equal((HttpStatusCode.UnprocessableEntity, ManufacturingRuleCodes.PlanLabelNotUsable),
                (refused.StatusCode, (await Problem(refused)).Code));
        }

        Assert.Equal((1, 1L), (
            await run.ScalarAsync<int>("SELECT Status FROM manufacturing.Plans WHERE CompanyId = @c AND Id = @p",
                ("@c", CompanyA), ("@p", shortId)),
            await run.ScalarAsync<long>("SELECT Version FROM manufacturing.Plans WHERE CompanyId = @c AND Id = @p",
                ("@c", CompanyA), ("@p", shortId))));
    }

    private static async Task<PlanViewV1> PlanAsync<T>(HttpClient client, HttpStatusCode expected, string path, T body,
        string? key = null)
    {
        using var response = await client.PostWithKeyAsync(path, body, key);
        Assert.True(response.StatusCode == expected, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PlanViewV1>())!;
    }

    private static async Task<string?> FieldAsync(HttpResponseMessage response)
    {
        using var problem = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("field", out var field) ? field.GetString() : null;
    }
}
