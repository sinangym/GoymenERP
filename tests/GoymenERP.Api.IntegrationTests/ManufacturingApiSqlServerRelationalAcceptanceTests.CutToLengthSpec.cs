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

        var createdReopened = (await client.GetFromJsonAsync<PlanViewV1>($"{Base}/plans/{planId:D}"))!;
        Assert.Equal(
            [(lineA, 40, "6000", 3), (lineB, -7, "2500.5", 2)],
            createdReopened.CutToLengthSpec!.Lines.Select(line =>
                (line.LineId, line.Sequence, line.LengthMm, line.Count)));
        var createdHistory = (await client.GetFromJsonAsync<PlanRevisionListViewV1>(
            $"{Base}/plans/{planId:D}/revisions"))!;
        var createdRevision = Assert.Single(createdHistory.Revisions);
        Assert.Equal(0, createdRevision.Revision);
        Assert.Equal(
            [(lineA, 40, "6000", 3), (lineB, -7, "2500.5", 2)],
            createdRevision.CutToLengthSpec!.Lines.Select(line =>
                (line.LineId, line.Sequence, line.LengthMm, line.Count)));

        var revisedSpec = new CutToLengthSpecRequestV1(
        [
            new CutToLengthLineRequestV1(lineB, 90, "2500.500", 4),
            new CutToLengthLineRequestV1(lineA, 5, "6000", 1),
        ]);
        var revise = new RevisePlanRequestV1(
            1, workCenter, inputs, target, "Kesim sırası değişti", revisedSpec);
        using var revisedResponse = await client.PostWithKeyAsync(
            $"{Base}/plans/{planId:D}/revision",
            revise,
            "cut-lines-revise");
        Assert.Equal(HttpStatusCode.OK, revisedResponse.StatusCode);
        var revised = (await revisedResponse.Content.ReadFromJsonAsync<PlanViewV1>())!;
        Assert.Equal(
            [(lineB, 90, "2500.5", 4), (lineA, 5, "6000", 1)],
            revised.CutToLengthSpec!.Lines.Select(line =>
                (line.LineId, line.Sequence, line.LengthMm, line.Count)));

        using var reviseReplayResponse = await client.PostWithKeyAsync(
            $"{Base}/plans/{planId:D}/revision", revise, "cut-lines-revise");
        Assert.Equal(HttpStatusCode.OK, reviseReplayResponse.StatusCode);
        Assert.Equal("true", Assert.Single(reviseReplayResponse.Headers.GetValues("Idempotency-Replayed")));
        var reviseReplay = (await reviseReplayResponse.Content.ReadFromJsonAsync<PlanViewV1>())!;
        Assert.Equal(JsonSerializer.Serialize(revised), JsonSerializer.Serialize(reviseReplay));

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

        var storedCreatedLines = JsonSerializer.Deserialize<CutLineSqlRow[]>(await run.ScalarAsync<string>(
            "SELECT (SELECT LineId, Ordinal, Sequence, LengthMm, [Count] FROM manufacturing.PlanCutLines "
            + "WHERE CompanyId = @c AND PlanId = @p ORDER BY Ordinal FOR JSON PATH)",
            ("@c", CompanyA), ("@p", planId)))!;
        Assert.Equal(
            [(lineA, 1, 40, 6000.000m, 3), (lineB, 2, -7, 2500.500m, 2)],
            storedCreatedLines.Select(line =>
                (line.LineId, line.Ordinal, line.Sequence, line.LengthMm, line.Count)));
        var storedRevisionLines = JsonSerializer.Deserialize<CutLineSqlRow[]>(await run.ScalarAsync<string>(
            "SELECT (SELECT LineId, Ordinal, Sequence, LengthMm, [Count] "
            + "FROM manufacturing.PlanRevisionCutLines WHERE CompanyId = @c AND PlanId = @p AND RevisionNo = 1 "
            + "ORDER BY Ordinal FOR JSON PATH)",
            ("@c", CompanyA), ("@p", planId)))!;
        Assert.Equal(
            [(lineB, 1, 90, 2500.500m, 4), (lineA, 2, 5, 6000.000m, 1)],
            storedRevisionLines.Select(line =>
                (line.LineId, line.Ordinal, line.Sequence, line.LengthMm, line.Count)));

        var legacyPlanId = Guid.NewGuid();
        using var legacyResponse = await client.PostWithKeyAsync(
            $"{Base}/plans", new SavePlanRequestV1(legacyPlanId, workCenter, inputs, null), "cut-lines-legacy");
        Assert.Equal(HttpStatusCode.Created, legacyResponse.StatusCode);
        var legacy = (await client.GetFromJsonAsync<PlanViewV1>($"{Base}/plans/{legacyPlanId:D}"))!;
        Assert.Null(legacy.CutToLengthSpec);
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanCutLines WHERE CompanyId = @c AND PlanId = @p",
            ("@c", CompanyA), ("@p", legacyPlanId)));

        using var legacyRevisedResponse = await client.PostWithKeyAsync(
            $"{Base}/plans/{legacyPlanId:D}/revision",
            new RevisePlanRequestV1(1, workCenter, inputs, null, "Kesim satırı olmayan plan", null),
            "cut-lines-legacy-revise");
        Assert.Equal(HttpStatusCode.OK, legacyRevisedResponse.StatusCode);
        var legacyRevised = (await legacyRevisedResponse.Content.ReadFromJsonAsync<PlanViewV1>())!;
        Assert.Null(legacyRevised.CutToLengthSpec);
        var legacyReopened = (await client.GetFromJsonAsync<PlanViewV1>($"{Base}/plans/{legacyPlanId:D}"))!;
        Assert.Equal(JsonSerializer.Serialize(legacyRevised), JsonSerializer.Serialize(legacyReopened));
        Assert.Null(legacyReopened.CutToLengthSpec);
        var legacyHistory = (await client.GetFromJsonAsync<PlanRevisionListViewV1>(
            $"{Base}/plans/{legacyPlanId:D}/revisions"))!;
        Assert.Equal([0, 1], legacyHistory.Revisions.Select(revision => revision.Revision));
        Assert.All(legacyHistory.Revisions, revision => Assert.Null(revision.CutToLengthSpec));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanRevisionCutLines WHERE CompanyId = @c AND PlanId = @p",
            ("@c", CompanyA), ("@p", legacyPlanId)));

        var userB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, ErpPermissions.ManufacturingPlansRead);
        using var otherCompany = run.ClientFor(userB, CompanyB);
        using var hidden = await otherCompany.GetAsync($"{Base}/plans/{planId:D}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.DoesNotContain("\"code\"", await hidden.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private sealed record CutLineSqlRow(Guid LineId, int Ordinal, int Sequence, decimal LengthMm, int Count);
}
