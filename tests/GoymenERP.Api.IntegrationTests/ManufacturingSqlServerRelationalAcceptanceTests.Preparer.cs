using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Üretim U2-d (URT-D2 "hazırlayan ≠ onaylayan") at the store on a REAL SQL Server: the saving actor is kept as the plan's
/// preparer, its approval and rejection are refused and store nothing, a reviser becomes the preparer of the version it
/// produced, a plan stored before U2-d and never revised (no preparer) is decided as before, and the preparer migration
/// refuses Down (51190) while a Draft never revised records its preparer, and otherwise travels Down and Up.
/// </summary>
public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    private const string BeforePlanPreparer = "20261102224501_AddManufacturingPlanProcessSpecs";

    private static ManufacturingCommandMetadata MetadataOf(Run run, Guid actor) => new(
        actor.ToString("D"), "synthetic.manufacturing.test-write", ["SYNTHETIC-MANUFACTURING-TEST"], run.Correlation,
        CausationId: null, IdempotencyKey: Guid.NewGuid().ToString("N"), SourceChannel: "Web");

    [SqlServerFact]
    public async Task The_preparer_of_the_current_version_cannot_decide_the_plan_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var plan = NewPlan(CompanyA, workCenter);
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(plan, Metadata(run));
        }

        Assert.Equal(run.Actor.ToString("D"), await run.ScalarAsync<string>(
            "SELECT CreatedBy FROM manufacturing.Plans WHERE Id = @p", ("@p", plan.Id)));

        // The saving actor neither approves nor rejects; nothing is stored and the version does not move.
        await using (var store = run.Store(CompanyA))
        {
            Assert.Equal(run.Actor.ToString("D"), (await store.Plans.FindPlanAsync(plan.Id))!.PreparedBy);
            Assert.Equal(ManufacturingRuleCodes.PlanSelfApproval, (await Assert.ThrowsAsync<ManufacturingRuleException>(
                () => store.Plans.ApprovePlanAsync(plan.Id, 1, run.Actor, At, Metadata(run)))).Code);
        }

        await using (var store = run.Store(CompanyA))
        {
            Assert.Equal(ManufacturingRuleCodes.PlanSelfApproval, (await Assert.ThrowsAsync<ManufacturingRuleException>(
                () => store.Lifecycle.RejectPlanAsync(plan.Id, 1, run.Actor, At, "kendi planım", Metadata(run)))).Code);
        }

        Assert.Equal((1, 1L, 0), (
            await run.ScalarAsync<int>("SELECT [Status] FROM manufacturing.Plans WHERE Id = @p", ("@p", plan.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM manufacturing.Plans WHERE Id = @p", ("@p", plan.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanRejections WHERE PlanId = @p",
                ("@p", plan.Id))));

        // Another user revises: the reviser prepared version 2, so the creator may now approve and the reviser may not.
        var reviser = Guid.NewGuid();
        await using (var store = run.Store(CompanyA))
        {
            var revised = await store.Revisions.RevisePlanAsync(plan.Id, 1, workCenter, ManufacturingProcess.Slitting,
                [new PlannedInput(RollA, Kg(9_000m), 7)], null, "miktar düzeltildi", MetadataOf(run, reviser));
            Assert.Equal(reviser.ToString("D"), revised.PreparedBy);
        }

        await using (var store = run.Store(CompanyA))
        {
            Assert.Equal(reviser.ToString("D"), (await store.Plans.FindPlanAsync(plan.Id))!.PreparedBy);
            Assert.Equal(ManufacturingRuleCodes.PlanSelfApproval, (await Assert.ThrowsAsync<ManufacturingRuleException>(
                () => store.Plans.ApprovePlanAsync(plan.Id, 2, reviser, At, MetadataOf(run, reviser)))).Code);
        }

        await using (var store = run.Store(CompanyA))
        {
            var approved = await store.Plans.ApprovePlanAsync(plan.Id, 2, run.Actor, At, Metadata(run));
            Assert.Equal((PlanStatus.Approved, 3L, run.Actor), (approved.Status, approved.Version, approved.ApprovedByUserId));
        }

        // The creating actor stays on the row; the preparer of the current version comes from the last revision.
        Assert.Equal(run.Actor.ToString("D"), await run.ScalarAsync<string>(
            "SELECT CreatedBy FROM manufacturing.Plans WHERE Id = @p", ("@p", plan.Id)));
    }

    [SqlServerFact]
    public async Task The_preparer_rollback_refuses_while_an_unrevised_draft_records_it_and_a_legacy_plan_is_decided_as_before()
    {
        await using var run = await Run.StartAsync();
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var draft = NewPlan(CompanyA, workCenter);
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(draft, Metadata(run));
        }

        // A Draft never revised has its preparer only on the plan row: Down refuses (51190) and keeps everything, so
        // its creator can never decide it after a later Up.
        await using (var context = run.Context(CompanyA))
        {
            var error = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(
                () => context.GetService<IMigrator>().MigrateAsync(BeforePlanPreparer));
            Assert.Equal(51190, error.Number);
            Assert.EndsWith("_AddManufacturingPlanPreparer", (await context.Database.GetAppliedMigrationsAsync()).Last(),
                StringComparison.Ordinal);

            // The later migrations Down did roll back (no row stopped them); Up brings the schema back to latest.
            await context.Database.MigrateAsync();
        }

        Assert.Equal(run.Actor.ToString("D"), await run.ScalarAsync<string>(
            "SELECT CreatedBy FROM manufacturing.Plans WHERE Id = @p", ("@p", draft.Id)));

        // Once that plan is decided (another user approves it) and the other Draft was revised by a second user (its
        // preparer is on the revision row), Down drops the column and Up adds it back empty.
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.ApprovePlanAsync(draft.Id, 1, run.Approver, At, Metadata(run));
        }

        var revisedDraft = NewPlan(CompanyA, workCenter);
        var reviser = Guid.NewGuid();
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(revisedDraft, Metadata(run));
        }

        await using (var store = run.Store(CompanyA))
        {
            await store.Revisions.RevisePlanAsync(revisedDraft.Id, 1, workCenter, ManufacturingProcess.Slitting,
                [new PlannedInput(RollA, Kg(9_000m), 7)], null, "miktar düzeltildi", MetadataOf(run, reviser));
        }

        await using (var context = run.Context(CompanyA))
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(BeforePlanPreparer);
            Assert.Equal(BeforePlanPreparer, (await context.Database.GetAppliedMigrationsAsync()).Last());
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.columns WHERE [name] = N'CreatedBy' "
                + "AND [object_id] = OBJECT_ID(N'[manufacturing].[Plans]')"));
            await context.Database.MigrateAsync();
            Assert.EndsWith("_AddManufacturingPlanSalesOrderLinks", (await context.Database.GetAppliedMigrationsAsync()).Last(),
                StringComparison.Ordinal);
        }

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Plans WHERE Id IN (@a, @b) AND CreatedBy IS NULL",
            ("@a", draft.Id), ("@b", revisedDraft.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.plan.created.v1' "
            + "AND EntityId = @p AND ActorId = @a", ("@p", draft.Id.ToString("D")), ("@a", run.Actor.ToString("D"))));

        // The revised Draft keeps its preparer from the revision row: the reviser is refused, the creator decides it.
        await using (var store = run.Store(CompanyA))
        {
            Assert.Equal(reviser.ToString("D"), (await store.Plans.FindPlanAsync(revisedDraft.Id))!.PreparedBy);
            Assert.Equal(ManufacturingRuleCodes.PlanSelfApproval, (await Assert.ThrowsAsync<ManufacturingRuleException>(
                () => store.Plans.ApprovePlanAsync(revisedDraft.Id, 2, reviser, At, MetadataOf(run, reviser)))).Code);
        }

        await using (var store = run.Store(CompanyA))
        {
            var approved = await store.Plans.ApprovePlanAsync(revisedDraft.Id, 2, run.Actor, At, Metadata(run));
            Assert.Equal(PlanStatus.Approved, approved.Status);
        }

        // A Draft stored before U2-d and never revised has no preparer (the row is written as such a row was) and is
        // decided as before U2-d, by the user who saved it too.
        var legacy = NewPlan(CompanyA, workCenter);
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(legacy, Metadata(run));
        }

        await run.ScalarAsync<int>("UPDATE manufacturing.Plans SET CreatedBy = NULL WHERE Id = @p; SELECT 1",
            ("@p", legacy.Id));
        await using (var store = run.Store(CompanyA))
        {
            var stored = (await store.Plans.FindPlanAsync(legacy.Id))!;
            Assert.Null(stored.PreparedBy);
            var approved = await store.Plans.ApprovePlanAsync(legacy.Id, 1, run.Actor, At, Metadata(run));
            Assert.Equal(PlanStatus.Approved, approved.Status);
        }

        // The database refuses an empty preparer whatever the application does.
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => run.ScalarAsync<int>(
            "UPDATE manufacturing.Plans SET CreatedBy = N'' WHERE Id = @p; SELECT 1", ("@p", legacy.Id)));
    }
}
