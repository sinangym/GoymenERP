using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Priority_HTTP_changes_persist_a_separate_revision_replay_once_and_feed_the_real_queue_without_reapproving_content()
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, [.. AllWrites, ErpPermissions.ManufacturingPlansRead]);
        using var client = run.ClientFor(user, CompanyA);
        var roll = await run.StockedLabelAsync($"SYN-MFG-PRIORITY-{Guid.NewGuid():N}"); var planId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/plans",
            new SavePlanRequestV1(planId, await run.WorkCenterOfAsync(CompanyA), [new(roll.Id, "500", roll.Version)], null))).StatusCode);
        var path = $"{Base}/plans/{planId}/priority";
        var initial = await client.GetFromJsonAsync<PlanPriorityViewV1>(path);
        Assert.Equal((0, 0L, 1L), (initial!.PriorityScore, initial.PriorityRevision, initial.PlanVersion));
        var request = new ChangePlanPriorityRequestV1(1, 0, 17, "Synthetic priority reason");
        const string key = "synthetic-priority-one";
        using var first = await client.PostWithKeyAsync(path, request, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var applied = await first.Content.ReadFromJsonAsync<PlanPriorityViewV1>();
        using var replay = await client.PostWithKeyAsync(path, request, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal(applied, await replay.Content.ReadFromJsonAsync<PlanPriorityViewV1>());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostWithKeyAsync(path, request, "synthetic-priority-stale")).StatusCode);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanPriorityVersions WHERE CompanyId=@company AND PlanId=@plan",
            ("@company", CompanyA), ("@plan", planId)));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM manufacturing.Plans WHERE CompanyId=@company AND Id=@plan",
            ("@company", CompanyA), ("@plan", planId)));
        var queue = await client.GetFromJsonAsync<PlanWorkCenterQueueViewV1>($"{Base}/plans/{planId}/work-center-queue");
        var target = Assert.Single(queue!.Items, item => item.PlanId == planId);
        Assert.Equal((17, 1L, "500", "0", "500", false, false),
            (target.PriorityScore, target.PriorityRevision, target.PlannedKg, target.ProcessedKg, target.RemainingKg, target.IsOverPlanned, target.IsQueued));
        var reader = await run.GrantAsync(TestUsers.NoAssignment, CompanyA, [ErpPermissions.ManufacturingPlansRead]);
        using var readOnly = run.ClientFor(reader, CompanyA);
        Assert.Equal(HttpStatusCode.Forbidden, (await readOnly.PostWithKeyAsync(path, new ChangePlanPriorityRequestV1(1, 1, 18, "Synthetic denied"))).StatusCode);
        var foreignUser = await run.GrantAsync(TestUsers.CompanyB, CompanyB, [ErpPermissions.ManufacturingPlansRead]);
        using var foreign = run.ClientFor(foreignUser, CompanyB);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"{Base}/plans/{planId}/work-center-queue")).StatusCode);
    }
}
