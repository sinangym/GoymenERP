using System.Data.Common;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesDraftApprovalSqlServerRelationalAcceptanceTests
{
    private static SalesMarginCostSnapshot MarginPin(SalesOrderDraft draft, DateTimeOffset now) =>
        new(draft.Lines[0].Id, draft.Revision, draft.Lines[0].Product.ProductId, draft.Lines[0].OrderUnit.UnitId,
            draft.Lines[0].Quantity.Value, Currency, 12.3456m, null, "{\"syntheticInventoryVersion\":17}", now.ToUniversalTime());

    [SqlServerFact]
    public async Task SAT_S12_margin_cost_pins_commit_with_approval_audit_and_attempt_once_are_tenant_scoped_and_block_destructive_down()
    {
        await using var run = await Run.StartAsync();
        SalesOrderDraft draft;
        await using (var scope = run.Store(CompanyA))
        {
            draft = (await scope.Store.StoreDraftAsync(run.Envelope("margin-create"), Hash("margin-create"),
                number => Draft(Owners.For(CompanyA), number, T0))).Draft;
            await scope.Store.UpdateDraftStateAsync(run.Envelope("margin-submit"), Hash("margin-submit"), SalesDraftCommands.SubmitV1,
                draft.Id, (item, now) => item.Submit(2, run.Actor, now), Submitted);
            await scope.Store.UpdateDraftStateAsync(run.Envelope("margin-approve"), Hash("margin-approve"), SalesDraftCommands.ApproveV1,
                draft.Id, (item, now) => { item.Approve(3, run.Actor, now); item.PinMarginCosts([MarginPin(item, now)]); }, Approved);
        }
        await using (var scope = run.Store(CompanyA))
        {
            var persisted = (await scope.Store.FindDraftAsync(draft.Id))!;
            Assert.Equal((12.3456m, Currency), (Assert.Single(persisted.MarginCosts).CostPerUnit, persisted.MarginCosts[0].CurrencyId));
            Assert.Equal(persisted.Approval.DecidedAtUtc, persisted.MarginCosts[0].RecordedAtUtc);
            var replay = await scope.Store.UpdateDraftStateAsync(run.Envelope("margin-approve"), Hash("margin-approve"), SalesDraftCommands.ApproveV1,
                draft.Id, (_, _) => throw new InvalidOperationException("Replay must not read or pin another cost."), Approved);
            Assert.NotNull(replay);
        }
        await using (var scope = run.Store(CompanyB)) Assert.Null(await scope.Store.FindDraftAsync(draft.Id));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftMarginCosts WHERE DraftId=@id", ("@id", draft.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=N'sales.order-draft.approved.v1' AND AfterJson LIKE N'%marginCosts%syntheticInventoryVersion%'", ("@id", draft.Id.ToString("D"))));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE DraftId=@id AND CommandName=@command", ("@id", draft.Id), ("@command", SalesDraftCommands.ApproveV1)));
        await using var context = run.Context(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var index = Array.FindIndex(applied, item => item.EndsWith("_AddSalesMarginCostSnapshots", StringComparison.Ordinal));
        Assert.True(index > 0);
        var failure = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(applied[index - 1]));
        Assert.Equal(51993, failure.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftMarginCosts WHERE DraftId=@id", ("@id", draft.Id)));
    }

    [SqlServerFact]
    public async Task SAT_S12_failure_after_cost_insert_rolls_back_approval_cost_audit_and_attempt_together()
    {
        await using var run = await Run.StartAsync();
        SalesOrderDraft draft;
        await using (var scope = run.Store(CompanyA))
        {
            draft = (await scope.Store.StoreDraftAsync(run.Envelope("margin-rollback-create"), Hash("margin-rollback-create"),
                number => Draft(Owners.For(CompanyA), number, T0))).Draft;
            await scope.Store.UpdateDraftStateAsync(run.Envelope("margin-rollback-submit"), Hash("margin-rollback-submit"), SalesDraftCommands.SubmitV1,
                draft.Id, (item, now) => item.Submit(2, run.Actor, now), Submitted);
        }
        var failure = new FailAfterMarginCostInsert();
        await using (var scope = run.Store(CompanyA, failure))
            await Assert.ThrowsAnyAsync<Exception>(() => scope.Store.UpdateDraftStateAsync(run.Envelope("margin-rollback-approve"), Hash("margin-rollback-approve"), SalesDraftCommands.ApproveV1,
                draft.Id, (item, now) => { item.Approve(3, run.Actor, now); item.PinMarginCosts([MarginPin(item, now)]); }, Approved));
        Assert.True(failure.Triggered);
        Assert.Equal(3L, await run.ScalarAsync<long>("SELECT Version FROM sales.Drafts WHERE Id=@id", ("@id", draft.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftMarginCosts WHERE DraftId=@id", ("@id", draft.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE DraftId=@id AND CommandName=@command", ("@id", draft.Id), ("@command", SalesDraftCommands.ApproveV1)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=N'sales.order-draft.approved.v1'", ("@id", draft.Id.ToString("D"))));
    }

    [SqlServerFact]
    public async Task SAT_S12_empty_margin_cost_migration_can_roll_back_without_backfilling_legacy_approved_orders()
    {
        await using var run = await Run.StartAsync();
        Guid draftId;
        await using (var scope = run.Store(CompanyA))
        {
            var draft = (await scope.Store.StoreDraftAsync(run.Envelope("margin-legacy-create"), Hash("margin-legacy-create"),
                number => Draft(Owners.For(CompanyA), number, T0))).Draft;
            draftId = draft.Id;
            await scope.Store.UpdateDraftStateAsync(run.Envelope("margin-legacy-submit"), Hash("margin-legacy-submit"), SalesDraftCommands.SubmitV1,
                draftId, (item, now) => item.Submit(2, run.Actor, now), Submitted);
            // Represents the old approval writer: no cost snapshot existed at its commit.
            await scope.Store.UpdateDraftStateAsync(run.Envelope("margin-legacy-approve"), Hash("margin-legacy-approve"), SalesDraftCommands.ApproveV1,
                draftId, (item, now) => item.Approve(3, run.Actor, now), Approved);
        }
        await using var context = run.Context(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var index = Array.FindIndex(applied, item => item.EndsWith("_AddSalesMarginCostSnapshots", StringComparison.Ordinal));
        Assert.True(index > 0);
        await context.GetService<IMigrator>().MigrateAsync(applied[index - 1]);
        Assert.Equal(applied[..index], (await context.Database.GetAppliedMigrationsAsync()).ToArray());
        // Forward again through every later migration of the table (SAT-D426 adds the frozen sale rate columns), so the
        // current store reads the restored schema; nothing is backfilled on the way.
        await context.Database.MigrateAsync();
        Assert.Equal(applied, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftMarginCosts"));
        await using var restored = run.Store(CompanyA);
        var legacy = (await restored.Store.FindDraftAsync(draftId))!;
        Assert.Equal(InternalApprovalState.Approved, legacy.ApprovalState);
        Assert.Empty(legacy.MarginCosts);
    }

    private sealed class FailAfterMarginCostInsert : DbCommandInterceptor
    {
        public bool Triggered { get; private set; }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO [sales].[DraftMarginCosts]", StringComparison.Ordinal))
            {
                Triggered = true;
                throw new InvalidOperationException("SENTETIK margin cost persistence failure.");
            }
            return ValueTask.FromResult(result);
        }
    }
}
