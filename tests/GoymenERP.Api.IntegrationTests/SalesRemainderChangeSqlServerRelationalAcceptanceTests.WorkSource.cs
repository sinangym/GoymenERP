using System.Data.Common;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesRemainderChangeSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Remainder_pending_Work_source_uses_one_bounded_company_filtered_scalar_query_and_retains_native_requester_metadata()
    {
        await using var run = await Run.StartAsync(); var first = await SeedOwnedRemainder(run); await SeedOwnedRemainder(run); var foreign = await SeedOwnedRemainder(run, CompanyB);
        var probe = new RemainderListQueryProbe(); await using var safe = RemainderScope(run, probe);
        IWorkInboxSourceV1 source = new SalesRemainderChangeWorkInboxSourceV1(safe.Company, safe.User, safe.Authority, safe.Service);
        var context = new WorkInboxSourceContextV1(CompanyA, safe.User.UserId, "SENTETIK-source", null);
        var result = await source.ListPendingAsync(context, 1, default);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome); Assert.True(result.HasMore); var item = Assert.Single(result.Items!);
        Assert.Equal(first, item.ItemId); Assert.NotEqual(foreign, item.ItemId); Assert.Equal(run.Actor, item.PreparedBy);
        Assert.Null(item.ExcludedDeciders); Assert.Null(Assert.Single(item.Rules).Actual); Assert.Equal(1, probe.Queries); Assert.False(probe.ReadFullLines);
        AssertOwnedRemainderClean(safe);
    }

    [SqlServerFact]
    public async Task Remainder_Work_version_source_recovers_exact_original_change_after_a_new_pending_request_without_human_history_or_mutation()
    {
        await using var run = await Run.StartAsync(); var id = await SeedOwnedRemainder(run); var key = "source-original-" + id.ToString("N");
        await using var safe = RemainderScope(run); var first = await safe.Service.ApproveRemainderChangeAsync(id, 1, new("5"), RemainderMetadata(key));
        var original = Assert.IsType<GoymenERP.Modules.Sales.Application.Contracts.SalesRemainderOriginalReceiptV1>(first.OriginalRemainderDecisionReceipt);
        await using (var raw = run.SalesStore(CompanyA))
            await raw.Store.ChangeRemainderAsync(run.SalesEnvelope("source-next-" + id.ToString("N"), safe.User.UserId), Hash("source-next-" + id),
                GoymenERP.Modules.Sales.Application.Contracts.SalesDraftCommands.RequestRemainderChangeV1, id,
                (order, now) => order.RequestRemainderChange(6, original.LineId, GoymenERP.Modules.Sales.Domain.Drafts.RemainderChangeKind.Revise,
                    "7", 7m, "SENTETIK sonraki", new(6m, 4m, 2.5m), safe.User.UserId, now),
                new("sales.order-draft.remainder-change-requested.v1", "RequestRemainderChange", "SENTETIK sonraki"), (_, _) => Task.CompletedTask);
        var durable = await run.EffectsAsync(id); safe.Roles.Support = true;
        var source = new SalesRemainderChangeWorkInboxSourceV1(safe.Company, safe.User, safe.Authority, safe.Service);
        var context = new WorkInboxSourceContextV1(CompanyA, safe.User.UserId, "SENTETIK-recovery", null);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(context, id, default));
        var selected = await source.ReadDecisionVersionAsync(context, id, "5", default); Assert.NotNull(selected);
        var p = Assert.IsType<WorkSalesRemainderChangeProofV1>(selected.SalesRemainderChangeProof);
        Assert.Equal((5L, 7L, 1, false), (p.SelectedExpectedVersion, p.ObservedCurrentVersion, p.ChangeNo, p.ApplyPersonaRestriction)); Assert.False(selected.Pending);
        var replay = await safe.Service.ApproveRemainderChangeAsync(id, 1, new("5"), RemainderMetadata(key)); Assert.True(replay.IsReplay);
        Assert.True(SalesRemainderChangeDecisionBindingV1.MatchesOriginalDecisionReceipt(selected, replay, new("5"), key, safe.User.UserId, true));
        Assert.False(SalesRemainderChangeDecisionBindingV1.MatchesOriginalDecisionReceipt(selected, replay, new("05"), key, safe.User.UserId, true)); // Raw native spellings remain distinct; Work versions are canonical.
        Assert.Equal(original.OriginalDecisionDigest, replay.OriginalRemainderDecisionReceipt!.OriginalDecisionDigest);
        Assert.Equal(durable, await run.EffectsAsync(id)); Assert.Equal(1, await run.AttemptsAsync(key));
    }
    private sealed class RemainderListQueryProbe : DbCommandInterceptor
    {
        internal int Queries; internal bool ReadFullLines;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        { Queries++; ReadFullLines |= command.CommandText.Contains("DraftLines", StringComparison.Ordinal); return ValueTask.FromResult(result); }
    }
}
