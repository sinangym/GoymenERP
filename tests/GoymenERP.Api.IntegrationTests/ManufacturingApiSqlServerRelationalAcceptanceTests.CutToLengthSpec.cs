using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// M01.2 against the real host and SQL Server. Client line identity, input order and explicit sequence are immutable
/// plan content; preview, save, revision, reopen and history all use the same lossless contract. Synthetic grants and
/// Inventory labels live only in this disposable acceptance database.
/// </summary>
public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Cut_to_length_lines_preview_save_replay_revise_reopen_and_persist_losslessly()
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            [.. AllWrites, ErpPermissions.ManufacturingPlansRead]);
        using var client = run.ClientFor(user, CompanyA);
        var workCenter = await run.WorkCenterOfKindAsync(CompanyA, "CutToLength");
        var roll = await run.StockedLabelAsync("SYN-MFG-CUT-LINES");
        var planId = Guid.NewGuid();
        var lineA = Guid.Parse("50000000-0000-0000-0000-000000000001");
        var lineB = Guid.Parse("50000000-0000-0000-0000-000000000002");
        var inputs = new[] { new PlannedInputRequestV1(roll.Id, "1000", roll.Version) };
        var createdSpec = new CutToLengthSpecRequestV1(
        [
            new CutToLengthLineRequestV1(lineA, 40, "6000.000", 3),
            new CutToLengthLineRequestV1(lineB, -7, "2500.5", 2),
        ]);
        var target = new TargetRequestV1("GoodOutputMass", "900");

        using var previewResponse = await client.PostAsJsonAsync(
            $"{Base}/plans/preview", new PlanPreviewRequestV1(
                workCenter, inputs, target, CutToLengthSpec: createdSpec));
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<PlanPreviewViewV1>())!;
        Assert.DoesNotContain(preview.MissingParameters,
            parameter => parameter.StartsWith("lines[", StringComparison.Ordinal));
        Assert.Contains("densityKgPerCubicMetre", preview.MissingParameters);
        Assert.Contains("theoreticalGoodOutputKg", preview.MissingParameters);

        var save = new SavePlanRequestV1(planId, workCenter, inputs, target,
            CutToLengthSpec: createdSpec);
        using var createdResponse = await client.PostWithKeyAsync($"{Base}/plans", save, "cut-lines-save");
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = (await createdResponse.Content.ReadFromJsonAsync<PlanViewV1>())!;
        Assert.Equal(
            [(lineA, 40, "6000", 3), (lineB, -7, "2500.5", 2)],
            created.CutToLengthSpec!.Lines.Select(line =>
                (line.LineId, line.Sequence, line.LengthMm, line.Count)));

        using var replayResponse = await client.PostWithKeyAsync(
            $"{Base}/plans",
            save with
            {
                CutToLengthSpec = new CutToLengthSpecRequestV1(
                [
                    new CutToLengthLineRequestV1(lineA, 40, "6000", 3),
                    new CutToLengthLineRequestV1(lineB, -7, "2500.500", 2),
                ]),
            },
            "cut-lines-save");
        Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
        Assert.Equal("true", Assert.Single(replayResponse.Headers.GetValues("Idempotency-Replayed")));

        using var conflictingReplay = await client.PostWithKeyAsync(
            $"{Base}/plans",
            save with
            {
                CutToLengthSpec = new CutToLengthSpecRequestV1(
                [
                    new CutToLengthLineRequestV1(lineA, 41, "6000", 3),
                    new CutToLengthLineRequestV1(lineB, -7, "2500.5", 2),
                ]),
            },
            "cut-lines-save");
        Assert.Equal(HttpStatusCode.Conflict, conflictingReplay.StatusCode);

        var revisedSpec = new CutToLengthSpecRequestV1(
        [
            new CutToLengthLineRequestV1(lineB, 90, "2500.500", 4),
            new CutToLengthLineRequestV1(lineA, 5, "6000", 1),
        ]);
        using var revisedResponse = await client.PostWithKeyAsync(
            $"{Base}/plans/{planId:D}/revision",
            new RevisePlanRequestV1(1, workCenter, inputs, target, "Kesim sırası değişti", revisedSpec),
            "cut-lines-revise");
        Assert.Equal(HttpStatusCode.OK, revisedResponse.StatusCode);
        var revised = (await revisedResponse.Content.ReadFromJsonAsync<PlanViewV1>())!;
        Assert.Equal(
            [(lineB, 90, "2500.5", 4), (lineA, 5, "6000", 1)],
            revised.CutToLengthSpec!.Lines.Select(line =>
                (line.LineId, line.Sequence, line.LengthMm, line.Count)));

        var reopened = (await client.GetFromJsonAsync<PlanViewV1>($"{Base}/plans/{planId:D}"))!;
        Assert.Equal(JsonSerializer.Serialize(revised), JsonSerializer.Serialize(reopened));
        var history = (await client.GetFromJsonAsync<PlanRevisionListViewV1>(
            $"{Base}/plans/{planId:D}/revisions"))!;
        Assert.Equal([0, 1], history.Revisions.Select(revision => revision.Revision));
        Assert.Equal(
            [(lineA, 40, "6000", 3), (lineB, -7, "2500.5", 2)],
            history.Revisions[0].CutToLengthSpec!.Lines.Select(line =>
                (line.LineId, line.Sequence, line.LengthMm, line.Count)));
        Assert.Equal(
            [(lineB, 90, "2500.5", 4), (lineA, 5, "6000", 1)],
            history.Revisions[1].CutToLengthSpec!.Lines.Select(line =>
                (line.LineId, line.Sequence, line.LengthMm, line.Count)));

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanCutLines WHERE CompanyId = @c AND PlanId = @p",
            ("@c", CompanyA), ("@p", planId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanCutLines WHERE CompanyId = @c AND PlanId = @p "
            + "AND LineId = @line AND Ordinal = 2 AND Sequence = -7 AND LengthMm = 2500.500 AND [Count] = 2",
            ("@c", CompanyA), ("@p", planId), ("@line", lineB)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanRevisionCutLines WHERE CompanyId = @c AND PlanId = @p AND RevisionNo = 1",
            ("@c", CompanyA), ("@p", planId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanRevisionCutLines WHERE CompanyId = @c AND PlanId = @p "
            + "AND RevisionNo = 1 AND LineId = @line AND Ordinal = 1 AND Sequence = 90 "
            + "AND LengthMm = 2500.500 AND [Count] = 4",
            ("@c", CompanyA), ("@p", planId), ("@line", lineB)));

        var legacyPlanId = Guid.NewGuid();
        using var legacyResponse = await client.PostWithKeyAsync(
            $"{Base}/plans", new SavePlanRequestV1(legacyPlanId, workCenter, inputs, null), "cut-lines-legacy");
        Assert.Equal(HttpStatusCode.Created, legacyResponse.StatusCode);
        var legacy = (await client.GetFromJsonAsync<PlanViewV1>($"{Base}/plans/{legacyPlanId:D}"))!;
        Assert.Null(legacy.CutToLengthSpec);
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanCutLines WHERE CompanyId = @c AND PlanId = @p",
            ("@c", CompanyA), ("@p", legacyPlanId)));

        var userB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, ErpPermissions.ManufacturingPlansRead);
        using var otherCompany = run.ClientFor(userB, CompanyB);
        using var hidden = await otherCompany.GetAsync($"{Base}/plans/{planId:D}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.DoesNotContain("\"code\"", await hidden.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
