using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    /// <summary>
    /// The existing immutable Slitting domain/persistence content through the public contract: preview and create take
    /// the same ordered lines, replay uses their canonical values, a fresh read returns the stored values, and approval
    /// remains fail-closed while the external calculation parameters have no approved source.
    /// </summary>
    [SqlServerFact]
    public async Task Slitting_draft_spec_previews_creates_replays_reopens_and_refuses_approval_without_parameters()
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            [.. AllWrites, ErpPermissions.ManufacturingPlansRead]);
        using var client = run.ClientFor(user, CompanyA);
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var roll = await run.StockedLabelAsync("SYN-MFG-SLITTING-SPEC");
        var planId = Guid.NewGuid();
        var inputs = new[] { new PlannedInputRequestV1(roll.Id, "1000", roll.Version) };
        var spec = new SlittingSpecRequestV1(
            [new SlitLineRequestV1("150.000", 4), new SlitLineRequestV1("240", 3)], "2000.000");

        using (var noPermission = run.ClientFor(TestUsers.CompanyAViewer, CompanyA))
        {
            using var refused = await noPermission.PostWithKeyAsync($"{Base}/plans",
                new SavePlanRequestV1(planId, workCenter, inputs, null, spec), "slitting-no-permission");
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Plans WHERE CompanyId = @c AND Id = @p",
            ("@c", CompanyA), ("@p", planId)));

        using (var invalidResponse = await client.PostWithKeyAsync($"{Base}/plans",
                   new SavePlanRequestV1(Guid.NewGuid(), workCenter, inputs, null, new SlittingSpecRequestV1([])),
                   "slitting-empty"))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidResponse.StatusCode);
            using var invalidProblem = JsonDocument.Parse(await invalidResponse.Content.ReadAsStringAsync());
            Assert.Equal(ManufacturingRuleCodes.SlittingSpecWithoutLines,
                invalidProblem.RootElement.GetProperty("code").GetString());
        }

        var coldRolling = await run.WorkCenterOfKindAsync(CompanyA, "ColdRolling");
        using (var wrongProcess = await client.PostWithKeyAsync($"{Base}/plans",
                   new SavePlanRequestV1(Guid.NewGuid(), coldRolling, inputs, null, spec), "slitting-wrong-process"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, wrongProcess.StatusCode);
            using var wrongProcessProblem = JsonDocument.Parse(await wrongProcess.Content.ReadAsStringAsync());
            Assert.Equal("slittingSpec", wrongProcessProblem.RootElement.GetProperty("field").GetString());
        }

        using var previewResponse = await client.PostAsJsonAsync(
            $"{Base}/plans/preview", new PlanPreviewRequestV1(workCenter, inputs, null, spec));
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<PlanPreviewViewV1>())!;
        Assert.DoesNotContain(preview.MissingParameters,
            parameter => parameter.StartsWith("lines[", StringComparison.Ordinal));
        Assert.Contains("netUsableWidthMm", preview.MissingParameters);

        var request = new SavePlanRequestV1(planId, workCenter, inputs, null, spec);
        using var createdResponse = await client.PostWithKeyAsync($"{Base}/plans", request, "slitting-spec");
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = (await createdResponse.Content.ReadFromJsonAsync<PlanViewV1>())!;
        var createdSpec = created.SlittingSpec!;
        Assert.Equal("2000", createdSpec.WindingTargetKg);
        Assert.Equal([("150", 4), ("240", 3)],
            createdSpec.Lines.Select(line => (line.BandWidthMm, line.Count)));
        Assert.Equal(JsonSerializer.Serialize(preview), JsonSerializer.Serialize(created.Calculation));

        var canonicalReplay = request with
        {
            SlittingSpec = new SlittingSpecRequestV1(
                [new SlitLineRequestV1("150", 4), new SlitLineRequestV1("240.0", 3)], "2000"),
        };
        using var replayResponse = await client.PostWithKeyAsync(
            $"{Base}/plans", canonicalReplay, "slitting-spec");
        Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
        Assert.Equal("true", Assert.Single(replayResponse.Headers.GetValues("Idempotency-Replayed")));

        using var mismatchResponse = await client.PostWithKeyAsync(
            $"{Base}/plans", request with
            {
                SlittingSpec = new SlittingSpecRequestV1(
                    [new SlitLineRequestV1("150", 5), new SlitLineRequestV1("240", 3)], "2000"),
            },
            "slitting-spec");
        Assert.Equal(HttpStatusCode.Conflict, mismatchResponse.StatusCode);

        using var reopenedResponse = await client.GetAsync($"{Base}/plans/{planId:D}");
        Assert.Equal(HttpStatusCode.OK, reopenedResponse.StatusCode);
        var reopened = (await reopenedResponse.Content.ReadFromJsonAsync<PlanViewV1>())!;
        Assert.Equal(JsonSerializer.Serialize(created), JsonSerializer.Serialize(reopened));

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanSlitLines WHERE CompanyId = @c AND PlanId = @p",
            ("@c", CompanyA), ("@p", planId)));
        Assert.Equal(150m, await run.ScalarAsync<decimal>(
            "SELECT BandWidthMm FROM manufacturing.PlanSlitLines WHERE CompanyId = @c AND PlanId = @p AND Sequence = 1",
            ("@c", CompanyA), ("@p", planId)));
        Assert.Equal(4, await run.ScalarAsync<int>(
            "SELECT BandCount FROM manufacturing.PlanSlitLines WHERE CompanyId = @c AND PlanId = @p AND Sequence = 1",
            ("@c", CompanyA), ("@p", planId)));
        Assert.Equal(240m, await run.ScalarAsync<decimal>(
            "SELECT BandWidthMm FROM manufacturing.PlanSlitLines WHERE CompanyId = @c AND PlanId = @p AND Sequence = 2",
            ("@c", CompanyA), ("@p", planId)));
        Assert.Equal(2000m, await run.ScalarAsync<decimal>(
            "SELECT WindingTargetKg FROM manufacturing.Plans WHERE CompanyId = @c AND Id = @p",
            ("@c", CompanyA), ("@p", planId)));
        Assert.Equal((1, 1),
            (await run.ScalarAsync<int>(
                    "SELECT MIN(Sequence) FROM manufacturing.PlanSlitLines WHERE CompanyId = @c AND PlanId = @p",
                    ("@c", CompanyA), ("@p", planId)),
                await run.ScalarAsync<int>(
                    "SELECT MAX(Sequence) - COUNT(*) + 1 FROM manufacturing.PlanSlitLines WHERE CompanyId = @c AND PlanId = @p",
                    ("@c", CompanyA), ("@p", planId))));

        var foreignPlanId = Guid.NewGuid();
        var foreignUser = await run.GrantAsync(TestUsers.CompanyB, CompanyB, AllWrites);
        using (var foreignClient = run.ClientFor(foreignUser, CompanyB))
        using (var foreignResponse = await foreignClient.PostWithKeyAsync($"{Base}/plans",
                   new SavePlanRequestV1(foreignPlanId, workCenter, inputs, null, spec), "slitting-foreign"))
        {
            Assert.Equal(
                (HttpStatusCode.UnprocessableEntity, ManufacturingStoreCodes.WorkCenterNotFound),
                (foreignResponse.StatusCode, (await Problem(foreignResponse)).Code));
        }
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Plans WHERE Id = @p", ("@p", foreignPlanId)));

        using var approvalResponse = await client.PostWithKeyAsync(
            $"{Base}/plans/{planId:D}/approval", new ApprovePlanRequestV1(created.Version), "slitting-approve");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, approvalResponse.StatusCode);
        using var problem = JsonDocument.Parse(await approvalResponse.Content.ReadAsStringAsync());
        Assert.Equal(ManufacturingRuleCodes.PlanParametersUnavailable,
            problem.RootElement.GetProperty("code").GetString());
        Assert.Equal((1, 1L),
            (await run.ScalarAsync<int>(
                    "SELECT Status FROM manufacturing.Plans WHERE CompanyId = @c AND Id = @p",
                    ("@c", CompanyA), ("@p", planId)),
                await run.ScalarAsync<long>(
                    "SELECT Version FROM manufacturing.Plans WHERE CompanyId = @c AND Id = @p",
                    ("@c", CompanyA), ("@p", planId))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Commands WHERE CompanyId = @c AND CommandName = @n",
            ("@c", CompanyA), ("@n", ManufacturingCommandNames.SavePlan)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Commands WHERE CompanyId = @c AND CommandName = @n",
            ("@c", CompanyA), ("@n", ManufacturingCommandNames.ApprovePlan)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId = @c AND EntityType = N'Manufacturing.ProductionPlan' AND EntityId = @p",
            ("@c", CompanyA), ("@p", planId.ToString("D"))));
    }
}
