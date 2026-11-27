using System.Data.Common;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Cogs_purpose_pending_list_is_one_company_scalar_query_and_matches_full_retained_owner_without_live_reads()
    {
        await using var run = await Run.Start(); var id = await CogsSafetyPrepare(run);
        await using (var seed = run.Open())
        {
            var prepared = (await seed.Store.FindAsync(id))!;
            await seed.Store.PrepareAsync(run.Source.Facts, Request(run.Source.Facts, prepared.PeriodId), Maker, Fence, Metadata(Maker));
        }
        var before = await CogsCountsOf(run); var probe = new CogsDecisionQueryProbe(); await using var scope = new CogsApprovalScope(run, extra: probe);
        var source = new AccountingInvoiceLineCogsCorrectionWorkInboxSourceV1(scope.Db, new(scope.Company, scope.User, scope.Authority), scope.Service);
        var page = await source.ListPendingAsync(CogsDecisionContext(scope), 1, default);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, page.Outcome); Assert.True(page.HasMore); var item = Assert.Single(page.Items!);
        var sql = Assert.Single(probe.Queries); Assert.Contains("[CompanyId]", sql); Assert.DoesNotContain("[SourceJson]", sql); Assert.DoesNotContain("[ViewJson]", sql);
        var reads = run.Source.Calls; run.Source.Unavailable = true;
        var snapshot = (await source.ReadDecisionAsync(CogsDecisionContext(scope), item.ItemId, default))!;
        Assert.Equal(JsonSerializer.Serialize(item, Json), JsonSerializer.Serialize(snapshot.Item, Json)); Assert.True(snapshot.Pending); Assert.Equal("1", snapshot.SourceVersion);
        Assert.True(AccountingInvoiceLineCogsDecisionBindingV1.MatchesDecisionProof(snapshot.Item, snapshot.InvoiceLineCogsProof));
        var answer = await scope.Service.ReadDecisionAsync(item.ItemId, _ => Task.CompletedTask);
        Assert.True(AccountingInvoiceLineCogsDecisionBindingV1.MatchesDecisionItem(item, answer.Resource));
        Assert.Equal(reads, run.Source.Calls); Assert.Equal(0, scope.CurrentPlan.Reads); Assert.Equal(before, await CogsCountsOf(run)); scope.AssertClean();
    }
    [SqlServerFact]
    public async Task Cogs_original_proposal_receipt_survives_a_real_later_line_revision_and_unavailable_live_source()
    {
        await using var run = await Run.Start(); var id = await CogsSafetyPrepare(run); await using var scope = new CogsApprovalScope(run);
        var pending = (await scope.Service.ReadDecisionAsync(id, _ => Task.CompletedTask)).Resource!;
        var first = await scope.Approve(id, "first-proposal"); var receipt = Assert.IsType<InvoiceLineCogsApprovalReceiptV1>(first.Resource?.ApprovalReceipt);
        Assert.Equal(pending.SourceFactsDigest, receipt.SourceFactsDigest); Assert.Equal(1L, receipt.ExpectedVersion); Assert.Equal(2L, receipt.ResultVersion);
        Guid nextId;
        await using (var native = run.Open())
        {
            var next = await native.Store.PrepareAsync(run.Source.Facts, Request(run.Source.Facts, pending.Proposal.PeriodId) with { ExpectedLineVersion = 1 }, Maker, Fence, Metadata(Maker));
            nextId = next.ProposalId;
        }
        Assert.Equal(AccountingApiStatus.Ok, (await scope.Approve(nextId, "later-line-revision")).Status);
        var before = await CogsCountsOf(run); var sourceReads = run.Source.Calls; var planReads = scope.CurrentPlan.Reads; run.Source.Unavailable = true;
        var replay = await scope.Approve(id, "first-proposal"); var terminal = (await scope.Service.ReadDecisionAsync(id, _ => Task.CompletedTask)).Resource!;
        Assert.True(replay.IsReplay); Assert.Equal(receipt, replay.Resource?.ApprovalReceipt); Assert.Equal(scope.CorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(2L, terminal.CurrentLineVersion); Assert.Equal(2L, terminal.Proposal.Version); Assert.Equal(id, terminal.Proposal.ProposalId);
        Assert.Equal(receipt.SourceFactsDigest, terminal.SourceFactsDigest); Assert.Equal(first.Resource?.ReversingCorrectionJournalId, replay.Resource?.ReversingCorrectionJournalId);
        Assert.Equal(sourceReads, run.Source.Calls); Assert.Equal(planReads, scope.CurrentPlan.Reads); Assert.Equal(before, await CogsCountsOf(run)); scope.AssertClean();
    }
    [SqlServerFact]
    public async Task Cogs_purpose_final_completed_consumer_permission_or_cancellation_refuses_without_read_writes()
    {
        await using var run = await Run.Start(); var id = await CogsSafetyPrepare(run); var before = await CogsCountsOf(run);
        foreach (var cancel in new[] { false, true })
        {
            await using var scope = new CogsApprovalScope(run); var callbacks = 0; var grant = true; using var cts = new CancellationTokenSource();
            scope.Authority.Read = () => grant;
            Task Consumer(CancellationToken _) { if (++callbacks == 4) { if (cancel) cts.Cancel(); else grant = false; } return Task.CompletedTask; }
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Service.ReadDecisionAsync(id, Consumer, cts.Token));
            else { var denied = await scope.Service.ReadDecisionAsync(id, Consumer); Assert.Equal(AccountingApiStatus.Denied, denied.Status); Assert.Null(denied.Resource); }
            Assert.Equal(4, callbacks); Assert.Equal(before, await CogsCountsOf(run)); scope.AssertClean();
        }
    }
    [SqlServerFact]
    public async Task Cogs_each_saved_receipt_read_fence_aborts_command_revision_journals_and_allows_original_key_once()
    {
        // owned Save1/service2/receipt-before3/Commands4/source-before5/source-after6/receipt-final7/runner8/precommit9.
        foreach (var phase in new[] { 3, 4, 5, 6, 7 })
        {
            await using var run = await Run.Start(); var id = await CogsSafetyPrepare(run); var before = await CogsCountsOf(run);
            var save = new CogsApprovalSave(); await using var scope = new CogsApprovalScope(run, save); var savedFences = 0; var grant = true;
            scope.Authority.Read = () => { if (save.Saved && ++savedFences == phase) grant = false; return grant; };
            var denied = await scope.Approve(id, "receipt-read-fence"); Assert.Equal(AccountingApiStatus.Denied, denied.Status); Assert.Null(denied.Resource);
            Assert.Equal(phase, savedFences); Assert.Equal(before, await CogsCountsOf(run)); scope.AssertClean();
            grant = true; scope.Authority.Read = null;
            var first = await scope.Approve(id, "receipt-read-fence"); var replay = await scope.Approve(id, "receipt-read-fence");
            Assert.Equal(AccountingApiStatus.Ok, first.Status); Assert.NotNull(first.Resource?.ApprovalReceipt); Assert.True(replay.IsReplay);
            Assert.Equal(first.Resource?.ApprovalReceipt, replay.Resource?.ApprovalReceipt); Assert.Equal(scope.CorrelationId, replay.OriginalCorrelationId);
            Assert.Equal(before with { Revisions = before.Revisions + 1, Commands = before.Commands + 1, Journals = before.Journals + 2, Audits = before.Audits + 1 }, await CogsCountsOf(run)); scope.AssertClean();
        }
    }
    private static WorkInboxSourceContextV1 CogsDecisionContext(CogsApprovalScope scope) => new(Company, Checker.ToString("D"), scope.CorrelationId, null);
    private sealed class CogsDecisionQueryProbe : DbCommandInterceptor
    {
        public List<string> Queries { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken token = default)
        { if (command.CommandText.Contains("[InvoiceLineCogsProposals]", StringComparison.Ordinal)) Queries.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
}
