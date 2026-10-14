using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// The plan technical snapshot migration against a real SQL Server. The snapshots below are explicit
/// persistence fixtures; they do not stand in for Product's accepted non-synthetic technical source.
/// </summary>
public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    private const string BeforeTechnicalSnapshots = "20260930220000_AddManufacturingOrderClosureDecision";
    private const string TechnicalSnapshots = "20261001200000_AddManufacturingPlanTechnicalSnapshots";
    private const string CommercialContexts = "20261004100000_AddManufacturingCommercialContexts";
    private const string PlanCutLines = "20261014120000_AddManufacturingPlanCutLines";

    [SqlServerFact]
    public async Task The_technical_snapshot_migration_round_trips_while_only_legacy_inputs_exist()
    {
        await using var run = await Run.StartAsync();
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var plan = NewPlan(CompanyA, workCenter);
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(plan, Metadata(run));
            await store.Revisions.RevisePlanAsync(plan.Id, 1, workCenter, ManufacturingProcess.Slitting,
                [new PlannedInput(RollA, Kg(9_000m), 7)], null, "legacy teknik kaynak", Metadata(run));
        }

        await using (var context = run.Context(CompanyA))
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(BeforeTechnicalSnapshots);
            Assert.Equal(BeforeTechnicalSnapshots, (await context.Database.GetAppliedMigrationsAsync()).Last());
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.columns WHERE [name] = N'TechnicalProductId' "
                + "AND [object_id] IN (OBJECT_ID(N'[manufacturing].[PlanInputs]'), "
                + "OBJECT_ID(N'[manufacturing].[PlanRevisionInputs]'))"));

            await context.Database.MigrateAsync();
            var reapplied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            Assert.Equal(PlanCutLines, reapplied[^1]);
            Assert.Equal(CommercialContexts, reapplied[^2]);
            Assert.Equal(TechnicalSnapshots, reapplied[^3]);
            Assert.Equal(BeforeTechnicalSnapshots, reapplied[^4]);
        }

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.check_constraints WHERE [name] IN "
            + "(N'CK_PlanInputs_TechnicalSnapshot', N'CK_PlanRevisionInputs_TechnicalSnapshot')"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanInputs WHERE PlanId = @p AND TechnicalProductId IS NULL",
            ("@p", plan.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanRevisionInputs "
            + "WHERE PlanId = @p AND TechnicalProductId IS NULL", ("@p", plan.Id)));
    }

    [SqlServerFact]
    public async Task The_technical_snapshot_migration_refuses_down_before_mutating_a_creation_input()
    {
        await using var run = await Run.StartAsync();
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var plan = new ProductionPlan(Guid.NewGuid(), CompanyA, workCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(RollA, Kg(10_000m), 7, TechnicalSnapshot())]);
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(plan, Metadata(run));
        }

        await using (var context = run.Context(CompanyA))
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(TechnicalSnapshots);
            Assert.Equal(TechnicalSnapshots, (await context.Database.GetAppliedMigrationsAsync()).Last());
            var error = await Assert.ThrowsAsync<SqlException>(
                () => migrator.MigrateAsync(BeforeTechnicalSnapshots));
            Assert.Equal(51047, error.Number);
            Assert.Equal(TechnicalSnapshots, (await context.Database.GetAppliedMigrationsAsync()).Last());
        }

        Assert.Equal("synthetic-product-version-v1", await run.ScalarAsync<string>(
            "SELECT TechnicalProductVersion FROM manufacturing.PlanInputs WHERE PlanId = @p",
            ("@p", plan.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.check_constraints WHERE [name] = N'CK_PlanInputs_TechnicalSnapshot'"));
    }

    [SqlServerFact]
    public async Task The_technical_snapshot_migration_refuses_down_before_mutating_a_revision_input()
    {
        await using var run = await Run.StartAsync();
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var plan = NewPlan(CompanyA, workCenter);
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(plan, Metadata(run));
            await store.Revisions.RevisePlanAsync(plan.Id, 1, workCenter, ManufacturingProcess.Slitting,
                [new PlannedInput(RollA, Kg(9_000m), 8, TechnicalSnapshot())], null,
                "revision teknik kaynak", Metadata(run));
        }

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanInputs WHERE PlanId = @p AND TechnicalProductId IS NULL",
            ("@p", plan.Id)));
        await using (var context = run.Context(CompanyA))
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(TechnicalSnapshots);
            Assert.Equal(TechnicalSnapshots, (await context.Database.GetAppliedMigrationsAsync()).Last());
            var error = await Assert.ThrowsAsync<SqlException>(
                () => migrator.MigrateAsync(BeforeTechnicalSnapshots));
            Assert.Equal(51047, error.Number);
            Assert.Equal(TechnicalSnapshots, (await context.Database.GetAppliedMigrationsAsync()).Last());
        }

        Assert.Equal("synthetic-product-version-v1", await run.ScalarAsync<string>(
            "SELECT TechnicalProductVersion FROM manufacturing.PlanRevisionInputs "
            + "WHERE PlanId = @p AND RevisionNo = 1", ("@p", plan.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.check_constraints "
            + "WHERE [name] = N'CK_PlanRevisionInputs_TechnicalSnapshot'"));
    }

    [SqlServerFact]
    public async Task Partial_technical_provenance_is_rejected_for_creation_and_revision_inputs_without_mutation()
    {
        await using var run = await Run.StartAsync();
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var plan = new ProductionPlan(Guid.NewGuid(), CompanyA, workCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(RollA, Kg(10_000m), 7, TechnicalSnapshot())]);
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(plan, Metadata(run));
            await store.Revisions.RevisePlanAsync(plan.Id, 1, workCenter, ManufacturingProcess.Slitting,
                [new PlannedInput(RollA, Kg(9_000m), 8, TechnicalSnapshot())], null,
                "partial constraint", Metadata(run));
        }

        await using (var context = run.Context(CompanyA))
        {
            var creationThickness = await Assert.ThrowsAsync<SqlException>(
                () => context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE [manufacturing].[PlanInputs]
                    SET [TechnicalThicknessMm] = NULL
                    WHERE [CompanyId] = {CompanyA} AND [PlanId] = {plan.Id}
                    """));
            Assert.Equal(547, creationThickness.Number);
            Assert.Contains("CK_PlanInputs_TechnicalSnapshot", creationThickness.Message, StringComparison.Ordinal);

            var creationWidth = await Assert.ThrowsAsync<SqlException>(
                () => context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE [manufacturing].[PlanInputs]
                    SET [TechnicalWidthMm] = NULL
                    WHERE [CompanyId] = {CompanyA} AND [PlanId] = {plan.Id}
                    """));
            Assert.Equal(547, creationWidth.Number);
            Assert.Contains("CK_PlanInputs_TechnicalSnapshot", creationWidth.Message, StringComparison.Ordinal);

            var revisionThickness = await Assert.ThrowsAsync<SqlException>(
                () => context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE [manufacturing].[PlanRevisionInputs]
                    SET [TechnicalThicknessMm] = NULL
                    WHERE [CompanyId] = {CompanyA} AND [PlanId] = {plan.Id} AND [RevisionNo] = {1}
                    """));
            Assert.Equal(547, revisionThickness.Number);
            Assert.Contains("CK_PlanRevisionInputs_TechnicalSnapshot", revisionThickness.Message,
                StringComparison.Ordinal);

            var revisionWidth = await Assert.ThrowsAsync<SqlException>(
                () => context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE [manufacturing].[PlanRevisionInputs]
                    SET [TechnicalWidthMm] = NULL
                    WHERE [CompanyId] = {CompanyA} AND [PlanId] = {plan.Id} AND [RevisionNo] = {1}
                    """));
            Assert.Equal(547, revisionWidth.Number);
            Assert.Contains("CK_PlanRevisionInputs_TechnicalSnapshot", revisionWidth.Message,
                StringComparison.Ordinal);
        }

        Assert.Equal((2m, 1_500m),
            (await run.ScalarAsync<decimal>(
                "SELECT TechnicalThicknessMm FROM manufacturing.PlanInputs WHERE PlanId = @p", ("@p", plan.Id)),
             await run.ScalarAsync<decimal>(
                "SELECT TechnicalWidthMm FROM manufacturing.PlanInputs WHERE PlanId = @p", ("@p", plan.Id))));
        Assert.Equal((2m, 1_500m),
            (await run.ScalarAsync<decimal>(
                "SELECT TechnicalThicknessMm FROM manufacturing.PlanRevisionInputs "
                + "WHERE PlanId = @p AND RevisionNo = 1", ("@p", plan.Id)),
             await run.ScalarAsync<decimal>(
                "SELECT TechnicalWidthMm FROM manufacturing.PlanRevisionInputs "
                + "WHERE PlanId = @p AND RevisionNo = 1", ("@p", plan.Id))));
    }

    private static PlanTechnicalParameterSnapshotV1 TechnicalSnapshot() => new(
        Guid.Parse("71000000-0000-4000-8000-000000000001"),
        "synthetic-product-version-v1",
        Guid.Parse("71000000-0000-4000-8000-000000000002"),
        "SYNTHETIC-COIL",
        "synthetic-family-r1",
        Guid.Parse("71000000-0000-4000-8000-000000000003"),
        "synthetic-template-v1",
        "SYNTHETIC-COIL-SHEET",
        "synthetic-template-r1",
        CompanyA,
        Guid.Parse("71000000-0000-4000-8000-000000000004"),
        "kg",
        Guid.Parse("71000000-0000-4000-8000-000000000005"),
        "synthetic-kg-profile-v1",
        new DateOnly(2026, 10, 1),
        2m,
        1_500m);
}
