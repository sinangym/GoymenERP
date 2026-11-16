using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_real_allocation_refusal_HTTP_response_shows_its_owner_quantities_after_both_modules_roll_back()
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, AllWrites);
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
        Assert.Equal(HttpStatusCode.OK, (await approver.PostWithKeyAsync($"{Base}/plans/{first}/approval", new ApprovePlanRequestV1(1))).StatusCode);
        using var refused = await approver.PostWithKeyAsync($"{Base}/plans/{second}/approval", new ApprovePlanRequestV1(1), "synthetic-allocation-refused");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        using var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.Equal(InventoryManufacturingRealizationCodesV1.AllocationInsufficientFreeStock, body.RootElement.GetProperty("code").GetString());
        var snapshot = body.RootElement.GetProperty("planAllocationShortfall");
        Assert.Equal((CompanyA, second, roll.Id), (snapshot.GetProperty("companyId").GetGuid(), snapshot.GetProperty("planId").GetGuid(), snapshot.GetProperty("labelId").GetGuid()));
        Assert.Equal(("600", "400.125", "199.875"), (snapshot.GetProperty("requestedKg").GetString(),
            snapshot.GetProperty("availableKg").GetString(), snapshot.GetProperty("missingKg").GetString()));
        Assert.False(snapshot.TryGetProperty("freePhysicalKg", out _)); Assert.False(snapshot.TryGetProperty("reservedKg", out _));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM manufacturing.Plans WHERE Id=@id", ("@id", second)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations WHERE DemandId=@id", ("@id", second)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Commands WHERE CommandName=N'manufacturing.plan.approve.v1' AND ResultJson LIKE @plan", ("@plan", $"%{second:D}%")));
    }
}
