using System.Data.Common;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingCostDifferenceSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Cost_work_pending_list_uses_one_company_bound_scalar_query_and_matches_actual_full_owner_decision()
    {
        await using var run = await Run.StartAsync(); var period = await run.OpenPeriod();
        for (var i = 0; i < 2; i++)
        {
            var native = Source(); await using var seed = run.Scope(); await seed.Store.AcceptAsync(Envelope(native), native);
            Assert.Equal(AccountingApiStatus.Ok, (await seed.Service(Maker).PrepareAsync(Preparation(native.CostDifferenceEventId!.Value, period), "source-seed-" + i)).Status);
        }
        var before = await SafetyCounts(run); var probe = new CostWorkSqlProbe(); await using var scope = new CostApprovalScope(run, extra: probe);
        var source = new AccountingManufacturingCostDifferenceWorkInboxSourceV1(scope.Db, new(scope.Company, scope.User, scope.Authority), scope.Service);
        var page = await source.ListPendingAsync(CostWorkSqlContext(scope), 1, default);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, page.Outcome); Assert.True(page.HasMore); var listed = Assert.Single(page.Items!);
        var sql = Assert.Single(probe.Reads); Assert.Contains("[CompanyId]", sql); Assert.Contains("[SignedTlDifference]", sql);
        Assert.DoesNotContain("[SourceJson]", sql); Assert.DoesNotContain("[AccountPlanProofJson]", sql); Assert.DoesNotContain("[Reason]", sql);
        var snapshot = (await source.ReadDecisionAsync(CostWorkSqlContext(scope), listed.ItemId, default))!;
        Assert.Equal(JsonSerializer.Serialize(listed, Json), JsonSerializer.Serialize(snapshot.Item, Json)); Assert.Equal("2", snapshot.SourceVersion);
        Assert.True(AccountingManufacturingCostDifferenceDecisionBindingV1.MatchesDecisionProof(snapshot.Item, snapshot.ManufacturingCostDifferenceProof));
        var full = await scope.Service.ReadDecisionAsync(listed.ItemId, _ => Task.CompletedTask);
        Assert.True(AccountingManufacturingCostDifferenceDecisionBindingV1.MatchesDecisionItem(snapshot.Item, full.Resource));
        Assert.Equal(AccountingManufacturingCostDifferenceWorkInboxSourceV1.ScreenPath, snapshot.Item.Link);
        Assert.Equal(0, scope.CurrentPlan.Reads); Assert.Equal(0, scope.Currencies.Reads); Assert.Equal(before, await SafetyCounts(run)); scope.AssertClean();
    }

    [SqlServerFact]
    public async Task Cost_work_original_receipt_after_repreparation_uses_N_to_N_plus_one_and_survives_current_plan_change()
    {
        await using var run = await Run.StartAsync(); var period = await run.OpenPeriod(); var native = Source(); var id = native.CostDifferenceEventId!.Value;
        await using (var seed = run.Scope())
        {
            await seed.Store.AcceptAsync(Envelope(native), native);
            Assert.Equal(AccountingApiStatus.Ok, (await seed.Service(Maker).PrepareAsync(Preparation(id, period), "initial-preparation")).Status);
            var newer = await seed.Service(Maker).PrepareAsync(Preparation(id, period) with { ExpectedVersion = 2 }, "repreparation");
            Assert.Equal(AccountingApiStatus.Ok, newer.Status); Assert.Equal(3L, newer.Resource?.Version);
        }
        await using var scope = new CostApprovalScope(run);
        var source = new AccountingManufacturingCostDifferenceWorkInboxSourceV1(scope.Db, new(scope.Company, scope.User, scope.Authority), scope.Service);
        var pending = (await source.ReadDecisionAsync(CostWorkSqlContext(scope), id, default))!; Assert.Equal("3", pending.SourceVersion); Assert.True(pending.Pending);
        var first = await scope.Service.ApproveAsync(id, 3, "frozen-native-three"); var receipt = Assert.IsType<ManufacturingCostDifferenceApprovalReceiptV1>(first.Resource?.ApprovalReceipt);
        Assert.Equal(3L, receipt.ExpectedVersion); Assert.Equal(4L, receipt.ResultVersion);
        Assert.Equal(pending.ManufacturingCostDifferenceProof?.SourceFactsDigest, receipt.SourceFactsDigest);
        var counts = await SafetyCounts(run); scope.CurrentPlan.Missing = true; var planReads = scope.CurrentPlan.Reads;
        var terminal = (await source.ReadDecisionAsync(CostWorkSqlContext(scope), id, default))!; Assert.False(terminal.Pending); Assert.Equal("4", terminal.SourceVersion);
        var replay = await scope.Service.ApproveAsync(id, 3, "frozen-native-three"); Assert.True(replay.IsReplay); Assert.Equal(receipt, replay.Resource?.ApprovalReceipt);
        Assert.True(AccountingManufacturingCostDifferenceDecisionBindingV1.MatchesDecisionItem(pending.Item, replay.Resource, replay.Resource?.ApprovalReceipt));
        var wrong = pending.Item with { Rules = [pending.Item.Rules[0] with { Actual = "601" }] };
        Assert.False(AccountingManufacturingCostDifferenceDecisionBindingV1.MatchesDecisionItem(wrong, replay.Resource, replay.Resource?.ApprovalReceipt));
        Assert.Equal(planReads, scope.CurrentPlan.Reads); Assert.Equal(counts, await SafetyCounts(run)); scope.AssertClean();
    }

    private static WorkInboxSourceContextV1 CostWorkSqlContext(CostApprovalScope scope) => new(Company, Checker.ToString("D"), scope.CorrelationId, null);
    private sealed class CostWorkSqlProbe : DbCommandInterceptor
    {
        public List<string> Reads { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("ManufacturingCostDifferences", StringComparison.Ordinal)) Reads.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
