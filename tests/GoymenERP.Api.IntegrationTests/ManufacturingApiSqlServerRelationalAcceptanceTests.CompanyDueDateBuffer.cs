using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_termin_HTTP_read_uses_the_stored_company_buffer_and_a_work_center_override_replaces_it_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            [.. AllWrites, ErpPermissions.ManufacturingPlansRead, SettingsWorkCenterCapacityPermissionManifestV1.Manage]);
        using var client = run.ClientFor(user, CompanyA);
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var roll = await run.StockedLabelAsync($"SYN-MFG-BUFFER-{Guid.NewGuid():N}");
        var planId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/plans",
            new SavePlanRequestV1(planId, workCenter, [new PlannedInputRequestV1(roll.Id, "500", roll.Version)], null))).StatusCode);
        var capacityPath = $"/api/v1/settings/work-center-capacities/{workCenter}/versions";
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostWithKeyAsync(capacityPath,
            new RecordWorkCenterCapacityRequestV1("0", "100", null, null, "Synthetic capacity without override"))).StatusCode);
        const string bufferPath = "/api/v1/settings/company-policies/manufacturing-due-date-buffer/versions";
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostWithKeyAsync(bufferPath,
            new RecordManufacturingDueDateBufferRequestV1("0", 2, "Synthetic two working day buffer"))).StatusCode);
        using var response = await client.GetAsync($"{Base}/plans/{planId}/due-date-suggestion");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var company = await response.Content.ReadFromJsonAsync<PlanDueDateSuggestionViewV1>();
        Assert.Equal(2, company!.SafetyMarginDays);
        // The calendar may still be unconfigured: this fact proves the real persisted buffer source, not a guessed termin.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostWithKeyAsync(capacityPath,
            new RecordWorkCenterCapacityRequestV1("1", "100", "1", "Days", "Synthetic work center replacement"))).StatusCode);
        var overridden = await client.GetFromJsonAsync<PlanDueDateSuggestionViewV1>($"{Base}/plans/{planId}/due-date-suggestion");
        Assert.Equal(1, overridden!.SafetyMarginDays);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM settings.CompanyPolicyVersions WHERE CompanyId=@company AND PolicyKey=N'manufacturing.due-date-buffer'",
            ("@company", CompanyA)));
    }
}
