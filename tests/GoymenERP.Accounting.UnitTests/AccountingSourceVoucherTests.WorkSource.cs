using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class AccountingSourceVoucherTests
{
    private static WorkInboxSourceContextV1 VoucherContext()=>new(Company,Actor.ToString("D"),"SENTETIK-source",null);
    private static AccountingSourceVoucherDecisionDetailV1 VoucherDetail(bool periodOpen=true,bool approved=false)
    {
        var s=DecisionSource(approved);var v=s.CurrentVoucher with{Status=approved?"Posted":periodOpen?"AwaitingApproval":"PeriodWaiting"};
        return new(v,s.CreatedAtUtc,false,periodOpen,AccountingSourceVoucherServiceV1.SourceVoucherSourceFactsDigest(s),
            AccountingSourceVoucherServiceV1.SourceVoucherPreviewDigest(v,s.CreatedAtUtc,false,periodOpen));
    }
    private static AccountingDbContext VoucherWorkDb(Fixture f)=>new(new DbContextOptionsBuilder<AccountingDbContext>()
        .UseInMemoryDatabase("SENTETIK-voucher-purpose-"+Guid.NewGuid().ToString("N")).Options,f.Company);
    [Theory][InlineData(true)][InlineData(false)]
    public async Task Work_source_reuses_native_formatter_actual_maker_and_exact_current_period_rule_without_private_wire(bool periodOpen)
    {
        var f=new Fixture();await using var db=VoucherWorkDb(f);var detail=VoucherDetail(periodOpen);var owner=new VoucherWorkOwner(detail);
        var source=new AccountingSourceVoucherWorkInboxSourceV1(db,new(f.Company,f.User,f.Authority),owner);
        var snapshot=Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(VoucherContext(),Invoice,default));
        Assert.True(snapshot.Pending);Assert.Equal("1",snapshot.SourceVersion);Assert.Equal(Maker.ToString("D"),snapshot.Item.PreparedBy);
        Assert.Equal(AccountingSourceVoucherWorkInboxSourceV1.ScreenPath,snapshot.Item.Link);
        var proof=Assert.IsType<WorkSourceVoucherProofV1>(snapshot.SourceVoucherProof);Assert.Equal(periodOpen,proof.PeriodOpen);
        Assert.Equal(periodOpen?1:2,snapshot.Item.Rules.Count);Assert.Equal("120",snapshot.Item.Rules[0].Actual);
        if(!periodOpen)Assert.Equal(AccountingSourceVoucherWorkInboxSourceV1.PeriodWaitingRule,snapshot.Item.Rules[1].RuleCode);
        Assert.Equal(new[]{"invoice","source-document","party","currency"},snapshot.Item.References.Select(x=>x.Role));
        Assert.True(AccountingSourceVoucherDecisionBindingV1.MatchesDecisionProof(snapshot.Item,proof));
        Assert.True(AccountingSourceVoucherDecisionBindingV1.MatchesDecisionItem(snapshot.Item,detail));
        var json=JsonSerializer.Serialize(snapshot,DecisionJson);Assert.DoesNotContain("sourceVoucherProof",json);Assert.DoesNotContain(detail.SourceFactsDigest,json);
        Assert.DoesNotContain("lines",json);Assert.DoesNotContain("functionalAmount",json);Assert.Equal(1,owner.Reads);Assert.False(db.ChangeTracker.HasChanges());
    }
    [Fact]
    public async Task Work_source_excludes_the_actual_source_invoice_finalizer_even_when_protocol_owner_would_return_it()
    {
        var f=new Fixture();await using var db=VoucherWorkDb(f);var d=VoucherDetail();var v=d.Voucher with{SourceActorId=Actor};
        d=d with{Voucher=v,PreviewDigest=AccountingSourceVoucherServiceV1.SourceVoucherPreviewDigest(v,d.CreatedAtUtc,false,true)};
        var owner=new VoucherWorkOwner(d);var source=new AccountingSourceVoucherWorkInboxSourceV1(db,new(f.Company,f.User,f.Authority),owner);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>source.ReadDecisionAsync(VoucherContext(),Invoice,default));Assert.Equal(1,owner.Reads);Assert.False(db.ChangeTracker.HasChanges());
    }
    [Theory][InlineData("company")][InlineData("id")][InlineData("null-view")][InlineData("preview")][InlineData("digest")][InlineData("status")][InlineData("period")]
    public async Task Work_source_refuses_unbound_owner_detail_before_making_a_snapshot(string fault)
    {
        var f=new Fixture();await using var db=VoucherWorkDb(f);var d=VoucherDetail();
        d=fault switch{"company"=>d with{Voucher=d.Voucher with{CompanyId=Guid.NewGuid()}},"id"=>d with{Voucher=d.Voucher with{Id=Guid.NewGuid()}},
            "null-view"=>d with{Voucher=null!},"preview"=>d with{PreviewDigest=new('B',64)},"digest"=>d with{SourceFactsDigest="absent"},
            "status"=>d with{Voucher=d.Voucher with{Status="unknown"}},_=>d with{PeriodOpen=false}};
        var source=new AccountingSourceVoucherWorkInboxSourceV1(db,new(f.Company,f.User,f.Authority),new VoucherWorkOwner(d));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>source.ReadDecisionAsync(VoucherContext(),Invoice,default));Assert.False(db.ChangeTracker.HasChanges());
    }
    [Theory][InlineData("permission")][InlineData("company")][InlineData("actor")][InlineData("cancel")]
    public async Task Work_source_rechecks_owner_await_and_completed_cancellation_without_more_access(string fault)
    {
        var f=new Fixture();await using var db=VoucherWorkDb(f);using var cancel=new CancellationTokenSource();
        var owner=new VoucherWorkOwner(VoucherDetail()){AfterRead=()=>{if(fault=="permission")f.Authority.Granted=false;if(fault=="company")f.Company.Set(Guid.NewGuid());if(fault=="actor")f.User.UserId=Maker.ToString("D");if(fault=="cancel")cancel.Cancel();}};
        var source=new AccountingSourceVoucherWorkInboxSourceV1(db,new(f.Company,f.User,f.Authority),owner);
        if(fault=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>source.ReadDecisionAsync(VoucherContext(),Invoice,cancel.Token));
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>source.ReadDecisionAsync(VoucherContext(),Invoice,cancel.Token));
        Assert.Equal(1,owner.Reads);Assert.False(db.ChangeTracker.HasChanges());
    }
    [Theory][InlineData(AccountingApiStatus.Denied)][InlineData(AccountingApiStatus.NotFound)][InlineData(AccountingApiStatus.Unavailable)]
    public async Task Work_source_preserves_owner_refusal_and_requires_a_registered_narrow_owner(AccountingApiStatus status)
    {
        var f=new Fixture();await using var db=VoucherWorkDb(f);var fence=new AccountingWorkInboxFence(f.Company,f.User,f.Authority);
        var source=new AccountingSourceVoucherWorkInboxSourceV1(db,fence,new VoucherWorkOwner(null){Status=status});
        if(status==AccountingApiStatus.NotFound)Assert.Null(await source.ReadDecisionAsync(VoucherContext(),Invoice,default));
        else if(status==AccountingApiStatus.Denied)await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>source.ReadDecisionAsync(VoucherContext(),Invoice,default));
        else await Assert.ThrowsAsync<InvalidOperationException>(()=>source.ReadDecisionAsync(VoucherContext(),Invoice,default));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new AccountingSourceVoucherWorkInboxSourceV1(db,fence).ReadDecisionAsync(VoucherContext(),Invoice,default));
    }
    [Theory][InlineData("caption")][InlineData("maker")][InlineData("time")][InlineData("module")][InlineData("kind")][InlineData("link")]
    [InlineData("ref-id")][InlineData("ref-label")][InlineData("ref-role")][InlineData("rule")][InlineData("threshold")][InlineData("unit")][InlineData("deviation")][InlineData("period-rule")]
    public void Work_scalar_binding_refuses_altered_identity_references_and_current_period_rule(string fault)
    {
        var d=VoucherDetail(false);var v=d.Voucher;var item=AccountingSourceVoucherDecisionBindingV1.FromView(v,d.CreatedAtUtc,false)!;
        var proof=new WorkSourceVoucherProofV1(v.CompanyId,v.Id,v.SourceDocumentId,v.InvoiceId,v.SourceActorId,d.CreatedAtUtc,v.DocumentKind,v.FinalizedVersion,v.PartyId,v.DocumentCurrencyId,v.Status,false,false,d.SourceFactsDigest,d.PreviewDigest,v.OfficialInvoiceNo);
        var refs=item.References.ToArray();var rules=item.Rules.ToArray();
        if(fault=="ref-id")refs[0]=refs[0] with{RecordId=Guid.NewGuid()};if(fault=="ref-label")refs[2]=refs[2] with{Label="incorrect"};if(fault=="ref-role")refs[3]=refs[3] with{Role="wrong"};
        if(fault=="rule")rules[0]=rules[0] with{RuleCode="other"};if(fault=="threshold")rules[0]=rules[0] with{Threshold="1"};if(fault=="unit")rules[0]=rules[0] with{Unit="TRY"};if(fault=="deviation")rules[0]=rules[0] with{Deviation="1"};if(fault=="period-rule")rules[1]=rules[1] with{Actual="1"};
        item=item with{References=refs,Rules=rules};item=fault switch{"caption"=>item with{DocumentLabel="incorrect"},"maker"=>item with{PreparedBy=Actor.ToString("D")},"time"=>item with{WaitingSinceUtc=d.CreatedAtUtc.AddTicks(1)},"module"=>item with{Module="finance"},"kind"=>item with{Kind="accounting.normal-journal-draft.v1"},"link"=>item with{Link="/other"},_=>item};
        Assert.False(AccountingSourceVoucherDecisionBindingV1.MatchesDecisionProof(item,proof));Assert.False(AccountingSourceVoucherDecisionBindingV1.MatchesDecisionItem(item,d));
    }
    [Fact]
    public void Work_primary_payable_is_authenticated_by_full_owner_and_original_receipt_not_scalar_echo()
    {
        var d=VoucherDetail();var v=d.Voucher;var item=AccountingSourceVoucherDecisionBindingV1.FromView(v,d.CreatedAtUtc,true)!;
        var altered=item with{Rules=[item.Rules[0] with{Actual="121"}]};
        var proof=new WorkSourceVoucherProofV1(v.CompanyId,v.Id,v.SourceDocumentId,v.InvoiceId,v.SourceActorId,d.CreatedAtUtc,v.DocumentKind,v.FinalizedVersion,v.PartyId,v.DocumentCurrencyId,v.Status,false,true,d.SourceFactsDigest,d.PreviewDigest,v.OfficialInvoiceNo);
        Assert.True(AccountingSourceVoucherDecisionBindingV1.MatchesDecisionProof(altered,proof)); // Scalar has no payable; not a full certification.
        Assert.False(AccountingSourceVoucherDecisionBindingV1.MatchesDecisionItem(altered,d));
        var source=DecisionSource(true);var original=source.StoredVoucher;var answer=new AccountingApiResult<AccountingSourceVoucherViewV1>(AccountingApiStatus.Ok,original,false,null);
        var receipt=AccountingSourceVoucherServiceV1.BindSourceVoucherApprovalReceipt(DecisionCommand(answer),source,answer,Invoice,1,"SENTETIK-key",Actor)!;
        var closedPeriodItem=AccountingSourceVoucherDecisionBindingV1.FromView(original,source.CreatedAtUtc,false)!;
        Assert.True(AccountingSourceVoucherDecisionBindingV1.MatchesOriginalDecisionItem(closedPeriodItem,original,receipt));
        var originalItem=AccountingSourceVoucherDecisionBindingV1.FromView(original,source.CreatedAtUtc,true)!;
        Assert.False(AccountingSourceVoucherDecisionBindingV1.MatchesOriginalDecisionItem(originalItem with{Rules=[originalItem.Rules[0] with{Actual="121"}]},original,receipt));
        Assert.False(AccountingSourceVoucherDecisionBindingV1.MatchesOriginalDecisionItem(closedPeriodItem,original,receipt with{ActorId=Maker}));
    }
    private sealed class VoucherWorkOwner(AccountingSourceVoucherDecisionDetailV1? detail):IAccountingSourceVoucherDecisionReadV1
    {
        public int Reads;public Action? AfterRead;public AccountingApiStatus Status=AccountingApiStatus.Ok;
        public async Task<AccountingApiResult<AccountingSourceVoucherDecisionDetailV1>> ReadDecisionAsync(Guid id,Func<CancellationToken,Task> fence,CancellationToken token=default)
        {Reads++;await fence(token);AfterRead?.Invoke();return new(Status,detail,false,null);}
    }
}
