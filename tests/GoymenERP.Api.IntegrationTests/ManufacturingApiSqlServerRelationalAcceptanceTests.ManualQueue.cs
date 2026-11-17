using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Manual_queue_HTTP_has_a_separate_permission_exact_replay_and_queue_revision_fence()
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            [.. AllWrites, ErpPermissions.ManufacturingPlansRead, ErpPermissions.ManufacturingPlansReorder]);
        using var client = run.ClientFor(user, CompanyA); using var approver = await run.ApproverAsync(CompanyA);
        var center = await run.WorkCenterOfAsync(CompanyA); var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        foreach (var id in ids)
        {
            var roll = await run.StockedLabelAsync($"SYN-QUEUE-{Guid.NewGuid():N}");
            Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/plans",
                new SavePlanRequestV1(id, center, [new(roll.Id, "500", roll.Version)], null))).StatusCode);
            await run.EnterTerminAsync(id);
            Assert.Equal(HttpStatusCode.OK, (await approver.PostWithKeyAsync($"{Base}/plans/{id}/approval", new ApprovePlanRequestV1(1))).StatusCode);
        }
        var readPath = $"{Base}/plans/{ids[0]}/work-center-queue"; var path = readPath + "/reorder";
        var initial = (await client.GetFromJsonAsync<PlanWorkCenterQueueViewV1>(readPath))!;
        Assert.Equal((0L, false), (initial.QueueRevision, initial.HasManualOrder));
        var last = initial.Items.Last(item => item.IsQueued).PlanId; var first = initial.Items.First(item => item.IsQueued).PlanId;
        var request = new MoveManufacturingQueuePlanRequestV1(initial.TargetPlanVersion, 0, last, first, "Synthetic manager override");
        using var applied = await client.PostWithKeyAsync(path, request, "synthetic-queue-exact");
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        var moved = (await applied.Content.ReadFromJsonAsync<PlanWorkCenterQueueViewV1>())!;
        Assert.Equal([last, first], moved.Items.Where(item => item.IsQueued).Select(item => item.PlanId));
        Assert.Equal((1L, true), (moved.QueueRevision, moved.HasManualOrder));
        using var replay = await client.PostWithKeyAsync(path, request, "synthetic-queue-exact");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal(1L, (await replay.Content.ReadFromJsonAsync<PlanWorkCenterQueueViewV1>())!.QueueRevision);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostWithKeyAsync(path, request with { Reason = "Different request" }, "synthetic-queue-exact")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostWithKeyAsync(path, request, "synthetic-queue-stale")).StatusCode);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.WorkCenterQueueVersions WHERE CompanyId=@company AND WorkCenterId=@center",
            ("@company", CompanyA), ("@center", center)));
        var reader = await run.GrantAsync(TestUsers.NoAssignment, CompanyA, [ErpPermissions.ManufacturingPlansRead]);
        using var readOnly = run.ClientFor(reader, CompanyA);
        Assert.Equal(HttpStatusCode.Forbidden, (await readOnly.PostWithKeyAsync(path, request with { ExpectedQueueRevision = 1 })).StatusCode);
        var foreignUser = await run.GrantAsync(TestUsers.CompanyB, CompanyB, [ErpPermissions.ManufacturingPlansRead, ErpPermissions.ManufacturingPlansReorder]);
        using var foreign = run.ClientFor(foreignUser, CompanyB);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.PostWithKeyAsync(path, request)).StatusCode);
    }
}
