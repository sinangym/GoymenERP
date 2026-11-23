using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_input_availability_HTTP_read_uses_real_owner_balances_and_never_allocates_or_exposes_a_foreign_plan()
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, [.. AllWrites, ErpPermissions.ManufacturingPlansRead]);
        using var client = run.ClientFor(user, CompanyA);
        var roll = await run.StockedLabelAsync($"SYN-MFG-QTY-{Guid.NewGuid():N}");
        var planId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/plans",
            new SavePlanRequestV1(planId, await run.WorkCenterOfAsync(CompanyA),
                [new PlannedInputRequestV1(roll.Id, "600", roll.Version)], null))).StatusCode);
        var before = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations WHERE DemandId = @plan", ("@plan", planId));
        using var response = await client.GetAsync($"{Base}/plans/{planId}/input-availability");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var view = await response.Content.ReadFromJsonAsync<PlanInputAvailabilityViewV1>();
        Assert.NotNull(view);
        Assert.Equal((CompanyA, planId, 1L), (view.CompanyId, view.PlanId, view.PlanVersion));
        var line = Assert.Single(view.Inputs);
        Assert.Equal((roll.Id, "600", "1000.125", "1000.125", "0"),
            (line.LabelId, line.RequestedKg, line.PhysicalKg, line.FreeKg, line.MissingKg));
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal(before, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations WHERE DemandId = @plan", ("@plan", planId)));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Base}/plans/{Guid.NewGuid()}/input-availability")).StatusCode);
        var foreignUser = await run.GrantAsync(TestUsers.CompanyB, CompanyB, [ErpPermissions.ManufacturingPlansRead]);
        using var foreign = run.ClientFor(foreignUser, CompanyB);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"{Base}/plans/{planId}/input-availability")).StatusCode);
    }
}
