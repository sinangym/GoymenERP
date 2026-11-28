using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.RiskExceptions;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

public static class SalesRiskExceptionDecisionBindingV1
{
    public const string KindName = SalesRiskExceptionWorkInboxSourceV1.KindName;
    public static WorkSalesRiskExceptionProofV1 Proof(SalesRiskExceptionDecisionDetailV1 detail)
    {
        if (!SalesRiskExceptionService.MatchesRiskDecisionDetail(detail)) throw new InvalidOperationException("Risk karar olguları uyuşmuyor.");
        var s = detail.Source!; var r = s.Recorded;
        return new(s.CompanyId,r.Id,r.OrderId,r.ExceptionNo,s.OrderDocumentNo,detail.Exception.Version,detail.Exception.Version,r.RequestedBy,r.RequestedAtUtc,
            r.State=="Requested",detail.ApprovalSupported,detail.RejectionSupported,false,detail.SourceFactsDigest,detail.PreviewDigest,null,null,null);
    }
    public static WorkSalesRiskExceptionProofV1 Proof(SalesRiskExceptionOriginalDecisionReceiptV1 receipt)
    {
        var s = OriginalSource(receipt); var r = s.Recorded;
        return new(s.CompanyId,r.Id,r.OrderId,r.ExceptionNo,s.OrderDocumentNo,receipt.ExpectedVersion,receipt.CurrentSource!.Recorded.Version.ToString(CultureInfo.InvariantCulture),
            r.RequestedBy,r.RequestedAtUtc,false,false,false,true,SalesRiskExceptionDecisionDigestsV1.SourceFactsDigest(s),receipt.OriginalDecisionDigest,
            receipt.OriginalDecisionDigest,receipt.IdempotencyKeyHash,receipt.PayloadHash);
    }
    public static WorkInboxItemV1? Item(SalesRiskExceptionDecisionDetailV1 detail) => SalesRiskExceptionService.MatchesRiskDecisionDetail(detail) ? Item(Proof(detail)) : null;
    public static WorkInboxItemV1 Item(WorkSalesRiskExceptionProofV1 p) => SalesRiskExceptionWorkInboxSourceV1.Project(p.CompanyId,
        [new(p.ExceptionId,p.CompanyId,p.OrderId,p.ExceptionNo,p.OrderDocumentNo,p.RequestedBy,p.RequestedAtUtc)],1).Items.Single();
    public static bool MatchesDecisionProof(WorkInboxItemV1? item, WorkSalesRiskExceptionProofV1? p)
    {
        if(p is null||p.CompanyId==Guid.Empty||p.ExceptionId==Guid.Empty||p.OrderId==Guid.Empty||p.ExceptionNo<1||string.IsNullOrWhiteSpace(p.OrderDocumentNo)
            ||string.IsNullOrWhiteSpace(p.RequestedBy)||!TryVersion(p.Version,out var version)||!TryVersion(p.CurrentVersion,out var current)||current<version
            ||!Digest(p.SourceFactsDigest)||!Digest(p.PreviewDigest)
            ||(p.Historical? current<=version||p.Pending||p.ApprovalSupported||p.RejectionSupported||!Digest(p.OriginalDecisionDigest)||!UpperDigest(p.OriginalKeyHash)||!Digest(p.OriginalPayloadHash)
                :p.OriginalDecisionDigest is not null||p.OriginalKeyHash is not null||p.OriginalPayloadHash is not null||current!=version
                    ||!p.Pending&&(p.ApprovalSupported||p.RejectionSupported)||p.ApprovalSupported&&!p.RejectionSupported))return false;
        try { var expected=Item(p); return SameItem(item,expected); }
        catch(Exception e) when(e is InvalidOperationException or ArgumentException){return false;}
    }
    public static bool MatchesDecisionSnapshot(WorkApprovalDecisionSnapshotV1? snapshot,SalesRiskExceptionDecisionDetailV1? detail)=>snapshot is not null
        &&detail is not null&&SalesRiskExceptionService.MatchesRiskDecisionDetail(detail)&&snapshot.SalesRiskExceptionProof==Proof(detail)
        &&snapshot.CompanyId==detail.CompanyId&&snapshot.SourceVersion==detail.Exception.Version&&snapshot.Pending==(detail.Exception.State=="Requested")
        &&MatchesDecisionProof(snapshot.Item,snapshot.SalesRiskExceptionProof);
    public static bool MatchesOriginalDecisionReceipt(WorkApprovalDecisionSnapshotV1? snapshot,SalesRiskExceptionCommandResult? response,
        string command,SalesDraftStateCommandRequest request,string key,string actor)
    {
        if(snapshot?.SalesRiskExceptionProof is not{} p||response?.OriginalDecisionReceipt is not{CurrentSource:{} current} r||request is null
            ||r.Before is null||r.After is null||current.Recorded is null
            ||snapshot.CompanyId!=p.CompanyId||snapshot.SourceVersion!=p.Version||snapshot.Pending!=p.Pending||!MatchesDecisionProof(snapshot.Item,p)
            ||!TryVersion(request.ExpectedVersion,out var expected)||expected==long.MaxValue||request.ExpectedVersion!=p.Version
            ||!TryVersion(r.ResultVersion,out var result)||result!=expected+1||string.IsNullOrWhiteSpace(key)||string.IsNullOrWhiteSpace(actor)
            ||command is not(SalesRiskExceptionCommands.ApproveV1 or SalesRiskExceptionCommands.RejectV1))return false;
        var approve=command==SalesRiskExceptionCommands.ApproveV1;
        return r.CompanyId==p.CompanyId&&r.ExceptionId==p.ExceptionId&&r.CommandName==command&&r.ActorId==actor&&actor!=p.RequestedBy
            &&r.Before.Id==p.ExceptionId&&r.Before.OrderId==p.OrderId&&r.Before.ExceptionNo==p.ExceptionNo&&r.Before.RequestedBy==p.RequestedBy&&r.Before.RequestedAtUtc==p.RequestedAtUtc
            &&r.Before.State=="Requested"&&r.Before.DecidedBy is null&&r.Before.DecidedAtUtc is null&&r.Before.RejectionReason is null&&r.Before.ValidOn is null
            &&r.After.Id==r.Before.Id&&r.After.OrderId==r.Before.OrderId&&r.After.ExceptionNo==r.Before.ExceptionNo&&r.After.LoadingInstructionId==r.Before.LoadingInstructionId
            &&r.After.Reason==r.Before.Reason&&r.After.RequestedBy==r.Before.RequestedBy&&r.After.RequestedAtUtc==r.Before.RequestedAtUtc
            &&r.Before.Version==expected&&r.After.Version==result&&r.ExpectedVersion==request.ExpectedVersion&&r.After.State==(approve?"Approved":"Rejected")
            &&r.After.DecidedBy==actor&&r.After.DecidedAtUtc==r.RecordedAtUtc&&r.After.RejectionReason==request.Reason
            &&(approve?request.Reason is null:!string.IsNullOrWhiteSpace(request.Reason)&&request.Reason.Length<=SalesShipmentRiskException.MaximumReasonLength)
            &&(p.Historical?p.CurrentVersion==current.Recorded.Version.ToString(CultureInfo.InvariantCulture)&&r.IdempotencyKeyHash==p.OriginalKeyHash&&r.PayloadHash==p.OriginalPayloadHash&&r.OriginalDecisionDigest==p.OriginalDecisionDigest
                :p.Pending&&(approve?p.ApprovalSupported:p.RejectionSupported))
            &&r.IdempotencyKeyHash==DecisionKeyHash(key)&&r.PayloadHash==DecisionPayloadHash(command,p.OrderId,p.ExceptionNo,request)
            &&r.OriginalCorrelationId==response.OriginalCorrelationId&&!string.IsNullOrWhiteSpace(r.OriginalCorrelationId)
            &&r.AuditEventId!=Guid.Empty&&r.AuditIntegrityHash is{Length:64} ah&&ah.All(char.IsAsciiHexDigit)
            &&r.OriginalDecisionDigest==SalesRiskExceptionDecisionDigestsV1.OriginalDecisionDigest(r)
            &&r.CurrentResponseDigest==SalesRiskExceptionDecisionDigestsV1.CurrentResponseDigest(response)
            &&current.CompanyId==p.CompanyId&&current.OrderDocumentNo==p.OrderDocumentNo&&current.Recorded==r.After
            &&SalesRiskExceptionDecisionDigestsV1.MatchesCurrent(current.Recorded,response.Exception)
            &&SalesRiskExceptionDecisionDigestsV1.SourceFactsDigest(OriginalSource(r))==p.SourceFactsDigest;
    }
    private static SalesRiskExceptionDecisionSourceV1 OriginalSource(SalesRiskExceptionOriginalDecisionReceiptV1 receipt)=>
        receipt.CurrentSource is{} current?current with{Recorded=receipt.Before,DroppedByShipmentId=null}:throw new InvalidOperationException("Özgün risk kaynak bağı eksik.");
    public static bool TryVersion(string? value,out long version)=>long.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out version)&&version>0&&version.ToString(CultureInfo.InvariantCulture)==value;
    public static string DecisionKeyHash(string key)=>SalesStore.IdempotencyKeyHash(key);
    public static string DecisionPayloadHash(string command,Guid orderId,int no,SalesDraftStateCommandRequest request)=>SalesRiskExceptionHash.Compute(command,orderId,no,request);
    private static bool Digest(string? value)=>value is{Length:64}&&value.All(c=>c is>='0' and<='9' or>='a' and<='f');
    private static bool UpperDigest(string? value)=>value is{Length:64}&&value.All(c=>c is>='0' and<='9' or>='A' and<='F');
    private static bool SameItem(WorkInboxItemV1? a,WorkInboxItemV1 b)=>a is not null&&a.Module==b.Module&&a.Kind==b.Kind&&a.ItemId==b.ItemId&&a.DocumentLabel==b.DocumentLabel
        &&a.WaitingSinceUtc==b.WaitingSinceUtc&&a.Link==b.Link&&a.PreparedBy==b.PreparedBy&&a.Scope is null&&a.ExcludedDeciders is null
        &&a.Rules is not null&&a.Rules.SequenceEqual(b.Rules)&&a.References is not null&&a.References.SequenceEqual(b.References);
}
