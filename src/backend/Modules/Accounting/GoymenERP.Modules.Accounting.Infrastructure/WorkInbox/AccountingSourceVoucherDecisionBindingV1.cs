using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

/// <summary>One native formatter for list, current owner detail and genuine original-result matching.</summary>
public static class AccountingSourceVoucherDecisionBindingV1
{
    public const string ModuleName="accounting";
    public const string KindName="accounting.source-voucher.v1";
    public const string ScreenPath="/muhasebe/kaynak-fisler";
    public const string PurposePath="/onaylarim/accounting.source-voucher.v1";
    public const string ApprovalRequiredRule="accounting.source-voucher-approval-required";
    public const string PeriodWaitingRule="accounting.source-voucher-period-waiting";
    public static WorkInboxItemV1? Item(Guid id,Guid source,Guid invoice,Guid maker,Guid party,Guid currency,
        DateTimeOffset created,string? officialNo,decimal payable,bool periodOpen)
    {
        if(id==Guid.Empty||source==Guid.Empty||invoice==Guid.Empty||maker==Guid.Empty||party==Guid.Empty||currency==Guid.Empty)return null;
        var rules=new List<WorkApprovalRuleResultV1>(2){new(ApprovalRequiredRule,null,WorkInboxRulesV1.Decimal(payable),null,null)};
        if(!periodOpen)rules.Add(new(PeriodWaitingRule,null,null,null,null));
        var label=AccountingWorkInboxFence.Label(officialNo,invoice);
        return new(ModuleName,KindName,id,label,created.ToUniversalTime(),ScreenPath,AccountingWorkInboxFence.Actor(maker),rules,
            [new("invoice",label,invoice,null),new("source-document",AccountingWorkInboxFence.ShortReference(source),source,null),
             new("party",AccountingWorkInboxFence.ShortReference(party),party,null),new("currency",AccountingWorkInboxFence.ShortReference(currency),currency,null)]);
    }
    public static WorkInboxItemV1? FromView(AccountingSourceVoucherViewV1 view,DateTimeOffset created,bool periodOpen)=>
        Amount(view.FunctionalPayable,out var payable)?Item(view.Id,view.SourceDocumentId,view.InvoiceId,view.SourceActorId,
            view.PartyId,view.DocumentCurrencyId,created,view.OfficialInvoiceNo,payable,periodOpen):null;
    /// <summary>Scalar identity/current rule shape only; payable and opaque source hashes need full owner or genuine receipt.</summary>
    public static bool MatchesDecisionProof(WorkInboxItemV1? item,WorkSourceVoucherProofV1? p)=>
        p is not null&&p.CompanyId!=Guid.Empty&&p.FinalizedVersion>0&&!string.IsNullOrWhiteSpace(p.DocumentKind)
        &&p.CreatedAtUtc.Offset==TimeSpan.Zero&&Status(p.Status)&&Digest(p.SourceFactsDigest)&&Digest(p.PreviewDigest)
        &&item?.Rules is {Count:>=1 and <=2} rules&&rules[0] is {RuleCode:ApprovalRequiredRule,Threshold:null,Deviation:null,Unit:null} rule
        &&Amount(rule.Actual,out var amount)&&WorkInboxRulesV1.Decimal(amount)==rule.Actual
        &&Item(p.VoucherId,p.SourceDocumentId,p.InvoiceId,p.SourceActorId,p.PartyId,p.CurrencyId,p.CreatedAtUtc,p.OfficialInvoiceNo,amount,p.PeriodOpen) is {} expected&&Same(item,expected);
    public static bool MatchesDecisionItem(WorkInboxItemV1? item,AccountingSourceVoucherDecisionDetailV1? detail)=>
        detail is not null&&ValidDetail(detail)&&FromView(detail.Voucher,detail.CreatedAtUtc,detail.PeriodOpen) is {} expected&&Same(item,expected);
    // An original command never proves historical period readiness. Only the primary immutable payable/refs bind here;
    // the optional second rule is current presentation and must have passed MatchesDecisionProof before native dispatch.
    public static bool MatchesOriginalDecisionItem(WorkInboxItemV1? item,AccountingSourceVoucherViewV1? original,
        AccountingSourceVoucherApprovalReceiptV1? receipt)=>original is not null&&receipt is not null
        &&original.CompanyId==receipt.CompanyId&&receipt.CompanyId!=Guid.Empty&&original.Id==receipt.VoucherId
        &&original.SourceDocumentId==receipt.SourceDocumentId&&original.InvoiceId==receipt.InvoiceId&&original.SourceActorId==receipt.SourceActorId
        &&original.Status=="Posted"&&receipt.ExpectedVersion==1&&receipt.ResultVersion==2&&original.Version==2
        &&receipt.ActorId!=Guid.Empty&&receipt.ActorId!=receipt.SourceActorId&&original.ApprovedBy==receipt.ActorId
        &&original.ApprovedAtUtc==receipt.ApprovedAtUtc&&original.JournalId==receipt.JournalId&&receipt.JournalId==original.Id
        &&receipt.CommandName==AccountingSourceVoucherServiceV1.SourceVoucherApproveCommand&&Digest(receipt.IdempotencyKeyHash)
        &&receipt.PayloadHash==AccountingSourceVoucherServiceV1.SourceVoucherApprovalPayloadHash(original.Id,1)
        &&Digest(receipt.SourceFactsDigest)&&receipt.OriginalResultDigest==AccountingSourceVoucherServiceV1.SourceVoucherOriginalResultDigest(new(AccountingApiStatus.Ok,original,false,null))
        &&!string.IsNullOrWhiteSpace(receipt.OriginalCorrelationId)&&item?.Rules is {Count:>=1 and <=2} rules
        &&FromView(original,receipt.CreatedAtUtc,rules.Count==1) is {} expected&&Same(item,expected);
    public static bool ValidDetail(AccountingSourceVoucherDecisionDetailV1? detail)=>detail?.Voucher is {} v
        &&v.CompanyId!=Guid.Empty&&v.Id!=Guid.Empty&&v.SourceDocumentId!=Guid.Empty&&v.InvoiceId!=Guid.Empty&&v.SourceActorId!=Guid.Empty
        &&v.PartyId!=Guid.Empty&&v.DocumentCurrencyId!=Guid.Empty&&v.FinalizedVersion>0&&v.MappingVersion>0&&v.Version>0
        &&Status(v.Status)&&detail.CreatedAtUtc.Offset==TimeSpan.Zero&&Digest(detail.SourceFactsDigest)&&Digest(detail.PreviewDigest)
        &&detail.PreviewDigest==AccountingSourceVoucherServiceV1.SourceVoucherPreviewDigest(v,detail.CreatedAtUtc,detail.SourceCancelled,detail.PeriodOpen)
        &&detail.SourceCancelled==(v.CancellationId is not null)
        &&(v.Status is not ("AwaitingApproval" or "PeriodWaiting")||!detail.SourceCancelled&&v.Version==1&&v.Status==(detail.PeriodOpen?"AwaitingApproval":"PeriodWaiting")&&v.ApprovedBy is null&&v.ApprovedAtUtc is null&&v.JournalId is null)
        &&v.Lines is {Count:>0}
        &&v.Lines.All(x=>x is not null&&x.LineNumber>0&&x.Side is 1 or 2&&x.AccountId!=Guid.Empty&&!string.IsNullOrWhiteSpace(x.MappingKey)&&Amount(x.FunctionalAmount,out _))
        &&v.Lines.Select(x=>x.LineNumber).Distinct().Count()==v.Lines.Count&&Amount(v.FunctionalPayable,out _);
    private static bool Status(string? value)=>value is "PeriodWaiting" or "AwaitingApproval" or "Posted" or "Cancelled" or "CancellationReversalPending" or "CancelledByReversal";
    private static bool Digest(string? value)=>value is {Length:64}&&value.All(c=>c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static bool Amount(string? value,out decimal amount)=>AccountingWorkInboxFence.TryAmount(value,out amount)
        &&value==amount.ToString("0.############################",CultureInfo.InvariantCulture);
    private static bool Same(WorkInboxItemV1? item,WorkInboxItemV1 expected)=>item is not null&&item.Module==expected.Module&&item.Kind==expected.Kind
        &&item.ItemId==expected.ItemId&&item.DocumentLabel==expected.DocumentLabel&&item.WaitingSinceUtc==expected.WaitingSinceUtc&&item.Link==expected.Link
        &&item.PreparedBy==expected.PreparedBy&&item.Scope is null&&item.Rules is not null&&item.References is not null
        &&item.Rules.SequenceEqual(expected.Rules)&&item.References.SequenceEqual(expected.References)&&(item.ExcludedDeciders??[]).SequenceEqual(expected.ExcludedDeciders??[]);
}
