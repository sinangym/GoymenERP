using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D252..SAT-D255 (EK50) / API-11 against a REAL SQL Server and the REAL host composition: a source plan is stored in one save
/// through the real Sales store (plan rows at the new version, header version, attempt and audit with the linked-supply
/// confirmation), the same key replays and a different payload under it is refused; the identity check holds the ONE equality;
/// another company neither writes nor sees a plan; the REAL draft service over the real store refuses a sum mismatch, a fully
/// delivered line (recorded, replayed), an unreadable allocation or link, an unconfirmed open link and a stale version (recorded,
/// replayed), saves a partially delivered line's remainder, and refuses without the write permission; the migration refuses to
/// roll back over a recorded plan (THROW 51241, forward fix). The owners are SYNTHETIC ports. SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesDraftSourcePlanSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 10, 26);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("5b8f1d22-6e3c-4d9f-8a21-3c4d5e6f7b01");
    private static readonly Guid MoneyProfile = Guid.Parse("5b8f1d22-6e3c-4d9f-8a21-3c4d5e6f7b02");
    private static readonly Guid KgProfile = Guid.Parse("5b8f1d22-6e3c-4d9f-8a21-3c4d5e6f7b03");
    private static readonly Guid Product = Guid.Parse("5b8f1d22-6e3c-4d9f-8a21-3c4d5e6f7b04");
    private const string Planner = "SENTETIK-planner";

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");
    private static readonly SalesDraftStateAudit PlanRecorded =
        new("sales.order-draft.source-plan-recorded.v1", "RecordSourcePlan", "SENTETIK kaynak planı kaydedildi");

    [SqlServerFact]
    public async Task The_source_plan_migrations_Down_passes_with_no_plan_and_refuses_while_a_plan_exists()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "100");
        var line = Assert.Single(draft.Lines);
        var guard = Assert.IsType<SqlOperation>(new AddDraftSourcePlanLines().DownOperations[0]).Sql;
        // No plan: the guard passes (an empty table rolls back freely).
        await run.ScalarAsync<int>(guard + " SELECT 1");

        await using (var scope = run.SalesStore(CompanyA))
        {
            await RecordAsync(scope, run.SalesEnvelope("k-plan", Planner), "k-plan", draft.Id, Plan(line.Id, 0m, 30m, 50m, 20m, 0m, false));
        }

        var error = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + " SELECT 1"));
        Assert.Equal(51241, error.Number);
        await using var context = run.SalesContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var planIndex = Array.FindIndex(applied, id => id.EndsWith("_AddDraftSourcePlanLines", StringComparison.Ordinal));
        Assert.True(planIndex > 0);
        // Whatever migrations follow the plan one are rolled back first (they hold no data here), so the guard under test is the next Down.
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(applied[planIndex]);
        Assert.Equal(applied[..(planIndex + 1)], await context.Database.GetAppliedMigrationsAsync());
        var rollback = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(applied[planIndex - 1]));
        Assert.Equal(51241, rollback.Number);
        Assert.Equal(applied[..(planIndex + 1)], await context.Database.GetAppliedMigrationsAsync());
        // Forward again: the later migrations are re-applied and the plan row survived the refused Down.
        await context.Database.MigrateAsync();
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal((1, 5L), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftSourcePlanLines] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [DraftVersion] FROM [sales].[DraftSourcePlanLines] WHERE [DraftId] = @id", ("@id", draft.Id))));
    }

    [SqlServerFact]
    public async Task A_source_plan_is_stored_in_one_save_with_its_attempt_and_audit_and_the_same_key_replays_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "100");
        var line = Assert.Single(draft.Lines);
        var attemptsBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts]");
        var envelope = run.SalesEnvelope("k-plan", Planner);
        var supply = Guid.NewGuid();
        var input = new SalesSourcePlanWriteInput(new SourcePlanInput([new SourcePlanLineInput(line.Id, 10m, 30m, 40m, 20m, 0m)], true),
            [new SalesSourcePlanLinkedItem(SalesLinkedSupplyKindsV1.Production, SalesLinkedSupplyDocumentTypesV1.ProductionOrder, supply, null, line.Id)]);

        await using (var scope = run.SalesStore(CompanyA))
        {
            var recorded = await RecordAsync(scope, envelope, "k-plan", draft.Id, input);
            Assert.Equal((SalesDraftWriteOutcome.Recorded, 5L), (recorded!.Outcome, recorded.Draft.Version));
        }

        // One save: the plan row, header version + 1, the attempt and the audit event (with the confirmation and the link) are all there.
        Assert.Equal((1, 5L), (await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftSourcePlanLines] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));
        Assert.Equal((10m, 30m, 40m, 20m, 0m, true), (
            await run.ScalarAsync<decimal>("SELECT [LockedQuantity] FROM [sales].[DraftSourcePlanLines] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<decimal>("SELECT [StockQuantity] FROM [sales].[DraftSourcePlanLines] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<decimal>("SELECT [ProductionQuantity] FROM [sales].[DraftSourcePlanLines] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<decimal>("SELECT [PurchasingQuantity] FROM [sales].[DraftSourcePlanLines] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<decimal>("SELECT [SubcontractQuantity] FROM [sales].[DraftSourcePlanLines] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<bool>("SELECT [LinkedSupplyConfirmed] FROM [sales].[DraftSourcePlanLines] WHERE [DraftId] = @id", ("@id", draft.Id))));
        Assert.Equal(attemptsBefore + 1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts]"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = @command AND [Outcome] = 1 AND [DraftId] = @id",
            ("@command", SalesDraftCommands.RecordSourcePlanV1), ("@id", draft.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'sales.order-draft.source-plan-recorded.v1' AND [EntityId] = @id "
            + "AND [EntityVersionBefore] = 4 AND [EntityVersionAfter] = 5 AND [AfterJson] LIKE N'%\"linkedSupplyConfirmed\":true%' "
            + "AND [AfterJson] LIKE N'%' + @supply + N'%'",
            ("@id", draft.Id.ToString("D")), ("@supply", supply.ToString("D"))));

        // The store reads the recorded plan back as the domain's facts.
        await using (var reader = run.SalesStore(CompanyA))
        {
            var fact = Assert.Single(await reader.Store.FindSourcePlanAsync(draft.Id));
            Assert.Equal((line.Id, 100m, 10m, 30m, 40m, 20m, 0m, true, Planner, 5L),
                (fact.LineId, fact.LineQuantity, fact.Locked, fact.Stock, fact.Production, fact.Purchasing, fact.Subcontract,
                    fact.LinkedSupplyConfirmed, fact.SavedBy, fact.DraftVersion));
        }

        // The same key and content replays the stored answer and writes nothing again.
        await using (var scope = run.SalesStore(CompanyA))
        {
            var replay = await scope.Store.RecordSourcePlanAsync(envelope, Hash("k-plan"), SalesDraftCommands.RecordSourcePlanV1, draft.Id,
                (_, _, _) => throw new InvalidOperationException("A replay does not mutate."), PlanRecorded,
                (_, _) => throw new InvalidOperationException("A replay asks no owner."));
            Assert.Equal(SalesDraftWriteOutcome.Replayed, replay!.Outcome);
        }

        Assert.Equal((1, attemptsBefore + 1),
            (await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftSourcePlanLines] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts]")));

        // The same key under different content is the payload mismatch, never a second plan.
        await using (var other = run.SalesStore(CompanyA))
        {
            var mismatch = await Assert.ThrowsAsync<SalesDraftStoreException>(() =>
                RecordAsync(other, envelope, "k-other", draft.Id, Plan(line.Id, 0m, 0m, 100m, 0m, 0m, false), expectedVersion: 5));
            Assert.Equal(SalesDraftStoreCodes.SameKeyPayloadMismatch, mismatch.Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftSourcePlanLines] WHERE [DraftId] = @id", ("@id", draft.Id)));
    }

    [SqlServerFact]
    public async Task The_database_refuses_an_unbalanced_plan_row_a_fully_locked_row_and_a_row_of_another_company()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "100");
        var line = Assert.Single(draft.Lines);
        const string insert = "INSERT INTO [sales].[DraftSourcePlanLines] ([CompanyId],[DraftId],[DraftVersion],[LineId],[LineNo],[LineQuantity],"
            + "[LockedQuantity],[StockQuantity],[ProductionQuantity],[PurchasingQuantity],[SubcontractQuantity],[LinkedSupplyConfirmed],[SavedBy],"
            + "[SavedAtUtc],[CorrelationId]) VALUES (@company, @id, 5, @line, @lineNo, 100, @locked, 0, @production, 0, 0, 0, N'x', "
            + "SYSDATETIMEOFFSET(), N'x'); SELECT 1";

        // The ONE equality (SAT-D252): 90 of 100 is refused by the identity check.
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(insert, ("@company", CompanyA), ("@id", draft.Id),
            ("@line", line.Id), ("@lineNo", line.LineNo), ("@locked", 0m), ("@production", 90m)))).Number);
        // A fully delivered line has no plan (SAT-D253): locked equal to the quantity is refused.
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(insert, ("@company", CompanyA), ("@id", draft.Id),
            ("@line", line.Id), ("@lineNo", line.LineNo), ("@locked", 100m), ("@production", 0m)))).Number);
        // Another company's row cannot name company A's order line.
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(insert, ("@company", CompanyB), ("@id", draft.Id),
            ("@line", line.Id), ("@lineNo", line.LineNo), ("@locked", 0m), ("@production", 100m)))).Number);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftSourcePlanLines] WHERE [DraftId] = @id", ("@id", draft.Id)));

        // The balanced row is stored.
        await run.ScalarAsync<int>(insert, ("@company", CompanyA), ("@id", draft.Id), ("@line", line.Id), ("@lineNo", line.LineNo),
            ("@locked", 40m), ("@production", 60m));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftSourcePlanLines] WHERE [DraftId] = @id", ("@id", draft.Id)));
    }

    [SqlServerFact]
    public async Task Another_company_cannot_record_a_source_plan_and_the_plan_is_invisible_to_it_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "100");
        var line = Assert.Single(draft.Lines);

        // Company B's store does not find company A's order: the command answers null (the service's 404) and writes nothing.
        await using (var other = run.SalesStore(CompanyB))
        {
            Assert.Null(await RecordAsync(other, run.SalesEnvelope("k-foreign", Planner), "k-foreign", draft.Id,
                Plan(line.Id, 0m, 0m, 100m, 0m, 0m, false)));
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftSourcePlanLines]"));

        await using (var owner = run.SalesStore(CompanyA))
        {
            await RecordAsync(owner, run.SalesEnvelope("k-own", Planner), "k-own", draft.Id, Plan(line.Id, 0m, 0m, 100m, 0m, 0m, false));
        }

        Assert.Equal(CompanyA, await run.ScalarAsync<Guid>("SELECT [CompanyId] FROM [sales].[DraftSourcePlanLines] WHERE [DraftId] = @id", ("@id", draft.Id)));
        await using (var reader = run.SalesStore(CompanyB))
        {
            Assert.Empty(await reader.Store.FindSourcePlanAsync(draft.Id));
            Assert.Null(await reader.Store.FindDraftAsync(draft.Id));
        }

        // Through the REAL service company B's read and save are both 404 (the same answer as an absent order).
        var owners = new Owners();
        await using var scopeB = run.SalesStore(CompanyB);
        var serviceB = run.SalesService(scopeB.Store, CompanyB, owners, Grants.ReadAndWrite);
        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => serviceB.GetSourcePlanAsync(draft.Id))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => serviceB.RecordSourcePlanAsync(draft.Id,
            Request("4", line.Id, "100"), Metadata("k-b")))).Status);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftSourcePlanLines]"));
    }

    [SqlServerFact]
    public async Task The_service_saves_the_remainder_of_a_partially_delivered_line_and_subtracts_its_own_allocation_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "100");
        var line = Assert.Single(draft.Lines);
        // 40 of 100 kg shipped, 25 kg of the line's own allocation: the user enters only the 35 kg that is left.
        var owners = new Owners { Shipped = 40m, Allocated = 25m };
        await using (var scope = run.SalesStore(CompanyA))
        {
            var service = run.SalesService(scope.Store, CompanyA, owners, Grants.ReadAndWrite);
            var before = await service.GetSourcePlanAsync(draft.Id);
            var row = Assert.Single(before.Lines);
            Assert.Equal(("4", true, true, "40", "60", SalesSourcePlanStockStatusesV1.Derived, "25"),
                (before.Version, before.Writable, row.Writable, row.Locked, row.Remaining, row.StockStatus, row.Stock));
            Assert.Contains(SalesSourcePlanWarningsV1.NoPlan, row.Warnings);

            // A sum that misses the ONE equality is 422 and writes nothing (not recorded).
            var mismatch = await Assert.ThrowsAsync<SalesDraftServiceException>(() => service.RecordSourcePlanAsync(draft.Id,
                Request("4", line.Id, "60"), Metadata("k-sum")));
            Assert.Equal((422, SalesDraftProblemCodesV1.SourcePlanSumMismatch), (mismatch.Status, mismatch.Code));
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftSourcePlanLines]"));

            var saved = await service.RecordSourcePlanAsync(draft.Id, Request("4", line.Id, "35"), Metadata("k-plan"));
            Assert.Equal(("5", false), (saved.Detail.Draft.Version, saved.IsReplay));
        }

        Assert.Equal((40m, 25m, 35m, false), (
            await run.ScalarAsync<decimal>("SELECT [LockedQuantity] FROM [sales].[DraftSourcePlanLines] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<decimal>("SELECT [StockQuantity] FROM [sales].[DraftSourcePlanLines] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<decimal>("SELECT [ProductionQuantity] FROM [sales].[DraftSourcePlanLines] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<bool>("SELECT [LinkedSupplyConfirmed] FROM [sales].[DraftSourcePlanLines] WHERE [DraftId] = @id", ("@id", draft.Id))));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = @command AND [Outcome] = 2", ("@command", SalesDraftCommands.RecordSourcePlanV1)));

        // The next read shows the recorded plan; a later allocation change is a mismatch warning, never a rewrite.
        owners.Allocated = 30m;
        await using (var scope = run.SalesStore(CompanyA))
        {
            var after = await run.SalesService(scope.Store, CompanyA, owners, Grants.ReadAndWrite).GetSourcePlanAsync(draft.Id);
            var row = Assert.Single(after.Lines);
            Assert.Equal(("5", "5", "35"), (after.Version, row.Saved!.DraftVersion, row.Saved.Production));
            Assert.DoesNotContain(SalesSourcePlanWarningsV1.NoPlan, row.Warnings);
            Assert.Contains(SalesSourcePlanWarningsV1.PlanAllocationMismatch, row.Warnings);
        }
    }

    [SqlServerFact]
    public async Task The_service_records_a_fully_delivered_line_refusal_and_a_stale_version_and_replays_both_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "100");
        var line = Assert.Single(draft.Lines);
        var owners = new Owners { Shipped = 100m };
        await using var scope = run.SalesStore(CompanyA);
        var service = run.SalesService(scope.Store, CompanyA, owners, Grants.ReadAndWrite);

        // SAT-D253: a fully delivered line refuses (recorded terminal 409); the same key replays the refusal.
        var locked = await Assert.ThrowsAsync<SalesDraftServiceException>(() => service.RecordSourcePlanAsync(draft.Id,
            Request("4", line.Id, "0"), Metadata("k-delivered")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SourcePlanLineLocked, false), (locked.Status, locked.Code, locked.IsReplay));
        var lockedAgain = await Assert.ThrowsAsync<SalesDraftServiceException>(() => service.RecordSourcePlanAsync(draft.Id,
            Request("4", line.Id, "0"), Metadata("k-delivered")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SourcePlanLineLocked, true), (lockedAgain.Status, lockedAgain.Code, lockedAgain.IsReplay));

        // A stale version is a recorded 409 version conflict; the same key replays it.
        owners.Shipped = 0m;
        var stale = await Assert.ThrowsAsync<SalesDraftServiceException>(() => service.RecordSourcePlanAsync(draft.Id,
            Request("3", line.Id, "100"), Metadata("k-stale")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict, false), (stale.Status, stale.Code, stale.IsReplay));
        var staleAgain = await Assert.ThrowsAsync<SalesDraftServiceException>(() => service.RecordSourcePlanAsync(draft.Id,
            Request("3", line.Id, "100"), Metadata("k-stale")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict, true), (staleAgain.Status, staleAgain.Code, staleAgain.IsReplay));

        Assert.Equal((0, 4L, 2), (await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftSourcePlanLines]"),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = @command AND [Outcome] = 2",
                ("@command", SalesDraftCommands.RecordSourcePlanV1))));
    }

    [SqlServerFact]
    public async Task The_service_refuses_an_unreadable_owner_and_an_unconfirmed_link_and_records_the_confirmed_link_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "100");
        var line = Assert.Single(draft.Lines);
        var supply = Guid.NewGuid();
        var owners = new Owners { AllocationUnreadable = true };
        await using var scope = run.SalesStore(CompanyA);
        var service = run.SalesService(scope.Store, CompanyA, owners, Grants.ReadAndWrite);

        // SAT-D255: an unreadable allocation is shown as unreadable (never zero) and refuses the save (503, not recorded).
        var read = Assert.Single((await service.GetSourcePlanAsync(draft.Id)).Lines);
        Assert.Equal((SalesSourcePlanStockStatusesV1.Unreadable, (string?)null, false), (read.StockStatus, read.Stock, read.Writable));
        var allocation = await Assert.ThrowsAsync<SalesDraftServiceException>(() => service.RecordSourcePlanAsync(draft.Id,
            Request("4", line.Id, "100"), Metadata("k-allocation")));
        Assert.Equal((503, SalesDraftProblemCodesV1.SourcePlanAllocationUnavailable), (allocation.Status, allocation.Code));

        // SAT-D254: an unreadable link refuses (503, not recorded).
        owners.AllocationUnreadable = false;
        owners.LinkedUnreadable = true;
        var link = await Assert.ThrowsAsync<SalesDraftServiceException>(() => service.RecordSourcePlanAsync(draft.Id,
            Request("4", line.Id, "100"), Metadata("k-link")));
        Assert.Equal((503, SalesDraftProblemCodesV1.SourcePlanLinkedSupplyUnavailable), (link.Status, link.Code));

        // An open link on the line needs the explicit confirmation (409, not recorded) ...
        owners.LinkedUnreadable = false;
        owners.OpenSupply = new SalesOrderLinkedSupplyItemV1(supply, Guid.NewGuid(), SalesLinkedSupplyDocumentTypesV1.ProductionOrder, "SENTETIK-1",
            line.Id, "Approved", "50", KgUnit, null, null);
        var unconfirmed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => service.RecordSourcePlanAsync(draft.Id,
            Request("4", line.Id, "100"), Metadata("k-confirm")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SourcePlanLinkedSupplyUnconfirmed), (unconfirmed.Status, unconfirmed.Code));
        Assert.Equal((0, 0), (await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftSourcePlanLines]"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = @command",
                ("@command", SalesDraftCommands.RecordSourcePlanV1))));

        // ... and the confirmed save under the same key records the plan, the confirmation and the link it covered in the audit.
        var saved = await service.RecordSourcePlanAsync(draft.Id, Request("4", line.Id, "100", confirm: true), Metadata("k-confirm"));
        Assert.Equal("5", saved.Detail.Draft.Version);
        Assert.True(await run.ScalarAsync<bool>("SELECT [LinkedSupplyConfirmed] FROM [sales].[DraftSourcePlanLines] WHERE [DraftId] = @id", ("@id", draft.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'sales.order-draft.source-plan-recorded.v1' AND [EntityId] = @id "
            + "AND [AfterJson] LIKE N'%\"linkedSupplyConfirmed\":true%' AND [AfterJson] LIKE N'%' + @supply + N'%'",
            ("@id", draft.Id.ToString("D")), ("@supply", supply.ToString("D"))));
    }

    [SqlServerFact]
    public async Task The_service_refuses_a_source_plan_save_without_the_write_permission_and_writes_nothing_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "100");
        var line = Assert.Single(draft.Lines);
        var owners = new Owners();
        await using var scope = run.SalesStore(CompanyA);
        // The draft read alone shows the plan (the read is under the draft read) but cannot save it.
        var service = run.SalesService(scope.Store, CompanyA, owners, Grants.ReadOnly);
        Assert.Single((await service.GetSourcePlanAsync(draft.Id)).Lines);
        var forbidden = await Assert.ThrowsAsync<SalesDraftServiceException>(() => service.RecordSourcePlanAsync(draft.Id,
            Request("4", line.Id, "100"), Metadata("k-forbidden")));
        Assert.Equal(403, forbidden.Status);
        Assert.Equal((0, 0, 4L), (await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftSourcePlanLines]"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = @command",
                ("@command", SalesDraftCommands.RecordSourcePlanV1)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));
    }

    private static Task<SalesDraftStateResult?> RecordAsync(SalesScope scope, SalesDraftWriteEnvelope envelope, string hashSeed, Guid draftId,
        SalesSourcePlanWriteInput input, long expectedVersion = 4) =>
        scope.Store.RecordSourcePlanAsync(envelope, Hash(hashSeed), SalesDraftCommands.RecordSourcePlanV1, draftId,
            (d, plan, now) => d.RecordSourcePlan(expectedVersion, envelope.ActorId, plan, now), PlanRecorded, (_, _) => Task.FromResult(input));

    private static SalesSourcePlanWriteInput Plan(Guid lineId, decimal locked, decimal stock, decimal production, decimal purchasing,
        decimal subcontract, bool confirmed) =>
        new(new SourcePlanInput([new SourcePlanLineInput(lineId, locked, stock, production, purchasing, subcontract)], confirmed), []);

    private static SalesDraftSourcePlanRequest Request(string version, Guid lineId, string production, bool confirm = false) =>
        new(version, confirm, [new SalesDraftSourcePlanLineRequestV1(lineId, production, "0", "0")]);

    private static SalesCommandMetadata Metadata(string key) =>
        new(key, "sales-source-plan-" + key, null, new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.Zero));

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A one-line draft (the given kg quantity) from SYNTHETIC owner snapshots in the approved kg unit: version 2.</summary>
    private static SalesOrderDraft Draft(Guid company, string documentNo, string quantity)
    {
        var header = new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), Currency, Day, Day.AddDays(10), DueTerms: TestDueTerms.PointDue30);
        var money = new MoneyProfileSnapshot(company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, money, T0);
        var unit = new UnitProfileSnapshot(company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, quantity,
            decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), Day.AddDays(3)), T0);
        return draft;
    }

    private enum Grants
    {
        ReadOnly,
        ReadAndWrite,
    }

    private sealed class Authority(Grants grants) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(permission == SalesPermissionManifestV1.DraftsRead
                || (grants == Grants.ReadAndWrite && permission == SalesPermissionManifestV1.OrdersSourcePlanWrite)
                ? new PermissionScope(true, true, new HashSet<string>(["SENTETIK-SALES-PLANNER"], StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
    }

    private sealed class FixedUser(string userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([company]);
        public bool CanAccessCompany(Guid companyId) => companyId == company;
    }

    private sealed class NoActiveRoles : IActiveRoleCodeReader
    {
        public Task<IReadOnlySet<string>> GetActiveRoleCodesAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));
    }

    private sealed class FixedCorrelation(string correlationId) : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => correlationId;
        public string? CausationId => null;
        public string SourceChannel => "Web";
        public string? ClientIpAddress => "127.0.0.1";
        public string? UserAgent => "SENTETIK-agent";
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent)
        {
        }
    }

    /// <summary>The owner ports a source plan never asks; reaching one is a failure of the case.</summary>
    private sealed class NoOwners : ISalesPartyAccountPortV1, ISettingsPrerequisiteProvider
    {
        public Task<SalesPartyAccountAnswerV1> GetAuthorizedAsync(Guid partyId, Guid companyAccountId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A source plan asks no such owner.");
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A source plan asks no such owner.");
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A source plan asks no such owner.");
        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A source plan asks no such owner.");
    }

    /// <summary>
    /// The SYNTHETIC delivery, allocation and production-supply owners (Logistics', Inventory's and Manufacturing's roles through the
    /// Sales-declared ports): answer what the case sets, for the asked order only, in the server-resolved company.
    /// </summary>
    private sealed class Owners
    {
        public decimal Shipped { get; set; }
        public decimal Allocated { get; set; }
        public bool AllocationUnreadable { get; set; }
        public bool LinkedUnreadable { get; set; }
        public SalesOrderLinkedSupplyItemV1? OpenSupply { get; set; }
    }

    private sealed class Deliveries(Owners owners, Guid company, Func<Guid, IReadOnlyList<Guid>> lines) : ISalesOrderDeliveryReadPortV1
    {
        public Task<SalesOrderDeliveryReadAnswerV1> ReadOrderDeliveriesAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SalesOrderDeliveryReadAnswerV1(company, orderId, owners.Shipped == 0m
                ? []
                : [.. lines(orderId).Select(line => new SalesOrderLineDeliveryV1(line, KgUnit, owners.Shipped, 0m))]));
    }

    private sealed class Allocations(Owners owners, Guid company, Func<Guid, IReadOnlyList<Guid>> lines) : ISalesOrderAllocationReadPortV1
    {
        public Task<SalesOrderAllocationReadAnswerV1> ReadOrderAllocationsAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            owners.AllocationUnreadable
                ? throw new InvalidOperationException("SENTETIK allocation owner outage")
                : Task.FromResult(new SalesOrderAllocationReadAnswerV1(company, orderId, owners.Allocated == 0m
                    ? []
                    : [.. lines(orderId).Select(line => new SalesOrderLineAllocationV1(line, 1, owners.Allocated))]));
    }

    private sealed class ProductionSupply(Owners owners) : ISalesOrderLinkedSupplyReadPortV1
    {
        public string SupplyKind => SalesLinkedSupplyKindsV1.Production;

        public Task<SalesOrderLinkedSupplyAnswerV1> ReadOrderLinkedSupplyAsync(SalesOrderLinkedSupplyQueryV1 query,
            CancellationToken cancellationToken = default) =>
            owners.LinkedUnreadable
                ? throw new InvalidOperationException("SENTETIK production owner outage")
                : Task.FromResult(new SalesOrderLinkedSupplyAnswerV1(
                    owners.OpenSupply is null ? SalesCancellationImpactStatusesV1.None : SalesCancellationImpactStatusesV1.Present,
                    query.CompanyId, query.OrderId, SupplyKind, owners.OpenSupply is { } item ? [item] : [], null));
    }

    private sealed class SalesScope(SalesDbContext dbContext, SalesStore store) : IAsyncDisposable
    {
        public ISalesOrderDraftStore Store => store;
        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public string Actor { get; } = "SENTETIK-preparer-" + Guid.NewGuid().ToString("N");
        public string Correlation { get; } = $"sales-sat-d252-{Guid.NewGuid():N}";

        /// <summary>The real host composed over a fresh database (health is probed so the migrations have run).</summary>
        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return run;
        }

        /// <summary>A stored draft of the company on the business day, submitted and approved (version 4), through the real Sales store.</summary>
        public async Task<SalesOrderDraft> ApprovedDraftAsync(Guid company, string keyPrefix, string quantity)
        {
            await using var scope = SalesStore(company);
            var draft = (await scope.Store.StoreDraftAsync(SalesEnvelope($"{keyPrefix}-create"), Hash($"{keyPrefix}-create"),
                documentNo => Draft(company, documentNo, quantity))).Draft;
            await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-submit"), Hash($"{keyPrefix}-submit"), SalesDraftCommands.SubmitV1, draft.Id,
                (d, now) => d.Submit(2, Actor, now), Submitted);
            var approved = await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-approve", actor: "SENTETIK-approver"), Hash($"{keyPrefix}-approve"),
                SalesDraftCommands.ApproveV1, draft.Id, (d, now) => d.Approve(3, "SENTETIK-approver", now), Approved);
            return approved!.Draft;
        }

        public SalesDraftWriteEnvelope SalesEnvelope(string key, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        /// <summary>The REAL draft service over a real store and the SYNTHETIC owners, with the given grants.</summary>
        public SalesDraftService SalesService(ISalesOrderDraftStore store, Guid company, Owners owners, Grants grants)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var noOwners = new NoOwners();
            IReadOnlyList<Guid> Lines(Guid orderId) => store.FindDraftAsync(orderId).GetAwaiter().GetResult()!.Lines.Select(line => line.Id).ToArray();
            return new SalesDraftService(companyContext, new FixedUser(Planner, company), new Authority(grants), new NoActiveRoles(),
                new FixedCorrelation(Correlation), noOwners, noOwners, null, store, new SalesDraftServiceBindings(), TimeProvider.System,
                deliveries: new Deliveries(owners, company, Lines), allocations: new Allocations(owners, company, Lines),
                linkedSupplies: [new ProductionSupply(owners)]);
        }

        public SalesDbContext SalesContext(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .Options, companyContext);
        }

        public SalesScope SalesStore(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var dbContext = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options, companyContext);
            return new SalesScope(dbContext, new SalesStore(dbContext, companyContext, collector, TimeProvider.System));
        }

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
}
