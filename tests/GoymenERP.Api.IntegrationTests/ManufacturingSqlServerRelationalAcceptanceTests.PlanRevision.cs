using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// MFG-PLAN-EDIT at the store on a REAL SQL Server: the revision is append-only beside the plan as created, the plan is
/// rebuilt through its revisions, two editors of one version leave one revision, a plan that opened an order is never
/// revised even if its row claims Draft, and the migration's Down refuses (51047) while any revision is stored.
/// </summary>
public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_revision_is_appended_beside_the_plan_as_created_and_the_plan_is_rebuilt_through_it()
    {
        await using var run = await Run.StartAsync();
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var plan = NewPlan(CompanyA, workCenter);
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(plan, Metadata(run));
            var revised = await store.Revisions.RevisePlanAsync(plan.Id, 1, workCenter, ManufacturingProcess.Slitting,
                [new PlannedInput(RollB, Kg(4_000m), 5)], null, " Sadece B rulosu ", Metadata(run));
            Assert.Equal((2L, 1), (revised.Version, revised.RevisionCount));
        }

        // A fresh context rebuilds the plan through its revision: the current content is the revision's.
        await using (var store = run.Store(CompanyA))
        {
            var stored = (await store.Plans.FindPlanAsync(plan.Id))!;
            Assert.Equal((2L, 1, PlanStatus.Draft, (ProductionTarget?)null), (stored.Version, stored.RevisionCount, stored.Status, stored.Target));
            Assert.Equal([(RollB, 4_000m, (long?)5)],
                stored.Inputs.Select(input => (input.ParentRollId, input.PlannedMass.Value, input.ExpectedLabelVersion)));
            var history = (await store.Revisions.ListPlanRevisionsAsync(plan.Id))!;
            Assert.Equal([0, 1], history.Select(entry => entry.Revision));
            Assert.Equal(("Sadece B rulosu", run.Actor.ToString("D")), (history[1].Reason, history[1].RevisedBy));
            Assert.Equal(2, history[0].Inputs.Count);

            // Approval counts the revision's step; the order snapshots the revised content.
            var approved = await store.Plans.ApprovePlanAsync(plan.Id, 2, run.Actor, At, Metadata(run));
            Assert.Equal(3L, approved.Version);
            var orderId = Guid.NewGuid();
            await store.Orders.CreateOrderAsync(orderId, plan.Id, Metadata(run));
            var order = (await store.Orders.FindOrderAsync(orderId))!;
            Assert.Equal([RollB], order.PlannedInputs.Select(input => input.ParentRollId));
            Assert.Null(order.Target);
        }

        // The creation rows are untouched, and the audit carries the revision with its reason.
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanInputs WHERE PlanId = @p", ("@p", plan.Id)));
        Assert.Equal(15_000m, await run.ScalarAsync<decimal>("SELECT TargetKg FROM manufacturing.Plans WHERE Id = @p", ("@p", plan.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.plan.revised.v1' AND Reason = N'Sadece B rulosu' "
            + "AND EntityVersionBefore = 1 AND EntityVersionAfter = 2 AND SourceChannel = N'Web'"));

        // Another company's store does not see the plan, its history or revise it.
        await using (var b = run.Store(CompanyB))
        {
            Assert.Null(await b.Revisions.ListPlanRevisionsAsync(plan.Id));
            var error = await Assert.ThrowsAsync<ManufacturingStoreException>(() => b.Revisions.RevisePlanAsync(plan.Id, 3,
                workCenter, ManufacturingProcess.Slitting, [new PlannedInput(RollA, Kg(1m), 1)], null, "yabancı", Metadata(run)));
            Assert.Equal(ManufacturingStoreCodes.PlanNotFound, error.Code);
        }
    }

    [SqlServerFact]
    public async Task Two_editors_of_one_plan_version_leave_one_revision()
    {
        await using var run = await Run.StartAsync();
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var plan = NewPlan(CompanyA, workCenter);
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(plan, Metadata(run));
        }

        using var barrier = new Barrier(2);
        async Task<string?> EditAsync(decimal kg)
        {
            await using var store = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                await store.Revisions.RevisePlanAsync(plan.Id, 1, workCenter, ManufacturingProcess.Slitting,
                    [new PlannedInput(RollA, Kg(kg), 2)], null, $"editör {kg}", Metadata(run));
                return null;
            }
            catch (ManufacturingStoreException exception)
            {
                return exception.Code;
            }
        }

        var outcomes = await Task.WhenAll(Task.Run(() => EditAsync(900m)), Task.Run(() => EditAsync(800m)));

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome => outcome == ManufacturingStoreCodes.ConcurrentChange);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanRevisions WHERE PlanId = @p", ("@p", plan.Id)));
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM manufacturing.Plans WHERE Id = @p", ("@p", plan.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.plan.revised.v1' AND EntityId = @p",
            ("@p", plan.Id.ToString("D"))));
    }

    [SqlServerFact]
    public async Task A_plan_that_opened_an_order_is_never_revised_even_when_its_row_claims_draft()
    {
        await using var run = await Run.StartAsync();
        var (plan, _) = await run.ApprovedOrderAsync(CompanyA);

        // A tampered row: the approval pair removed and the version set back, as if it were a draft.
        await using (var context = run.Context(CompanyA))
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [manufacturing].[Plans] SET [Status] = 1, [ApprovedByUserId] = NULL, [ApprovedAtUtc] = NULL, [Version] = 1 WHERE [Id] = {plan.Id}");
        }

        await using (var store = run.Store(CompanyA))
        {
            var error = await Assert.ThrowsAsync<ManufacturingRuleException>(() => store.Revisions.RevisePlanAsync(plan.Id, 1,
                plan.WorkCenterId, ManufacturingProcess.Slitting, [new PlannedInput(RollA, Kg(1m), 1)], null, "emirden sonra",
                Metadata(run)));
            Assert.Equal(ManufacturingRuleCodes.PlanNotDraft, error.Code);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanRevisions"));
    }

    [SqlServerFact]
    public async Task The_revision_migration_rolls_back_only_while_no_revision_is_stored()
    {
        await using var run = await Run.StartAsync();
        const string beforeRevisions = "20260924210000_AddManufacturingInputLabelVersions";

        // Empty: Down and Up cleanly.
        await using (var context = run.Context(CompanyA))
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(beforeRevisions);
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE [name] = N'PlanRevisions' AND [schema_id] = SCHEMA_ID(N'manufacturing')"));
            await context.Database.MigrateAsync();
        }

        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var plan = NewPlan(CompanyA, workCenter);
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(plan, Metadata(run));
            await store.Revisions.RevisePlanAsync(plan.Id, 1, workCenter, ManufacturingProcess.Slitting,
                [new PlannedInput(RollA, Kg(9_000m), 3)], null, "geri alma sınaması", Metadata(run));
        }

        // With a revision: refused with Manufacturing's module number, nothing dropped.
        await using (var context = run.Context(CompanyA))
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(beforeRevisions));
            Assert.Equal(51047, error.Number);
            Assert.EndsWith("_AddManufacturingPlanRevisions", (await context.Database.GetAppliedMigrationsAsync()).Last(),
                StringComparison.Ordinal);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanRevisions WHERE PlanId = @p", ("@p", plan.Id)));
    }
}
