using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Üretim U2-b1 against REAL SQL Server through the REAL host (API-12; URT-D25, URT-D26, URT-D27): a Paint plan's entered
/// values (package, pieces, profile length, perimeter, the paint and its color) are saved, revised, listed in the history
/// and captured at approval with source <c>Plan</c>; a value of another process is refused by the API and by SQL; the
/// rejection keeps its own grant (U2-a review).
/// </summary>
public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Plan_process_spec_is_saved_revised_and_captured_on_real_sql()
    {
        await using var run = await Run.StartAsync(useSqlSettings: true, clockAt: CatalogDay);
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, LifecycleGrants);
        using var client = run.ClientFor(user, CompanyA);
        var paint = await run.WorkCenterOfKindAsync(CompanyA, "Paint");
        var roll = await run.StockedLabelAsync($"SYN-MFG-U2B1-{Guid.NewGuid():N}");
        var inputs = new[] { new PlannedInputRequestV1(roll.Id, "1000", roll.Version) };
        var spec = new PlanProcessSpecRequestV1(PackageCount: 10, PiecesPerPackage: 25, PieceLengthMm: "6000.000",
            PerimeterMm: "160.5", PaintDescription: "  Polyester toz / Akzo  ", PaintColorCode: "RAL 9016");

        // A Tube / Profile value on a Paint plan, and a Paint spec without its paint, are the caller's error.
        foreach (var wrong in new[]
                 {
                     spec with { UnitKg = "12.5" },
                     spec with { PaintDescription = null },
                     spec with { PackageCount = 0 },
                     spec with { PieceLengthMm = "6000.0001" },
                 })
        {
            using var refused = await client.PostWithKeyAsync($"{Base}/plans",
                new SavePlanRequestV1(Guid.NewGuid(), paint, inputs, null, ProcessSpec: wrong));
            Assert.Equal((HttpStatusCode.BadRequest, "processSpec"), (refused.StatusCode, await FieldAsync(refused)));
        }

        var planId = Guid.NewGuid();
        var created = await PlanAsync(client, HttpStatusCode.Created, $"{Base}/plans",
            new SavePlanRequestV1(planId, paint, inputs, null, ProcessSpec: spec));
        Assert.Equal(new PlanProcessSpecViewV1(10, 25, "6000", null, "160.5", null, "Polyester toz / Akzo", "RAL 9016"),
            created.ProcessSpec);
        Assert.Equal(160.5m, await run.ScalarAsync<decimal>(
            "SELECT PerimeterMm FROM manufacturing.Plans WHERE CompanyId = @c AND Id = @p", ("@c", CompanyA), ("@p", planId)));
        var area = created.Calculation!.Answers.Single(answer => answer.Formula == "PNT-005");
        Assert.DoesNotContain("perimeterMm", area.MissingParameters);

        // SQL holds the process rule whatever the application does: a Cold Rolling value on a Paint plan is refused.
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => run.ExecuteAsync(
            "UPDATE manufacturing.Plans SET TargetThicknessMm = 1.5 WHERE CompanyId = @c AND Id = @p",
            ("@c", CompanyA), ("@p", planId)));

        // The spec is part of the revision and of the history.
        var revised = await PlanAsync(client, HttpStatusCode.OK, $"{Base}/plans/{planId}/revision",
            new RevisePlanRequestV1(1, paint, inputs, null, "Paket içi adet düzeltildi",
                ProcessSpec: spec with { PiecesPerPackage = 20 }));
        Assert.Equal((2L, 20), (revised.Version, revised.ProcessSpec!.PiecesPerPackage));
        var history = (await client.GetFromJsonAsync<PlanRevisionListViewV1>($"{Base}/plans/{planId}/revisions"))!;
        Assert.Equal([25, 20], history.Revisions.Select(revision => revision.ProcessSpec!.PiecesPerPackage));

        // A writer without the approval grant cannot reject (U2-a review).
        var writerWithoutApprove = await run.GrantAsync(TestUsers.NoAssignment, CompanyA,
            [ErpPermissions.ManufacturingPlansWrite, ErpPermissions.ManufacturingPlansRead]);
        using (var noGrant = run.ClientFor(writerWithoutApprove, CompanyA))
        using (var forbidden = await noGrant.PostWithKeyAsync($"{Base}/plans/{planId}/rejection",
                   new RejectPlanRequestV1(2, "red")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        // Approval captures every entered number with source Plan beside the Paint profile's own values.
        var approved = await PlanAsync(client, HttpStatusCode.OK, $"{Base}/plans/{planId}/approval",
            new ApprovePlanRequestV1(2));
        var planValues = approved.ParameterSnapshot!.Values
            .Where(value => value.Source == "Plan")
            .ToDictionary(value => value.Key, value => value.Number);
        Assert.Equal(new Dictionary<string, string?>
        {
            ["packageCount"] = "10",
            ["piecesPerPackage"] = "20",
            ["profileLengthMm"] = "6000",
            ["perimeterMm"] = "160.5",
        }, planValues);
        Assert.Equal(4, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanParameterSnapshotValues WHERE CompanyId = @c AND PlanId = @p "
            + "AND ValueSource = 'Plan'", ("@c", CompanyA), ("@p", planId)));
        var reopened = (await client.GetFromJsonAsync<PlanViewV1>($"{Base}/plans/{planId}"))!;
        Assert.Equal(approved.ParameterSnapshot.Values, reopened.ParameterSnapshot!.Values);
        Assert.Equal("RAL 9016", reopened.ProcessSpec!.PaintColorCode);
    }
}
