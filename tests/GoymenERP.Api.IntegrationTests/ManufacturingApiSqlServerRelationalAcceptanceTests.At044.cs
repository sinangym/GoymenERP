using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Slitting_create_preview_and_revision_bound_HamKg_by_all_physical_positions_including_blocked_stock()
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, [.. AllWrites, ErpPermissions.ManufacturingPlansRead]);
        using var client = run.ClientFor(user, CompanyA);
        var roll = await run.StockedLabelAsync($"SYN-AT044-PHYSICAL-{Guid.NewGuid():N}");
        await run.BlockAsync(roll.Id, 900m);
        var version = await run.CurrentLabelVersionAsync(roll.Id);
        var center = await run.WorkCenterOfAsync(CompanyA);
        var exact = new SavePlanRequestV1(Guid.NewGuid(), center,
            [new PlannedInputRequestV1(roll.Id, "1000.125", version)], null);
        var excess = exact with { PlanId = Guid.NewGuid(), Inputs = [new PlannedInputRequestV1(roll.Id, "1000.126", version)] };
        var auditBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");

        using (var refused = await client.PostWithKeyAsync($"{Base}/plans", excess))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            Assert.Equal(ManufacturingRuleCodes.PlanLabelNotUsable, (await Problem(refused)).Code);
            using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
            Assert.Equal("inputs[0].plannedKg", problem.RootElement.GetProperty("field").GetString());
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Plans WHERE Id = @id", ("@id", excess.PlanId)));
        Assert.Equal(auditBefore, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));

        using (var preview = await client.PostAsJsonAsync($"{Base}/plans/preview",
            new PlanPreviewRequestV1(center, exact.Inputs, null)))
        {
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            Assert.Equal("1000.125", (await preview.Content.ReadFromJsonAsync<PlanPreviewViewV1>())!.PlannedInputKg);
        }
        using (var preview = await client.PostAsJsonAsync($"{Base}/plans/preview",
            new PlanPreviewRequestV1(center, excess.Inputs, null)))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, preview.StatusCode);
            Assert.Equal(ManufacturingRuleCodes.PlanLabelNotUsable, (await Problem(preview)).Code);
        }
        Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/plans", exact)).StatusCode);
        var smaller = exact with { PlanId = Guid.NewGuid(), Inputs = [new PlannedInputRequestV1(roll.Id, "900", version)] };
        Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/plans", smaller)).StatusCode);
        var quantities = (await client.GetFromJsonAsync<PlanInputAvailabilityViewV1>($"{Base}/plans/{smaller.PlanId}/input-availability"))!;
        var input = Assert.Single(quantities.Inputs);
        Assert.Equal(("1000.125", "900", "100.125"), (input.PhysicalKg, input.BlockedKg, input.FreeKg));

        var rejectedRevision = new RevisePlanRequestV1(1, center, excess.Inputs, null, "SENTETIK physical cap");
        auditBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        using (var refused = await client.PostWithKeyAsync($"{Base}/plans/{smaller.PlanId}/revision", rejectedRevision))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            Assert.Equal(ManufacturingRuleCodes.PlanLabelNotUsable, (await Problem(refused)).Code);
        }
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM manufacturing.Plans WHERE Id = @id", ("@id", smaller.PlanId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanRevisions WHERE PlanId = @id", ("@id", smaller.PlanId)));
        Assert.Equal(auditBefore, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
        using var revised = await client.PostWithKeyAsync($"{Base}/plans/{smaller.PlanId}/revision",
            rejectedRevision with { Inputs = exact.Inputs });
        Assert.Equal(HttpStatusCode.OK, revised.StatusCode);
        var changed = (await revised.Content.ReadFromJsonAsync<PlanViewV1>())!;
        Assert.Equal((2L, "1000.125"), (changed.Version, Assert.Single(changed.Inputs).PlannedKg));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations WHERE DemandId = @id", ("@id", smaller.PlanId)));
        Assert.Equal(900m, await run.ScalarAsync<decimal>(
            "SELECT PlannedKg FROM manufacturing.PlanInputs WHERE PlanId = @id",
            ("@id", smaller.PlanId)));
    }

    [SqlServerFact]
    public async Task A_slitting_draft_may_exceed_free_stock_but_approval_still_refuses_and_rolls_back_its_allocation()
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, [.. AllWrites, ErpPermissions.ManufacturingPlansRead]);
        using var client = run.ClientFor(user, CompanyA);
        using var approver = await run.ApproverAsync(CompanyA);
        var roll = await run.StockedLabelAsync($"SYN-AT044-FREE-{Guid.NewGuid():N}");
        var center = await run.WorkCenterOfAsync(CompanyA);
        var first = new SavePlanRequestV1(Guid.NewGuid(), center,
            [new PlannedInputRequestV1(roll.Id, "600", roll.Version)], null);
        Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/plans", first)).StatusCode);
        await run.EnterTerminAsync(first.PlanId);
        Assert.Equal(HttpStatusCode.OK, (await approver.PostWithKeyAsync($"{Base}/plans/{first.PlanId}/approval", new ApprovePlanRequestV1(1))).StatusCode);

        var second = new SavePlanRequestV1(Guid.NewGuid(), center,
            [new PlannedInputRequestV1(roll.Id, "1000.125", await run.CurrentLabelVersionAsync(roll.Id))], null);
        Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/plans", second)).StatusCode);
        await run.EnterTerminAsync(second.PlanId);
        var auditBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        using var refused = await approver.PostWithKeyAsync($"{Base}/plans/{second.PlanId}/approval", new ApprovePlanRequestV1(1));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(InventoryManufacturingRealizationCodesV1.AllocationInsufficientFreeStock, (await Problem(refused)).Code);
        var stored = (await client.GetFromJsonAsync<PlanViewV1>($"{Base}/plans/{second.PlanId}"))!;
        Assert.Equal(("Draft", 1L), (stored.Status, stored.Version));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations WHERE DemandId = @id", ("@id", second.PlanId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanInputAllocations WHERE PlanId = @id", ("@id", second.PlanId)));
        Assert.Equal(auditBefore, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
    }
}
