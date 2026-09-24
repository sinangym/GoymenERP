using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// MFG-5 against a REAL SQL Server: Manufacturing's own migration on top of the host's migrated
/// and seeded database (Organization's companies and work centers) and Audit's real save
/// interceptor. Every plan, order, roll and fact below is synthetic test data in this test's own
/// database; nothing is seeded.
/// </summary>
public sealed class ManufacturingSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid RollA = Guid.Parse("5a1d2c3b-4a59-4687-9a0b-1c2d3e4f5a71");
    private static readonly Guid RollB = Guid.Parse("5a1d2c3b-4a59-4687-9a0b-1c2d3e4f5a72");
    private static readonly DateTimeOffset At = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task Accepted_inventory_label_versions_are_durable_in_the_plan_and_order_snapshot()
    {
        await using var run = await Run.StartAsync();
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var plan = new ProductionPlan(Guid.NewGuid(), CompanyA, workCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(RollA, Kg(10_000m), 17), new PlannedInput(RollB, Kg(5_000m), 23)],
            new ProductionTarget(TargetBasis.InputMass, Kg(15_000m)));
        var orderId = Guid.NewGuid();

        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(plan, Metadata(run));
            await store.Plans.ApprovePlanAsync(plan.Id, 1, run.Actor, At, Metadata(run));
            await store.Orders.CreateOrderAsync(orderId, plan.Id, Metadata(run));
        }

        await using var context = run.Context(CompanyA);
        var planVersions = await context.PlanInputs.AsNoTracking()
            .Where(row => row.PlanId == plan.Id)
            .OrderBy(row => row.Sequence)
            .Select(row => row.ExpectedLabelVersion!.Value)
            .ToArrayAsync();
        var orderVersions = await context.OrderInputs.AsNoTracking()
            .Where(row => row.OrderId == orderId)
            .OrderBy(row => row.Sequence)
            .Select(row => row.ExpectedLabelVersion!.Value)
            .ToArrayAsync();
        Assert.Equal(new long[] { 17, 23 }, planVersions);
        Assert.Equal(new long[] { 17, 23 }, orderVersions);
    }

    [SqlServerFact]
    public async Task The_input_label_version_migration_round_trips_and_legacy_null_keeps_its_replay_identity()
    {
        await using var run = await Run.StartAsync();
        const string beforeLabelVersions = "20260924150000_ExpandManufacturingProcesses";
        var legacy = NewPlan(CompanyA, await run.WorkCenterOfAsync(CompanyA));

        await using (var store = run.Store(CompanyA))
        {
            Assert.Equal(ManufacturingWriteOutcome.Recorded,
                await store.Plans.SavePlanAsync(legacy, Metadata(run)));
        }

        await using (var context = run.Context(CompanyA))
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(beforeLabelVersions);
            Assert.Equal(beforeLabelVersions, (await context.Database.GetAppliedMigrationsAsync()).Last());
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.columns WHERE [name] = N'ExpectedLabelVersion' "
                + "AND [object_id] IN (OBJECT_ID(N'[manufacturing].[PlanInputs]'), "
                + "OBJECT_ID(N'[manufacturing].[OrderInputs]'))"));
            await context.Database.MigrateAsync();
        }

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanInputs WHERE PlanId = @id AND ExpectedLabelVersion IS NULL",
            ("@id", legacy.Id)));
        await using var replayStore = run.Store(CompanyA);
        Assert.Equal(ManufacturingWriteOutcome.Replayed,
            await replayStore.Plans.SavePlanAsync(Draft(legacy), Metadata(run)));
    }

    [SqlServerFact]
    public async Task Plan_order_and_facts_are_durable_replayable_audited_and_close_from_stored_facts()
    {
        await using var run = await Run.StartAsync();
        var (plan, orderId) = await run.ApprovedOrderAsync(CompanyA);
        var issueA = Guid.NewGuid();

        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(ManufacturingWriteOutcome.Recorded,
                await a.Orders.IssueInputAsync(orderId, 1, issueA, RollA, Kg(10_000m), At, Metadata(run)));
            await a.Orders.IssueInputAsync(orderId, 2, Guid.NewGuid(), RollB, Kg(5_000m), At.AddMinutes(1), Metadata(run));
            await a.Orders.DeclareOutputAsync(orderId, 3, Guid.NewGuid(), RollA, OutputClass.GoodProduct, Kg(9_700.125m),
                At.AddHours(1), Metadata(run));
            await a.Orders.DeclareOutputAsync(orderId, 4, Guid.NewGuid(), RollA, OutputClass.Scrap, Kg(299.875m),
                At.AddHours(1).AddMinutes(1), Metadata(run));
            await a.Orders.DeclareOutputAsync(orderId, 5, Guid.NewGuid(), RollB, OutputClass.GoodProduct, Kg(3_000m),
                At.AddHours(2), Metadata(run));
            await a.Orders.RecordWorkInProgressAsync(orderId, 6, Guid.NewGuid(), RollB, Kg(2_000m), At.AddHours(3), Metadata(run));

            // Same id, same content (5.00 written as 5): a replay, no second fact, no version change.
            Assert.Equal(ManufacturingWriteOutcome.Replayed,
                await a.Orders.IssueInputAsync(orderId, 1, issueA, RollA, Kg(10_000.000m), At, Metadata(run)));
            // Same id, other content: refused.
            Assert.Equal(ManufacturingStoreCodes.FactIdConflict, (await Assert.ThrowsAsync<ManufacturingStoreException>(
                () => a.Orders.IssueInputAsync(orderId, 7, issueA, RollA, Kg(9_999m), At, Metadata(run)))).Code);
            Assert.Equal(ManufacturingWriteOutcome.Replayed, await a.Plans.SavePlanAsync(Draft(plan), Metadata(run)));
            Assert.Equal(ManufacturingWriteOutcome.Replayed, await a.Orders.CreateOrderAsync(orderId, plan.Id, Metadata(run)));
        }

        // Close decided on the order rebuilt from its stored facts: roll B still has 2.000 kg WIP,
        // no roll has an unexplained difference, so the order closes and the WIP stays.
        await using (var a = run.Store(CompanyA))
        {
            await a.Orders.CloseOrderAsync(orderId, 7, run.Actor, At.AddHours(4), Metadata(run));
        }

        await using (var a = run.Store(CompanyA))
        {
            var stored = (await a.Orders.FindOrderAsync(orderId))!;
            Assert.Equal((OrderStatus.Closed, 8L), (stored.Status, stored.Version));
            Assert.Equal(2_000m, stored.BalanceOf(RollB).WorkInProgress.Value);
            Assert.Equal(9_700.125m, stored.BalanceOf(RollA).OutputOf(OutputClass.GoodProduct).Value);
            Assert.Equal(0m, stored.BalanceOf(RollA).Unexplained);
            Assert.Equal(ManufacturingRuleCodes.OrderClosed, Assert.IsType<ManufacturingRuleException>(await Record.ExceptionAsync(
                () => a.Orders.DeclareOutputAsync(orderId, 8, Guid.NewGuid(), RollB, OutputClass.Scrap, Kg(2_000m),
                    At.AddHours(5), Metadata(run)))).Code);
        }

        Assert.Equal(6, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Facts"));
        Assert.Equal(8L, await run.ScalarAsync<long>("SELECT Version FROM manufacturing.Orders WHERE Id = @id", ("@id", orderId)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT Status FROM manufacturing.Orders WHERE Id = @id", ("@id", orderId)));
        // One audit event per stored write: plan created, approved, order created, six facts, the
        // close. None for the replays or the refusals.
        Assert.Equal(10, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @c", ("@c", run.Correlation)));
    }

    [SqlServerFact]
    public async Task A_close_from_stored_facts_reconciles_each_roll_on_its_own()
    {
        await using var run = await Run.StartAsync();
        var (_, orderId) = await run.ApprovedOrderAsync(CompanyA);

        // Totals net to zero across the order, but each roll is off by 1.000 kg.
        await using (var a = run.Store(CompanyA))
        {
            await a.Orders.IssueInputAsync(orderId, 1, Guid.NewGuid(), RollA, Kg(5_000m), At, Metadata(run));
            await a.Orders.IssueInputAsync(orderId, 2, Guid.NewGuid(), RollB, Kg(5_000m), At.AddMinutes(1), Metadata(run));
            await a.Orders.DeclareOutputAsync(orderId, 3, Guid.NewGuid(), RollA, OutputClass.GoodProduct, Kg(3_000m),
                At.AddHours(1), Metadata(run));
            await a.Orders.RecordWorkInProgressAsync(orderId, 4, Guid.NewGuid(), RollA, Kg(3_000m), At.AddHours(2), Metadata(run));
            await a.Orders.DeclareOutputAsync(orderId, 5, Guid.NewGuid(), RollB, OutputClass.GoodProduct, Kg(4_000m),
                At.AddHours(3), Metadata(run));
        }

        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(ManufacturingRuleCodes.UnexplainedDifference, Assert.IsType<ManufacturingRuleException>(
                await Record.ExceptionAsync(() => a.Orders.CloseOrderAsync(orderId, 6, run.Actor, At.AddHours(4), Metadata(run)))).Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT Status FROM manufacturing.Orders WHERE Id = @id", ("@id", orderId)));
        Assert.Equal(6L, await run.ScalarAsync<long>("SELECT Version FROM manufacturing.Orders WHERE Id = @id", ("@id", orderId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.order.closed.v1'"));

        // Each roll reconciled on its own, the same stored order closes and keeps its WIP.
        await using (var a = run.Store(CompanyA))
        {
            await a.Orders.DeclareOutputAsync(orderId, 6, Guid.NewGuid(), RollB, OutputClass.Scrap, Kg(1_000m), At.AddHours(5),
                Metadata(run));
            await a.Orders.RecordWorkInProgressAsync(orderId, 7, Guid.NewGuid(), RollA, Kg(2_000m), At.AddHours(6), Metadata(run));
            await a.Orders.CloseOrderAsync(orderId, 8, run.Actor, At.AddHours(7), Metadata(run));
            Assert.Equal(2_000m, (await a.Orders.FindOrderAsync(orderId))!.BalanceOf(RollA).WorkInProgress.Value);
        }
    }

    [SqlServerFact]
    public async Task Each_company_sees_only_its_own_orders_and_a_fact_id_is_unique_per_company_only()
    {
        await using var run = await Run.StartAsync();
        var (planA, orderA) = await run.ApprovedOrderAsync(CompanyA);
        var (_, orderB) = await run.ApprovedOrderAsync(CompanyB);
        var sharedFactId = Guid.NewGuid();

        await using (var a = run.Store(CompanyA))
        {
            await a.Orders.IssueInputAsync(orderA, 1, sharedFactId, RollA, Kg(10_000m), At, Metadata(run));
        }

        await using (var b = run.Store(CompanyB))
        {
            Assert.Null(await b.Orders.FindOrderAsync(orderA));
            Assert.Null(await b.Plans.FindPlanAsync(planA.Id));
            Assert.Equal(ManufacturingStoreCodes.OrderNotFound, (await Assert.ThrowsAsync<ManufacturingStoreException>(
                () => b.Orders.IssueInputAsync(orderA, 2, Guid.NewGuid(), RollA, Kg(1m), At, Metadata(run)))).Code);
            Assert.Equal(ManufacturingStoreCodes.PlanNotFound, (await Assert.ThrowsAsync<ManufacturingStoreException>(
                () => b.Orders.CreateOrderAsync(Guid.NewGuid(), planA.Id, Metadata(run)))).Code);
            Assert.Equal(ManufacturingStoreCodes.CompanyContextMismatch, (await Assert.ThrowsAsync<ManufacturingStoreException>(
                () => b.Plans.SavePlanAsync(NewPlan(CompanyA, planA.WorkCenterId), Metadata(run)))).Code);

            // The same fact id is company B's own fact: unique per company only.
            Assert.Equal(ManufacturingWriteOutcome.Recorded,
                await b.Orders.IssueInputAsync(orderB, 1, sharedFactId, RollA, Kg(10_000m), At, Metadata(run)));
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Facts WHERE Id = @id", ("@id", sharedFactId)));
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM manufacturing.Orders WHERE Id = @id", ("@id", orderA)));
    }

    [SqlServerFact]
    public async Task A_fact_id_is_one_fact_in_the_store_and_in_the_database()
    {
        await using var run = await Run.StartAsync();
        var (_, orderId) = await run.ApprovedOrderAsync(CompanyA);
        var issueId = Guid.NewGuid();

        await using (var a = run.Store(CompanyA))
        {
            await a.Orders.IssueInputAsync(orderId, 1, issueId, RollA, Kg(4_000m), At, Metadata(run));
            // The same id for another roll, even on the version the order now has: refused by content.
            Assert.Equal(ManufacturingStoreCodes.FactIdConflict, (await Assert.ThrowsAsync<ManufacturingStoreException>(
                () => a.Orders.IssueInputAsync(orderId, 2, issueId, RollB, Kg(4_000m), At, Metadata(run)))).Code);
        }

        // The primary key holds even without the store's own check (a racing writer).
        await using (var raw = run.Context(CompanyA))
        {
            raw.Facts.Add(new FactRow
            {
                CompanyId = CompanyA,
                Kind = (int)FactKind.InputIssue,
                Id = issueId,
                OrderId = orderId,
                ParentRollId = RollB,
                MassKg = 1m,
                OccurredAtUtc = At,
                PayloadHash = new string('0', 64),
                RecordedAtUtc = At,
                RecordedBy = "synthetic-actor",
                CorrelationId = run.Correlation,
            });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => raw.SaveChangesAsync());
            Assert.Equal(2627, Assert.IsType<SqlException>(error.InnerException).Number);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Facts"));
        Assert.Equal(4_000m, await run.ScalarAsync<decimal>("SELECT MassKg FROM manufacturing.Facts WHERE Id = @id", ("@id", issueId)));
    }

    [SqlServerFact]
    public async Task Two_writers_that_read_the_same_order_cannot_both_commit()
    {
        await using var run = await Run.StartAsync();
        var (_, orderId) = await run.ApprovedOrderAsync(CompanyA);

        // Both writers rebuild the same order at version 1, then wait for each other at the save.
        var barrier = new Barrier(2);
        async Task<object> WriteAsync(Guid roll)
        {
            await using var writer = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                return await writer.Orders.IssueInputAsync(orderId, 1, Guid.NewGuid(), roll, Kg(1_000m), At, Metadata(run));
            }
            catch (ManufacturingStoreException exception)
            {
                return exception.Code;
            }
        }

        var results = await Task.WhenAll(Task.Run(() => WriteAsync(RollA)), Task.Run(() => WriteAsync(RollB)));

        Assert.Single(results, result => result is ManufacturingWriteOutcome.Recorded);
        Assert.Single(results, result => result is string code && code == ManufacturingStoreCodes.ConcurrentChange);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Facts"));
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM manufacturing.Orders WHERE Id = @id", ("@id", orderId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.input.issued.v1'"));
    }

    [SqlServerFact]
    public async Task A_save_that_fails_in_the_database_leaves_nothing_behind()
    {
        await using var run = await Run.StartAsync();
        var workCenterA = await run.WorkCenterOfAsync(CompanyA);
        var workCenterB = await run.WorkCenterOfAsync(CompanyB);

        // A work center of company B under company A: the composite (company, work center) foreign
        // key refuses it (547), the store names it, and the plan, its inputs and the audit event roll
        // back together. A work center that does not exist at all is refused the same way.
        foreach (var workCenter in new[] { workCenterB, Guid.NewGuid() })
        {
            await using var a = run.Store(CompanyA);
            var error = await Assert.ThrowsAsync<ManufacturingStoreException>(
                () => a.Plans.SavePlanAsync(NewPlan(CompanyA, workCenter), Metadata(run)));
            Assert.Equal(ManufacturingStoreCodes.WorkCenterNotFound, error.Code);
            var database = Assert.IsType<DbUpdateException>(error.InnerException);
            Assert.Equal(547, Assert.IsType<SqlException>(database.InnerException).Number);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Plans"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanInputs"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Manufacturing.ProductionPlan'"));

        // The same plan at the company's own work center then succeeds: nothing was half-written.
        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(ManufacturingWriteOutcome.Recorded,
                await a.Plans.SavePlanAsync(NewPlan(CompanyA, workCenterA), Metadata(run)));
        }
    }

    /// <summary>
    /// MFG-6 slice 2: a slitting spec is plan content in real SQL — stored with the plan in one save, read
    /// back, replayed, and a different spec under the same id refused. Its approval is refused while any
    /// engine input is unknown, naming each one, and nothing changes: no version, no approval, no audit.
    /// </summary>
    [SqlServerFact]
    public async Task A_slitting_spec_is_plan_content_and_its_approval_is_refused_naming_every_missing_input()
    {
        await using var run = await Run.StartAsync();
        var plan = SpecPlan(CompanyA, await run.WorkCenterOfAsync(CompanyA));

        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(ManufacturingWriteOutcome.Recorded, await a.Plans.SavePlanAsync(plan, Metadata(run)));
            Assert.Equal(ManufacturingWriteOutcome.Replayed, await a.Plans.SavePlanAsync(SpecPlan(plan), Metadata(run)));
            Assert.Equal(ManufacturingStoreCodes.PlanIdConflict, (await Assert.ThrowsAsync<ManufacturingStoreException>(
                () => a.Plans.SavePlanAsync(SpecPlan(plan, new SlittingSpec([new SlitLine(150m, 4)], Kg(2_000m))),
                    Metadata(run)))).Code);
        }

        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(plan.Spec, (await a.Plans.FindPlanAsync(plan.Id))!.Spec);
        }

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanSlitLines WHERE PlanId = @p", ("@p", plan.Id)));
        Assert.Equal(2_000m, await run.ScalarAsync<decimal>(
            "SELECT WindingTargetKg FROM manufacturing.Plans WHERE Id = @p", ("@p", plan.Id)));
        var auditsBefore = await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Manufacturing.ProductionPlan' AND EntityId = @p",
            ("@p", plan.Id.ToString()));

        await using (var a = run.Store(CompanyA))
        {
            var refusal = await Assert.ThrowsAsync<ManufacturingRuleException>(
                () => a.Plans.ApprovePlanAsync(plan.Id, 1, run.Actor, At, Metadata(run)));
            Assert.Equal(ManufacturingRuleCodes.PlanParametersUnavailable, refusal.Code);
            Assert.Equal(
                ["densityKgPerCubicMetre", "innerDiameterMm", "labelState", "lineSpeedMetresPerMinute", "maximumOuterDiameterMm",
                    "netUsableWidthMm", "parentWidthMm", "thicknessMm", "toleranceMetres"],
                refusal.Parameters);
        }

        Assert.Equal((1, 1L), (await run.ScalarAsync<int>("SELECT Status FROM manufacturing.Plans WHERE Id = @p", ("@p", plan.Id)),
            await run.ScalarAsync<long>("SELECT Version FROM manufacturing.Plans WHERE Id = @p", ("@p", plan.Id))));
        Assert.Equal(auditsBefore, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Manufacturing.ProductionPlan' AND EntityId = @p",
            ("@p", plan.Id.ToString())));

        // Another company neither finds the plan nor sees its lines.
        await using (var b = run.Store(CompanyB))
        {
            Assert.Null(await b.Plans.FindPlanAsync(plan.Id));
        }

        await using (var context = run.Context(CompanyB))
        {
            Assert.Equal(0, await context.PlanSlitLines.CountAsync());
        }
    }

    /// <summary>
    /// MFG-6 slice 2's migration Down on real SQL: with no spec stored it rolls back and forward cleanly;
    /// with a stored spec it refuses (THROW 51047, Manufacturing's module number) and keeps everything.
    /// </summary>
    [SqlServerFact]
    public async Task The_slitting_spec_migration_rolls_back_only_while_no_spec_is_stored()
    {
        await using var run = await Run.StartAsync();
        const string beforeSpecs = "20260918230000_AddManufacturingCommands";

        await using (var context = run.Context(CompanyA))
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(beforeSpecs);
            Assert.Equal(beforeSpecs, (await context.Database.GetAppliedMigrationsAsync()).Last());
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE [name] = N'PlanSlitLines' AND [schema_id] = SCHEMA_ID(N'manufacturing')"));
            await context.Database.MigrateAsync();
        }

        var plan = SpecPlan(CompanyA, await run.WorkCenterOfAsync(CompanyA));
        await using (var a = run.Store(CompanyA))
        {
            await a.Plans.SavePlanAsync(plan, Metadata(run));
        }

        await using (var context = run.Context(CompanyA))
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(beforeSpecs));
            Assert.Equal(51047, error.Number);
            Assert.EndsWith("_AddPlanSlittingSpecs", (await context.Database.GetAppliedMigrationsAsync()).Last(),
                StringComparison.Ordinal);
        }

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanSlitLines WHERE PlanId = @p", ("@p", plan.Id)));
    }

    /// <summary>
    /// DEC-PRDCT-001's discriminator expansion is reversible while all durable facts still use the
    /// original Slitting value. Existing plan/order rows survive both Down and the following Up.
    /// </summary>
    [SqlServerFact]
    public async Task The_process_constraint_migration_rolls_back_and_reapplies_while_only_slitting_is_stored()
    {
        await using var run = await Run.StartAsync();
        const string beforeProcesses = "20260919130000_AddPlanSlittingSpecs";
        var expandedPlanConstraint = await run.ScalarAsync<string>(
            "SELECT [definition] FROM sys.check_constraints WHERE [name] = N'CK_Plans_Enums' "
            + "AND [parent_object_id] = OBJECT_ID(N'[manufacturing].[Plans]')");
        var expandedOrderConstraint = await run.ScalarAsync<string>(
            "SELECT [definition] FROM sys.check_constraints WHERE [name] = N'CK_Orders_Enums' "
            + "AND [parent_object_id] = OBJECT_ID(N'[manufacturing].[Orders]')");

        // An empty Manufacturing store travels Down and Up cleanly.
        await using (var context = run.Context(CompanyA))
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(beforeProcesses);
            Assert.Equal(beforeProcesses, (await context.Database.GetAppliedMigrationsAsync()).Last());
            Assert.NotEqual(expandedPlanConstraint, await run.ScalarAsync<string>(
                "SELECT [definition] FROM sys.check_constraints WHERE [name] = N'CK_Plans_Enums' "
                + "AND [parent_object_id] = OBJECT_ID(N'[manufacturing].[Plans]')"));
            Assert.NotEqual(expandedOrderConstraint, await run.ScalarAsync<string>(
                "SELECT [definition] FROM sys.check_constraints WHERE [name] = N'CK_Orders_Enums' "
                + "AND [parent_object_id] = OBJECT_ID(N'[manufacturing].[Orders]')"));
            await context.Database.MigrateAsync();
            Assert.EndsWith("_ExpandManufacturingProcesses", (await context.Database.GetAppliedMigrationsAsync()).Last(),
                StringComparison.Ordinal);
            Assert.Equal(expandedPlanConstraint, await run.ScalarAsync<string>(
                "SELECT [definition] FROM sys.check_constraints WHERE [name] = N'CK_Plans_Enums' "
                + "AND [parent_object_id] = OBJECT_ID(N'[manufacturing].[Plans]')"));
            Assert.Equal(expandedOrderConstraint, await run.ScalarAsync<string>(
                "SELECT [definition] FROM sys.check_constraints WHERE [name] = N'CK_Orders_Enums' "
                + "AND [parent_object_id] = OBJECT_ID(N'[manufacturing].[Orders]')"));
        }

        // Slitting-only durable rows also survive the round trip unchanged.
        var (plan, orderId) = await run.ApprovedOrderAsync(CompanyA);
        await using (var context = run.Context(CompanyA))
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(beforeProcesses);
            Assert.Equal(beforeProcesses, (await context.Database.GetAppliedMigrationsAsync()).Last());
            Assert.Equal((1, 1),
                (await run.ScalarAsync<int>("SELECT Process FROM manufacturing.Plans WHERE Id = @id", ("@id", plan.Id)),
                    await run.ScalarAsync<int>("SELECT Process FROM manufacturing.Orders WHERE Id = @id", ("@id", orderId))));
            await context.Database.MigrateAsync();
            Assert.EndsWith("_ExpandManufacturingProcesses", (await context.Database.GetAppliedMigrationsAsync()).Last(),
                StringComparison.Ordinal);
        }

        Assert.Equal((1, 1),
            (await run.ScalarAsync<int>("SELECT Process FROM manufacturing.Plans WHERE Id = @id", ("@id", plan.Id)),
                await run.ScalarAsync<int>("SELECT Process FROM manufacturing.Orders WHERE Id = @id", ("@id", orderId))));
    }

    /// <summary>
    /// A non-Slitting plan or order makes Down a forward-fix refusal. The 51047 guard runs before
    /// either constraint changes and before migration history moves.
    /// </summary>
    [SqlServerFact]
    public async Task The_process_constraint_migration_refuses_down_before_mutation_for_each_non_slitting_table()
    {
        await using var run = await Run.StartAsync();
        const string beforeProcesses = "20260919130000_AddPlanSlittingSpecs";
        var workCenter = await run.WorkCenterOfKindAsync(CompanyA, "ColdRolling");
        var plan = NewPlan(CompanyA, workCenter, ManufacturingProcess.ColdRolling);
        var orderId = Guid.NewGuid();

        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(plan, Metadata(run));
        }

        var planConstraint = await run.ScalarAsync<string>(
            "SELECT [definition] FROM sys.check_constraints WHERE [name] = N'CK_Plans_Enums' "
            + "AND [parent_object_id] = OBJECT_ID(N'[manufacturing].[Plans]')");
        var orderConstraint = await run.ScalarAsync<string>(
            "SELECT [definition] FROM sys.check_constraints WHERE [name] = N'CK_Orders_Enums' "
            + "AND [parent_object_id] = OBJECT_ID(N'[manufacturing].[Orders]')");

        async Task AssertDownRefusedBeforeMutation()
        {
            await using var context = run.Context(CompanyA);
            var error = await Assert.ThrowsAsync<SqlException>(
                () => context.GetService<IMigrator>().MigrateAsync(beforeProcesses));
            Assert.Equal(51047, error.Number);
            Assert.EndsWith("_ExpandManufacturingProcesses", (await context.Database.GetAppliedMigrationsAsync()).Last(),
                StringComparison.Ordinal);
            Assert.Equal(planConstraint, await run.ScalarAsync<string>(
                "SELECT [definition] FROM sys.check_constraints WHERE [name] = N'CK_Plans_Enums' "
                + "AND [parent_object_id] = OBJECT_ID(N'[manufacturing].[Plans]')"));
            Assert.Equal(orderConstraint, await run.ScalarAsync<string>(
                "SELECT [definition] FROM sys.check_constraints WHERE [name] = N'CK_Orders_Enums' "
                + "AND [parent_object_id] = OBJECT_ID(N'[manufacturing].[Orders]')"));
        }

        // Plans alone block the downgrade.
        await AssertDownRefusedBeforeMutation();
        Assert.Equal(ManufacturingProcess.ColdRolling, (ManufacturingProcess)await run.ScalarAsync<int>(
            "SELECT Process FROM manufacturing.Plans WHERE Id = @id", ("@id", plan.Id)));

        // Then isolate the Orders predicate: create the matching order and make only its snapshot non-Slitting.
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.ApprovePlanAsync(plan.Id, 1, run.Actor, At, Metadata(run));
            await store.Orders.CreateOrderAsync(orderId, plan.Id, Metadata(run));
        }

        await using (var context = run.Context(CompanyA))
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [manufacturing].[Plans] SET [Process] = 1 WHERE [Id] = {plan.Id}");
        }

        await AssertDownRefusedBeforeMutation();
        Assert.Equal((1, (int)ManufacturingProcess.ColdRolling),
            (await run.ScalarAsync<int>("SELECT Process FROM manufacturing.Plans WHERE Id = @id", ("@id", plan.Id)),
                await run.ScalarAsync<int>("SELECT Process FROM manufacturing.Orders WHERE Id = @id", ("@id", orderId))));
    }

    /// <summary>A plan with the §28.4 golden slit lines and a winding target, on one roll of 28.750 kg.</summary>
    private static ProductionPlan SpecPlan(Guid company, Guid workCenter) =>
        new(Guid.NewGuid(), company, workCenter, ManufacturingProcess.Slitting, [new PlannedInput(RollA, Kg(28_750m))],
            target: null, new SlittingSpec([new SlitLine(150m, 4), new SlitLine(240m, 3)], Kg(2_000m)));

    /// <summary>The same plan (id, company, work center, inputs), with its own spec or another one.</summary>
    private static ProductionPlan SpecPlan(ProductionPlan plan, SlittingSpec? spec = null) =>
        new(plan.Id, plan.CompanyId, plan.WorkCenterId, plan.Process, plan.Inputs, plan.Target, spec ?? plan.Spec);

    private static Kilograms Kg(decimal value) => new(value);

    private static ProductionPlan NewPlan(
        Guid company,
        Guid workCenter,
        ManufacturingProcess process = ManufacturingProcess.Slitting) =>
        new(Guid.NewGuid(), company, workCenter, process,
            [new PlannedInput(RollA, Kg(10_000m)), new PlannedInput(RollB, Kg(5_000m))],
            new ProductionTarget(TargetBasis.InputMass, Kg(15_000m)));

    /// <summary>The draft form of a plan, to replay its first save.</summary>
    private static ProductionPlan Draft(ProductionPlan plan) =>
        new(plan.Id, plan.CompanyId, plan.WorkCenterId, plan.Process, plan.Inputs, plan.Target);

    private static ManufacturingCommandMetadata Metadata(Run run) => new(
        run.Actor.ToString("D"), "synthetic.manufacturing.test-write", ["SYNTHETIC-MANUFACTURING-TEST"], run.Correlation,
        CausationId: null, IdempotencyKey: Guid.NewGuid().ToString("N"), SourceChannel: "Web");

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public Guid Actor { get; } = Guid.NewGuid();
        public string Correlation { get; } = $"mfg5-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            // The host migrates and seeds Organization, Audit and the rest; Manufacturing's own
            // migration runs after it, as its foreign keys need Organization's tables.
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            await using var context = run.Context(CompanyA);
            await context.Database.MigrateAsync();
            return run;
        }

        /// <summary>A stored plan of the company's first work center, approved, and an order from it.</summary>
        public async Task<(ProductionPlan Plan, Guid OrderId)> ApprovedOrderAsync(Guid company)
        {
            var plan = NewPlan(company, await WorkCenterOfAsync(company));
            var orderId = Guid.NewGuid();
            await using var store = Store(company);
            await store.Plans.SavePlanAsync(plan, Metadata(this));
            var approved = await store.Plans.ApprovePlanAsync(plan.Id, 1, Actor, At, Metadata(this));
            await store.Orders.CreateOrderAsync(orderId, plan.Id, Metadata(this));
            return (approved, orderId);
        }

        public ManufacturingDbContext Context(Guid company, params IInterceptor[] interceptors)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var options = new DbContextOptionsBuilder<ManufacturingDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", ManufacturingDbContext.Schema))
                .AddInterceptors(interceptors)
                .Options;
            return new ManufacturingDbContext(options, companyContext);
        }

        public StoreScope Store(Guid company, params IInterceptor[] extra)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var interceptors = new List<IInterceptor> { new AuditSaveChangesInterceptor(collector, companyContext) };
            interceptors.AddRange(extra);
            var options = new DbContextOptionsBuilder<ManufacturingDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", ManufacturingDbContext.Schema))
                .AddInterceptors(interceptors)
                .Options;
            var dbContext = new ManufacturingDbContext(options, companyContext);
            var store = new ManufacturingStore(dbContext, companyContext, collector, TimeProvider.System);
            return new StoreScope(dbContext, store);
        }

        public async Task<Guid> WorkCenterOfAsync(Guid company) =>
            await ScalarAsync<Guid>("SELECT TOP (1) Id FROM organization.WorkCenters WHERE CompanyId = @c ORDER BY Id",
                ("@c", company));

        public async Task<Guid> WorkCenterOfKindAsync(Guid company, string kind) =>
            await ScalarAsync<Guid>(
                "SELECT TOP (1) Id FROM organization.WorkCenters WHERE CompanyId = @c AND Kind = @kind ORDER BY Id",
                ("@c", company), ("@kind", kind));

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class StoreScope(ManufacturingDbContext dbContext, ManufacturingStore store) : IAsyncDisposable
    {
        public IManufacturingPlanStore Plans => store;
        public IManufacturingOrderStore Orders => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    /// <summary>Holds each save until both writers have reached it, so both read the same order first.</summary>
    private sealed class BarrierInterceptor(Barrier barrier) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return ValueTask.FromResult(result);
        }
    }
}
