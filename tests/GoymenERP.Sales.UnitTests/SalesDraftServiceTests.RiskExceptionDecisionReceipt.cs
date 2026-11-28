using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Risk_original_receipt_binds_actual_audit_attempt_and_native_body_without_original_http_reconstruction(bool approve)
    {
        var f=RiskReceiptFixture(approve);var receipt=RestoreRiskReceipt(f);Assert.NotNull(receipt);
        Assert.Equal(("1","2",Actor,f.Before),(receipt.ExpectedVersion,receipt.ResultVersion,receipt.ActorId,receipt.Before));
        Assert.Equal(f.After,receipt.After);Assert.Equal(f.Response.OriginalCorrelationId,receipt.OriginalCorrelationId);
        var native=f.Response with{OriginalDecisionReceipt=receipt};using var json=JsonDocument.Parse(JsonSerializer.Serialize(native,SalesDraftJson.Options));
        Assert.Equal(3,json.RootElement.EnumerateObject().Count());Assert.False(json.RootElement.TryGetProperty("originalDecisionReceipt",out _));
        Assert.False(JsonSerializer.Serialize(receipt.Before,SalesDraftJson.Options).Contains("isValidToday",StringComparison.Ordinal));
        Assert.Equal(SalesRiskExceptionDecisionDigestsV1.CurrentResponseDigest(f.Response),receipt.CurrentResponseDigest);
    }
    [Fact]
    public void Risk_dynamic_today_and_current_order_readiness_cannot_rewrite_the_original_decision_digest()
    {
        var f=RiskReceiptFixture(true);var original=RestoreRiskReceipt(f)!;
        var later=f with{Response=f.Response with{Exception=f.Response.Exception with{IsValidToday=false}},Source=f.Source with{OrderVersion=9,OrderState="Cancelled"}};
        var next=RestoreRiskReceipt(later);Assert.NotNull(next);Assert.Equal(original.OriginalDecisionDigest,next.OriginalDecisionDigest);
        Assert.NotEqual(original.CurrentResponseDigest,next.CurrentResponseDigest);Assert.Equal("Approved",next.After.State);Assert.Equal("Cancelled",next.CurrentSource!.OrderState);
    }
    [Theory]
    [InlineData("key")] [InlineData("actor")] [InlineData("hash")] [InlineData("company")] [InlineData("target")] [InlineData("corr")]
    [InlineData("audit-actor")] [InlineData("permission")] [InlineData("audit-version")] [InlineData("unknown-json")] [InlineData("duplicate")]
    [InlineData("missing")] [InlineData("after-state")] [InlineData("version-text")]
    public void Risk_original_receipt_refuses_missing_ambiguous_or_malformed_provenance(string fault)
    {
        var f=RiskReceiptFixture(true);var a=f.Attempt;var audit=f.Audit;var response=f.Response;var source=f.Source;
        if(fault=="key")a.IdempotencyKeyHash=new string('A',64);if(fault=="actor")a.ActorId="SENTETIK-other";if(fault=="hash")a.RequestHash=new string('b',64);
        if(fault=="company")a.CompanyId=Guid.NewGuid();if(fault=="target")a.ExceptionId=Guid.NewGuid();if(fault=="corr")response=response with{OriginalCorrelationId="other"};
        if(fault=="audit-actor")audit=audit with{ActorId="other"};if(fault=="permission")audit=audit with{PermissionCode=SalesPermissionManifestV1.DraftsRead};
        if(fault=="audit-version")audit=audit with{EntityVersionBefore=2};if(fault=="unknown-json")audit=audit with{AfterJson=audit.AfterJson.TrimEnd('}')+",\"private\":true}"};
        if(fault=="after-state")source=source with{Recorded=source.Recorded with{Reason="changed"}};
        if(fault=="version-text")a.RequestHash=SalesRiskExceptionHash.Compute(a.CommandName,a.OrderId,1,new("01"));
        var stored=AuditEvent.Create(new(audit,AuditEventIntegrity.Compute(audit)));
        IReadOnlyList<AuditEvent> events=fault=="missing"?[]:fault=="duplicate"?[stored,stored]:[stored];
        Assert.Null(SalesStore.RestoreOriginalRiskDecisionReceipt(a,events,Actor,f.Key,f.Hash,a.CommandName,response,source));
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Risk_binding_matches_original_key_and_body_with_separate_current_response_and_historical_version(bool approve)
    {
        var f = RiskReceiptFixture(approve); var r = RestoreRiskReceipt(f)!;
        var current = SalesRiskExceptionService.ProjectRiskDecision(f.Source, Day);
        var pending = SalesRiskExceptionService.ProjectRiskDecision(f.Source with { Recorded = f.Before }, Day);
        var proof = SalesRiskExceptionDecisionBindingV1.Proof(pending);
        var snapshot = new WorkApprovalDecisionSnapshotV1(Company, SalesRiskExceptionDecisionBindingV1.Item(proof), "1", true) { SalesRiskExceptionProof = proof };
        var response = f.Response with { OriginalDecisionReceipt = r };
        var command = f.Attempt.CommandName; var request = new SalesDraftStateCommandRequest("1", f.After.RejectionReason);
        Assert.True(SalesRiskExceptionDecisionBindingV1.MatchesOriginalDecisionReceipt(snapshot, response, command, request, "SENTETIK-risk-receipt", Actor));
        var historical = SalesRiskExceptionDecisionBindingV1.Proof(r);
        var retained = new WorkApprovalDecisionSnapshotV1(Company, SalesRiskExceptionDecisionBindingV1.Item(historical), "1", false) { SalesRiskExceptionProof = historical };
        Assert.Equal(("1", "2", false), (retained.SourceVersion, historical.CurrentVersion, retained.Pending));
        Assert.True(SalesRiskExceptionDecisionBindingV1.MatchesOriginalDecisionReceipt(retained, response with { IsReplay = true }, command, request, "SENTETIK-risk-receipt", Actor));
        Assert.False(SalesRiskExceptionDecisionBindingV1.MatchesOriginalDecisionReceipt(retained, response, command, request, "different-key", Actor));
        Assert.False(SalesRiskExceptionDecisionBindingV1.MatchesOriginalDecisionReceipt(retained, response, command, request with { ExpectedVersion = "2" }, "SENTETIK-risk-receipt", Actor));
        Assert.False(SalesRiskExceptionDecisionBindingV1.MatchesOriginalDecisionReceipt(retained, response with { OriginalDecisionReceipt = null }, command, request, "SENTETIK-risk-receipt", Actor));
        Assert.False(SalesRiskExceptionDecisionBindingV1.MatchesOriginalDecisionReceipt(retained, response with { Exception = current.Exception with { Reason = "changed" } }, command, request, "SENTETIK-risk-receipt", Actor));
    }
    private static SalesRiskExceptionOriginalDecisionReceiptV1? RestoreRiskReceipt(RiskReceiptFixtureV1 f)=>SalesStore.RestoreOriginalRiskDecisionReceipt(f.Attempt,
        [AuditEvent.Create(new(f.Audit,AuditEventIntegrity.Compute(f.Audit)))],Actor,f.Key,f.Hash,f.Attempt.CommandName,f.Response,f.Source);
    private static RiskReceiptFixtureV1 RiskReceiptFixture(bool approve)
    {
        var s=RiskDecisionSource();var before=s.Recorded;var at=before.RequestedAtUtc.AddHours(1).AddTicks(1);
        var command=approve?SalesRiskExceptionCommands.ApproveV1:SalesRiskExceptionCommands.RejectV1;
        var native=SalesRiskExceptionDecisionDigestsV1.Restore(Company,before);if(approve)native.Approve(1,Actor,at,Day);else native.Reject(1,Actor,"  SENTETIK ret  ",at);
        var after=before with{State=approve?"Approved":"Rejected",Version=2,DecidedBy=Actor,DecidedAtUtc=at,RejectionReason=approve?null:"  SENTETIK ret  ",ValidOn=approve?Day:null};
        var source=s with{Recorded=after};var response=new SalesRiskExceptionCommandResult(SalesRiskExceptionService.ProjectRiskDecision(source,Day).Exception,false,"SENTETIK-risk-original");
        var hash=SalesRiskExceptionHash.Compute(command,before.OrderId,1,new("1",after.RejectionReason));var key=SalesStore.IdempotencyKeyHash("SENTETIK-risk-receipt");
        var attempt=new ShipmentRiskExceptionAttemptRow{CompanyId=Company,ExceptionId=before.Id,OrderId=before.OrderId,CommandName=command,ActorId=Actor,IdempotencyKeyHash=key,RequestHash=hash,
            Outcome=1,CorrelationId=response.OriginalCorrelationId,RecordedAtUtc=at};
        var audit=new AuditEventEnvelope(Guid.NewGuid(),Company,at,approve?"sales.order.risk-exception.approved.v1":"sales.order.risk-exception.rejected.v1","Sales.ShipmentRiskException",
            before.Id.ToString("D"),approve?"ApproveRiskException":"RejectRiskException",1,2,AuditActorTypes.User,Actor,RiskApprovePermission,"[]",JsonSerializer.Serialize(before,SalesDraftJson.Options),
            JsonSerializer.Serialize(after,SalesDraftJson.Options),"SENTETIK karar",null,AuditAiInvolvement.None,null,"Web",null,null,response.OriginalCorrelationId,null,key,AuditOutcomes.Succeeded,1);
        return new(before,after,source,response,attempt,audit,key,hash);
    }
    private sealed record RiskReceiptFixtureV1(SalesRiskExceptionRecordedStateV1 Before,SalesRiskExceptionRecordedStateV1 After,SalesRiskExceptionDecisionSourceV1 Source,
        SalesRiskExceptionCommandResult Response,ShipmentRiskExceptionAttemptRow Attempt,AuditEventEnvelope Audit,string Key,string Hash);
}
