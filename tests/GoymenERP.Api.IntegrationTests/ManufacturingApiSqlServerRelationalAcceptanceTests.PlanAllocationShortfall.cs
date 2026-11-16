using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_real_allocation_refusal_HTTP_response_shows_its_owner_quantities_after_both_modules_roll_back()
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, [.. AllWrites, ErpPermissions.ManufacturingPlansRead]);
        using var client = run.ClientFor(user, CompanyA);
        var roll = await run.StockedLabelAsync($"SYN-MFG-REFUSAL-{Guid.NewGuid():N}");
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var center = await run.WorkCenterOfAsync(CompanyA);
        foreach (var id in new[] { first, second })
        {
            Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/plans",
                new SavePlanRequestV1(id, center, [new(roll.Id, "600", roll.Version)], null))).StatusCode);
            await run.EnterTerminAsync(id);
        }
        using var approver = await run.ApproverAsync(CompanyA);
        var oldRead = await client.GetFromJsonAsync<PlanInputAvailabilityViewV1>($"{Base}/plans/{second}/input-availability");
        Assert.Equal("1000.125", Assert.Single(oldRead!.Inputs).FreeKg);
        Assert.Equal(HttpStatusCode.OK, (await approver.PostWithKeyAsync($"{Base}/plans/{first}/approval", new ApprovePlanRequestV1(1))).StatusCode);
        using var refused = await approver.PostWithKeyAsync($"{Base}/plans/{second}/approval", new ApprovePlanRequestV1(1), "synthetic-allocation-refused");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        using var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.Equal(InventoryManufacturingRealizationCodesV1.AllocationInsufficientFreeStock, body.RootElement.GetProperty("code").GetString());
        var snapshot = body.RootElement.GetProperty("planAllocationShortfall");
        Assert.Equal((CompanyA, second, roll.Id), (snapshot.GetProperty("companyId").GetGuid(), snapshot.GetProperty("planId").GetGuid(), snapshot.GetProperty("labelId").GetGuid()));
        Assert.Equal(("600", "400.125", "199.875"), (snapshot.GetProperty("requestedKg").GetString(),
            snapshot.GetProperty("availableKg").GetString(), snapshot.GetProperty("missingKg").GetString()));
        Assert.NotEqual(Assert.Single(oldRead.Inputs).LabelVersion, snapshot.GetProperty("labelVersion").GetString());
        Assert.False(snapshot.TryGetProperty("freePhysicalKg", out _)); Assert.False(snapshot.TryGetProperty("reservedKg", out _));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM manufacturing.Plans WHERE Id=@id", ("@id", second)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations WHERE DemandId=@id", ("@id", second)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Commands WHERE CommandName=N'manufacturing.plan.approve.v1' AND ResultJson LIKE @plan", ("@plan", $"%{second:D}%")));
        var foreignUser = await run.GrantAsync(TestUsers.CompanyB, CompanyB, AllWrites);
        using var foreignClient = run.ClientFor(foreignUser, CompanyB);
        using var hidden = await foreignClient.PostWithKeyAsync($"{Base}/plans/{second}/approval", new ApprovePlanRequestV1(1));
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var hiddenBody = JsonDocument.Parse(await hidden.Content.ReadAsStringAsync());
        Assert.False(hiddenBody.RootElement.TryGetProperty("planAllocationShortfall", out _));
        Assert.DoesNotContain(roll.Id.ToString(), await hidden.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [SqlServerFact]
    public async Task Two_reapproval_attempts_of_one_cancelled_order_create_exactly_one_new_allocation_generation()
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            [.. AllWrites, ErpPermissions.ManufacturingPlansRead, ErpPermissions.ManufacturingOrdersCancel]);
        using var client = run.ClientFor(user, CompanyA);
        var (orderId, roll) = await run.OpenOrderAsync(user);
        var planId = await run.ScalarAsync<Guid>("SELECT PlanId FROM manufacturing.Orders WHERE CompanyId=@company AND Id=@id",
            ("@company", CompanyA), ("@id", orderId));
        Assert.Equal(HttpStatusCode.OK, (await client.PostWithKeyAsync($"{Base}/orders/{orderId}/cancellation",
            new CancelOrderRequestV1(1, "Synthetic order cancellation for allocation race"))).StatusCode);
        var released = await client.GetFromJsonAsync<PlanViewV1>($"{Base}/plans/{planId}");
        Assert.True(released!.RequiresReapproval); Assert.Equal(3L, released.Version);
        using var approver = await run.ApproverAsync(CompanyA);
        var request = new ReapprovePlanRequestV1(released.Version, [roll]);
        var answers = await Task.WhenAll(
            approver.PostWithKeyAsync($"{Base}/plans/{planId}/reapproval", request, "synthetic-reapproval-race-a"),
            approver.PostWithKeyAsync($"{Base}/plans/{planId}/reapproval", request, "synthetic-reapproval-race-b"));
        try
        {
            Assert.Single(answers, answer => answer.StatusCode == HttpStatusCode.OK);
            Assert.Single(answers, answer => answer.StatusCode == HttpStatusCode.Conflict);
            var fresh = await client.GetFromJsonAsync<PlanViewV1>($"{Base}/plans/{planId}");
            Assert.False(fresh!.RequiresReapproval); Assert.Equal(4L, fresh.Version); Assert.Null(fresh.OrderId);
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanReapprovals WHERE CompanyId=@company AND PlanId=@plan",
                ("@company", CompanyA), ("@plan", planId)));
            Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanInputAllocations WHERE CompanyId=@company AND PlanId=@plan",
                ("@company", CompanyA), ("@plan", planId)));
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations WHERE CompanyId=@company AND DemandId=@plan AND Status=1",
                ("@company", CompanyA), ("@plan", planId)));
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations WHERE CompanyId=@company AND DemandId=@plan AND Status=3",
                ("@company", CompanyA), ("@plan", planId)));
            Assert.Equal(3, await run.ScalarAsync<int>("SELECT Status FROM manufacturing.Orders WHERE CompanyId=@company AND Id=@order",
                ("@company", CompanyA), ("@order", orderId)));
        }
        finally { foreach (var answer in answers) answer.Dispose(); }
    }
}
