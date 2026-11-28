using System.Data.Common;
using System.Text.Json;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesQuoteSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Selected_expired_quote_original_decision_binds_actual_request_and_keeps_native_current_replay_and_NULL_ResultJson()
    {
        await using var run=await Run.StartAsync();var q=await SeedSafeQuote(run,true);
        await using var safe=QuoteSafetyScope(run);var target=await SelectedQuoteTarget(safe,q.Id,q.RequestNo);
        var human=await safe.Service.ReadQuoteConversionDecisionAsync(target.RequestId,_=>Task.CompletedTask);
        Assert.Equal(SalesQuoteDecisionReadStateV1.Found,human.State);Assert.Equal(("1","1",true,true),
            (human.Detail!.Quote.Quote.Version,human.Detail.Request.Version,human.Detail.ApprovalSupported,human.Detail.RejectionSupported));
        var body=new SalesQuoteExpiryApprovalRequest("01","  SENTETIK original  ");
        var first=await safe.Service.ApproveSelectedQuoteConversionAsync(target,body,run.Metadata("selected-approve","selected-original"));
        var original=Assert.IsType<SalesQuoteOriginalDecisionReceiptV1>(first.OriginalDecisionReceipt);
        Assert.Equal((target,"01","2","Approved","selected-original"),(original.Target,original.ExpectedVersion,original.ResultVersion,original.OriginalState,original.OriginalCorrelationId));
        var rejection=await safe.Service.RejectSelectedQuoteConversionAsync(target,new("01","  SENTETIK ret  "),run.Metadata("selected-reject","selected-reject-original"));
        var rejected=Assert.IsType<SalesQuoteOriginalDecisionReceiptV1>(rejection.OriginalDecisionReceipt);
        Assert.Equal(("Rejected","2",target),(rejected.OriginalState,rejected.ResultVersion,rejected.Target));
        var terminal=await safe.Service.ReadQuoteConversionDecisionAsync(target.RequestId,_=>Task.CompletedTask);
        Assert.Equal(("2","2",false,false),(terminal.Detail!.Quote.Quote.Version,terminal.Detail.Request.Version,terminal.Detail.ApprovalSupported,terminal.Detail.RejectionSupported));
        var replay=await safe.Service.ApproveSelectedQuoteConversionAsync(target,body,run.Metadata("selected-approve","later-correlation"));
        Assert.True(replay.IsReplay);Assert.Equal("selected-original",replay.OriginalCorrelationId);
        Assert.Equal(original.OriginalDecisionDigest,Assert.IsType<SalesQuoteOriginalDecisionReceiptV1>(replay.OriginalDecisionReceipt).OriginalDecisionDigest);
        Assert.Equal("Approved",replay.OriginalDecisionReceipt!.OriginalState); // The selected row is now Rejected; original approval is unchanged.
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(replay,SalesDraftJson.Options));
        Assert.Equal(3,json.RootElement.EnumerateObject().Count());Assert.False(json.RootElement.TryGetProperty("originalDecisionReceipt",out _));
        Assert.Equal(0,await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.QuoteAttempts WHERE QuoteId=@id AND ResultJson IS NOT NULL",("@id",q.Id)));
        Assert.Equal((1,1),(await QuoteDecisionCount(run,q.Id,true),await QuoteDecisionCount(run,q.Id,false)));AssertQuoteClean(safe);
    }

    [SqlServerFact]
    public async Task Selected_expired_quote_rejected_A_and_new_B_same_revision_cannot_relabel_an_original_B_key()
    {
        await using var run=await Run.StartAsync();var q=await SeedSafeQuote(run,true);
        await using var safe=QuoteSafetyScope(run);var a=await SelectedQuoteTarget(safe,q.Id,q.RequestNo);
        await safe.Service.RejectSelectedQuoteConversionAsync(a,new("1","SENTETIK A ret"),run.Metadata("selected-A-reject"));
        var opened=await safe.Service.RequestQuoteConversionAsync(q.Id,new("1","SENTETIK B"),run.Metadata("selected-B-request"));
        var b=await SelectedQuoteTarget(safe,q.Id,opened.Request.RequestNo);Assert.NotEqual(a.RequestId,b.RequestId);Assert.Equal(a.SubjectRevision,b.SubjectRevision);
        var refused=await Assert.ThrowsAsync<SalesDraftServiceException>(()=>safe.Service.ApproveSelectedQuoteConversionAsync(a,new("1"),run.Metadata("selected-invalid-A")));
        Assert.Equal(503,refused.Status);AssertQuoteAborted(safe);
        var first=await safe.Service.ApproveSelectedQuoteConversionAsync(b,new("1"),run.Metadata("selected-B-approve","B-original"));
        Assert.Equal(b,Assert.IsType<SalesQuoteOriginalDecisionReceiptV1>(first.OriginalDecisionReceipt).Target);
        var wrongSelection=await safe.Service.ApproveSelectedQuoteConversionAsync(a,new("1"),run.Metadata("selected-B-approve"));
        Assert.True(wrongSelection.IsReplay);Assert.Null(wrongSelection.OriginalDecisionReceipt);
        var replay=await safe.Service.ApproveSelectedQuoteConversionAsync(b,new("1"),run.Metadata("selected-B-approve"));
        Assert.Equal("B-original",replay.OriginalCorrelationId);Assert.Equal(b,replay.OriginalDecisionReceipt!.Target);
        Assert.Equal(1,await QuoteDecisionCount(run,q.Id,true));
        Assert.Equal(0,await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.QuoteAttempts WHERE IdempotencyKeyHash=@key",("@key",SalesStore.IdempotencyKeyHash("selected-invalid-A"))));
    }

    [SqlServerFact]
    public async Task Selected_expired_quote_legacy_success_without_selected_trace_keeps_native_replay_but_no_private_proof()
    {
        await using var run=await Run.StartAsync();var q=await SeedSafeQuote(run,true);
        await using var safe=QuoteSafetyScope(run);var target=await SelectedQuoteTarget(safe,q.Id,q.RequestNo);
        var native=await safe.Service.ApproveExpiredQuoteConversionAsync(q.Id,new("1"),run.Metadata("legacy-approval","legacy-original"));
        Assert.Null(native.OriginalDecisionReceipt);
        var replay=await safe.Service.ApproveSelectedQuoteConversionAsync(target,new("1"),run.Metadata("legacy-approval"));
        Assert.True(replay.IsReplay);Assert.Equal("legacy-original",replay.OriginalCorrelationId);Assert.Null(replay.OriginalDecisionReceipt);
        Assert.Equal(1,await QuoteDecisionCount(run,q.Id,true));
        Assert.Equal(0,await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType='sales.quote.expired-conversion-approved.v1' AND ApprovalContextJson IS NOT NULL",("@id",q.Id.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Selected_expired_quote_target_versions_numbers_and_revision_are_validated_inside_owned_TX_before_any_decision()
    {
        await using var run=await Run.StartAsync();var q=await SeedSafeQuote(run,true);
        await using var safe=QuoteSafetyScope(run);var target=await SelectedQuoteTarget(safe,q.Id,q.RequestNo);
        var invalid=new[]{target with{RequestId=Guid.NewGuid()},target with{QuoteId=Guid.NewGuid()},target with{RequestNo=target.RequestNo+1},
            target with{SubjectRevision=target.SubjectRevision+1},target with{RequestVersion="2"}};
        foreach(var selected in invalid)
        {
            var refusal=await Assert.ThrowsAsync<SalesDraftServiceException>(()=>safe.Service.ApproveSelectedQuoteConversionAsync(selected,new("1"),run.Metadata("selected-shape")));
            Assert.Equal(selected.QuoteId==q.Id?503:404,refusal.Status);AssertQuoteAborted(safe);Assert.Equal(0,await QuoteDecisionCount(run,q.Id,true));
        }
        var accepted=await safe.Service.ApproveSelectedQuoteConversionAsync(target,new("1"),run.Metadata("selected-shape"));
        Assert.NotNull(accepted.OriginalDecisionReceipt);Assert.False(accepted.IsReplay);Assert.Equal(1,await QuoteDecisionCount(run,q.Id,true));
    }

    [SqlServerFact]
    public async Task Selected_expired_quote_human_read_is_company_scoped_and_final_consumer_normal_completion_cannot_release_facts()
    {
        await using var run=await Run.StartAsync();var q=await SeedSafeQuote(run,true);
        await using var safe=QuoteSafetyScope(run);var target=await SelectedQuoteTarget(safe,q.Id,q.RequestNo);
        var observed=0;var first=await safe.Service.ReadQuoteConversionDecisionAsync(target.RequestId,_=>{observed++;return Task.CompletedTask;});
        Assert.Equal(SalesQuoteDecisionReadStateV1.Found,first.State);Assert.True(observed>6);Assert.Empty(safe.Db.ChangeTracker.Entries());
        foreach(var cancel in new[]{false,true})
        {
            using var cancellation=new CancellationTokenSource();var calls=0;safe.Authority.Granted=true;
            Task Fence(CancellationToken _){if(++calls==observed){if(cancel)cancellation.Cancel();else safe.Authority.Granted=false;}return Task.CompletedTask;}
            if(cancel)await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>safe.Service.ReadQuoteConversionDecisionAsync(target.RequestId,Fence,cancellation.Token));
            else{var denied=await safe.Service.ReadQuoteConversionDecisionAsync(target.RequestId,Fence);Assert.Equal(SalesQuoteDecisionReadStateV1.Denied,denied.State);Assert.Null(denied.Detail);}
            Assert.Equal(observed,calls);AssertQuoteClean(safe);Assert.Empty(safe.Db.ChangeTracker.Entries());
        }
        await using var other=run.Store(CompanyB);
        var foreign=await run.Service(other,run.Actor).ReadQuoteConversionDecisionAsync(target.RequestId,_=>Task.CompletedTask);
        Assert.Equal(SalesQuoteDecisionReadStateV1.NotFound,foreign.State);Assert.Null(foreign.Detail);
        Assert.Equal(0,await QuoteDecisionCount(run,q.Id,true));Assert.Equal(0,await QuoteDecisionCount(run,q.Id,false));
    }

    [SqlServerFact]
    public async Task Selected_expired_quote_receipt_attempt_read_revocation_or_cancel_rolls_back_effect_audit_and_key_then_retries_once()
    {
        await using var run=await Run.StartAsync();
        foreach(var approve in new[]{true,false})foreach(var cancel in new[]{true,false})
        {
            var q=await SeedSafeQuote(run,true);var probe=new SelectedQuoteReceiptReadProbe(approve);using var cancellation=new CancellationTokenSource();
            await using var safe=QuoteSafetyScope(run,probe);var target=await SelectedQuoteTarget(safe,q.Id,q.RequestNo);
            probe.AfterRead=()=>{if(cancel)cancellation.Cancel();else safe.Authority.Granted=false;};var key="selected-read-"+Guid.NewGuid().ToString("N");
            async Task Decide(){if(approve)await safe.Service.ApproveSelectedQuoteConversionAsync(target,new("1"),run.Metadata(key),cancellation.Token);
                else await safe.Service.RejectSelectedQuoteConversionAsync(target,new("1","SENTETIK ret"),run.Metadata(key),cancellation.Token);}
            if(cancel)await Assert.ThrowsAnyAsync<OperationCanceledException>(Decide);
            else Assert.Equal(403,(await Assert.ThrowsAsync<SalesDraftServiceException>(Decide)).Status);
            Assert.True(probe.Triggered);AssertQuoteAborted(safe);
            Assert.Equal(0,await QuoteDecisionCount(run,q.Id,approve));Assert.Equal(0,await QuoteAuditCount(run,q.Id,approve));
            safe.Authority.Granted=true;probe.AfterRead=null;
            if(approve){var answer=await safe.Service.ApproveSelectedQuoteConversionAsync(target,new("1"),run.Metadata(key));Assert.False(answer.IsReplay);Assert.NotNull(answer.OriginalDecisionReceipt);}
            else{var answer=await safe.Service.RejectSelectedQuoteConversionAsync(target,new("1","SENTETIK ret"),run.Metadata(key));Assert.False(answer.IsReplay);Assert.NotNull(answer.OriginalDecisionReceipt);}
            Assert.Equal(1,await QuoteDecisionCount(run,q.Id,approve));Assert.Equal(1,await QuoteAuditCount(run,q.Id,approve));
        }
    }

    private static async Task<SalesQuoteConversionTargetV1> SelectedQuoteTarget(QuoteSafetyScopeState safe,Guid quote,int no)
    {
        var row=await safe.Db.ApprovalRequests.AsNoTracking().SingleAsync(x=>x.CompanyId==CompanyA && x.SubjectId==quote && x.RequestNo==no);
        return new(row.Id,quote,no,row.SubjectRevision,row.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    private sealed class SelectedQuoteReceiptReadProbe(bool approve):DbCommandInterceptor
    {
        internal Action? AfterRead;internal bool Triggered;
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,CommandExecutedEventData eventData,DbDataReader result,CancellationToken cancellationToken=default)
        {
            var saved=eventData.Context?.ChangeTracker.Entries().Any(x=>x.State==EntityState.Unchanged &&
                (approve?x.Entity is QuoteAttemptRow q && q.CommandName==SalesQuoteCommands.ApproveExpiredConversionV1:
                    x.Entity is ApprovalRequestAttemptRow r && r.CommandName==SalesApprovalRequestCommands.RejectQuoteConversionV1))==true;
            if(!Triggered && saved && command.CommandText.Contains(approve?"[QuoteAttempts]":"[ApprovalRequestAttempts]",StringComparison.Ordinal))
            {Triggered=true;AfterRead?.Invoke();}
            return ValueTask.FromResult(result);
        }
    }
}
