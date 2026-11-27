using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class AccountingSourceVoucherTests
{
    private static readonly JsonSerializerOptions DecisionJson = new(JsonSerializerDefaults.Web);
    private static AccountingSourceVoucherDecisionSourceV1 DecisionSource(bool approved = false)
    {
        var at = new DateTimeOffset(2026, 11, 28, 10, 0, 0, TimeSpan.Zero);
        var p = new AccountingSourceVoucherViewV1(Invoice, Company, "sales-invoice", Guid.NewGuid(), Guid.NewGuid(), 4, Maker,
            Guid.NewGuid(), Guid.NewGuid(), null, new(2026, 11, 28), null, approved ? "Posted" : "PeriodWaiting", approved ? 2 : 1,
            1, approved ? Guid.NewGuid() : null, approved ? Invoice : null, approved ? Actor : null, approved ? at : null, null,
            [new(1, "SENTETIK-debit", 1, Guid.NewGuid(), "120", null, null),new(2, "SENTETIK-credit", 2, Guid.NewGuid(), "120", null, null)], "SENTETIK-57", "120");
        return new(p, p with { Status = approved ? "Posted" : "AwaitingApproval" }, at,
            new(100,20,0,120,100,20,0,120), false, true, approved, approved ? "SENTETIK-correlation" : null);
    }
    [Fact]
    public async Task Purpose_read_uses_only_existing_approval_permission_and_keeps_current_readiness_separate()
    {
        var f = new Fixture(); var source = DecisionSource(); var reader = new DecisionStore(source);
        var service = new AccountingSourceVoucherServiceV1(reader,f.Commands,f.Authority,f.Company,f.User,new Correlation());
        var fences = 0; var answer = await service.ReadDecisionAsync(Invoice,_=>{fences++;return Task.CompletedTask;});
        var detail = Assert.IsType<AccountingSourceVoucherDecisionDetailV1>(answer.Resource);
        Assert.Equal(AccountingApiStatus.Ok,answer.Status);Assert.True(fences>=3);Assert.Equal(1,reader.Reads);
        Assert.Equal(source.CurrentVoucher,detail.Voucher);Assert.True(detail.PeriodOpen);Assert.False(detail.SourceCancelled);
        Assert.Equal(0,reader.Writes);Assert.Equal(0,f.Commands.Reads);
        var closed=source with{PeriodOpen=false,CurrentVoucher=source.CurrentVoucher with{Status="PeriodWaiting"}};
        Assert.Equal(AccountingSourceVoucherServiceV1.SourceVoucherSourceFactsDigest(source),AccountingSourceVoucherServiceV1.SourceVoucherSourceFactsDigest(closed));
        Assert.NotEqual(detail.PreviewDigest,AccountingSourceVoucherServiceV1.SourceVoucherPreviewDigest(closed.CurrentVoucher,closed.CreatedAtUtc,false,false));
    }
    [Theory]
    [InlineData("company")][InlineData("actor")][InlineData("permission")][InlineData("cancel")]
    public async Task Purpose_final_normal_completing_consumer_cannot_release_changed_context(string fault)
    {
        var f=new Fixture();var reader=new DecisionStore(DecisionSource());var service=new AccountingSourceVoucherServiceV1(reader,f.Commands,f.Authority,f.Company,f.User,new Correlation());
        using var cts=new CancellationTokenSource();var callbacks=0;
        Task Fence(CancellationToken _){if(++callbacks==3){if(fault=="company")f.Company.Set(Guid.NewGuid());if(fault=="actor")f.User.UserId=Maker.ToString("D");if(fault=="permission")f.Authority.Granted=false;if(fault=="cancel")cts.Cancel();}return Task.CompletedTask;}
        if(fault=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>service.ReadDecisionAsync(Invoice,Fence,cts.Token));
        else{var answer=await service.ReadDecisionAsync(Invoice,Fence,cts.Token);Assert.Equal(AccountingApiStatus.Denied,answer.Status);Assert.Null(answer.Resource);}
        Assert.Equal(0,reader.Writes);
    }
    [Theory]
    [InlineData("null-record")][InlineData("foreign")][InlineData("amount")][InlineData("current-core")][InlineData("unknown-status")][InlineData("maker")]
    public async Task Purpose_missing_or_mismatched_owner_facts_never_become_human_success(string fault)
    {
        var f=new Fixture();var s=DecisionSource();
        if(fault=="foreign")s=s with{StoredVoucher=s.StoredVoucher with{CompanyId=Guid.NewGuid()}};
        if(fault=="amount")s=s with{CommercialAmounts=s.CommercialAmounts with{FunctionalPayable=121}};
        if(fault=="current-core")s=s with{CurrentVoucher=s.CurrentVoucher with{FinalizedVersion=5}};
        if(fault=="unknown-status")s=s with{CurrentVoucher=s.CurrentVoucher with{Status="invented"}};
        if(fault=="maker")f.User.UserId=Maker.ToString("D");
        if(fault=="null-record")s=s with{StoredVoucher=null!};
        var reader=new DecisionStore(s);var service=new AccountingSourceVoucherServiceV1(reader,f.Commands,f.Authority,f.Company,f.User,new Correlation());
        var answer=await service.ReadDecisionAsync(Invoice,_=>Task.CompletedTask);
        Assert.Equal(fault=="maker"?AccountingApiStatus.Denied:AccountingApiStatus.Unavailable,answer.Status);Assert.Null(answer.Resource);Assert.Equal(0,reader.Writes);
    }
    [Fact]
    public void Receipt_binds_original_posted_result_after_current_period_closure_and_source_cancellation()
    {
        var source=DecisionSource(true);var result=new AccountingApiResult<AccountingSourceVoucherViewV1>(AccountingApiStatus.Ok,source.StoredVoucher,false,null);
        var command=DecisionCommand(result);var receipt=AccountingSourceVoucherServiceV1.BindSourceVoucherApprovalReceipt(command,source,result,Invoice,1,"SENTETIK-key",Actor);
        Assert.NotNull(receipt);Assert.Equal(2,receipt.ResultVersion);Assert.Equal(1,receipt.ExpectedVersion);
        var current=source with{PeriodOpen=false,SourceCancelled=true,CurrentVoucher=source.CurrentVoucher with{Status="CancellationReversalPending",CancellationId=Guid.NewGuid(),CancellationReason="SENTETIK cancelled"}};
        var replay=result with{IsReplay=true,OriginalCorrelationId=command.OriginalCorrelationId};
        Assert.Equal(receipt,AccountingSourceVoucherServiceV1.BindSourceVoucherApprovalReceipt(command,current,replay,Invoice,1,"SENTETIK-key",Actor));
        Assert.Equal(JsonSerializer.Serialize(result,DecisionJson),JsonSerializer.Serialize(result with{Resource=result.Resource! with{ApprovalReceipt=receipt}},DecisionJson));
        Assert.Equal(AccountingSourceVoucherServiceV1.SourceVoucherOriginalResultDigest(result),AccountingSourceVoucherServiceV1.SourceVoucherOriginalResultDigest(replay));
    }
    [Theory]
    [InlineData("root-extra")][InlineData("resource-extra")][InlineData("private-extra")][InlineData("legacy")]
    [InlineData("actor")][InlineData("key")][InlineData("hash")][InlineData("correlation")][InlineData("version")][InlineData("source")][InlineData("journal")]
    public void Receipt_rejects_unproven_or_altered_original_command_without_reconstructing_current_answer(string fault)
    {
        var source=DecisionSource(true);var answer=new AccountingApiResult<AccountingSourceVoucherViewV1>(AccountingApiStatus.Ok,source.StoredVoucher,false,null);var command=DecisionCommand(answer);
        if(fault.EndsWith("extra",StringComparison.Ordinal)){var json=JsonNode.Parse(command.ResultJson)!.AsObject();if(fault=="root-extra")json["unexpected"]=true;else json["resource"]![fault=="private-extra"?"approvalReceipt":"unexpected"]=true;command=command with{ResultJson=json.ToJsonString()};}
        if(fault=="actor")command=command with{ActorId=Maker.ToString("D")};if(fault=="key")command=command with{IdempotencyKeyHash=new string('A',64)};if(fault=="hash")command=command with{RequestHash=new string('A',64)};if(fault=="correlation")command=command with{OriginalCorrelationId="different"};
        if(fault=="source")source=source with{StoredVoucher=source.StoredVoucher with{FinalizedVersion=5},CurrentVoucher=source.CurrentVoucher with{FinalizedVersion=5}};
        if(fault=="journal")source=source with{OriginalJournalBound=false};
        Assert.Null(AccountingSourceVoucherServiceV1.BindSourceVoucherApprovalReceipt(fault=="legacy"?null:command,source,answer,Invoice,fault=="version"?2:1,"SENTETIK-key",Actor));
    }
    private static AccountingCommandRecord DecisionCommand(AccountingApiResult<AccountingSourceVoucherViewV1> answer)=>new(
        AccountingSourceVoucherServiceV1.SourceVoucherApproveCommand,AccountingSourceVoucherServiceV1.SourceVoucherApprovalKeyHash("SENTETIK-key"),Actor.ToString("D"),
        AccountingSourceVoucherServiceV1.SourceVoucherApprovalPayloadHash(Invoice,1),JsonSerializer.Serialize(answer,DecisionJson),"SENTETIK-correlation");
    private sealed class DecisionStore(AccountingSourceVoucherDecisionSourceV1 source):IAccountingSourceVoucherStoreV1,IAccountingSourceVoucherDecisionStoreV1
    {
        public int Reads,Writes;
        public async Task<AccountingSourceVoucherDecisionSourceV1?> ReadSourceVoucherDecisionAsync(Guid id,Func<CancellationToken,Task> fence,CancellationToken token=default)
        {Reads++;await fence(token);return source;}
        public Task<IReadOnlyList<AccountingSourceVoucherViewV1>> ListAsync(CancellationToken token=default)=>throw new InvalidOperationException("Purpose must not use general list/current plan");
        public Task<AccountingSourceVoucherViewV1?> FindAsync(Guid id,CancellationToken token=default)=>throw new InvalidOperationException("Purpose must not use decorated general Find");
        public Task<AccountingSourceVoucherViewV1> ApproveAsync(Guid id,long version,Guid actor,Func<CancellationToken,Task> fence,AccountingCommandMetadata metadata,CancellationToken token=default){Writes++;throw new InvalidOperationException("Read-only");}
        public Task<AccountingSourceVoucherViewV1> ProposeCancellationReversalAsync(Guid id,ProposeAccountingSourceVoucherReversalV1 request,Guid actor,Func<CancellationToken,Task> fence,AccountingCommandMetadata metadata,CancellationToken token=default){Writes++;throw new InvalidOperationException("Read-only");}
    }
}
