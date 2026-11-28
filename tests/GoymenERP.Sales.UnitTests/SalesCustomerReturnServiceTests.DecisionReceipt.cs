using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.CustomerReturns;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using static GoymenERP.Sales.UnitTests.SalesCustomerReturnFixtures;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesCustomerReturnServiceTests
{
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Return_receipt_binds_real_native_attempt_and_audit_decision_not_an_invented_persisted_result_JSON(bool approve)
    {
        var f = await ReturnReceiptFixture(approve); var r = Assert.IsType<SalesCustomerReturnOriginalReceiptV1>(RestoreReturn(f));
        Assert.Equal((2L, 3L, Approver, f.Attempt.CorrelationId), (r.ExpectedVersion, r.ResultVersion, r.ActorId, r.OriginalCorrelationId));
        Assert.Equal(SalesCustomerReturnDecisionDigestsV1.OriginalDecisionDigest(r), r.OriginalDecisionDigest);
        Assert.Equal(SalesCustomerReturnDecisionDigestsV1.CurrentResponseDigest(f.Answer), r.CurrentResponseDigest);
        Assert.Equal(JsonSerializer.Serialize(f.Answer, SalesDraftJson.Options), JsonSerializer.Serialize(f.Answer with { OriginalDecisionReceipt = r }, SalesDraftJson.Options));
        Assert.Equal(approve ? null : "SENTETIK ret", r.OriginalDecision.RejectionReason);
    }
    [Theory]
    [InlineData("company")] [InlineData("actor")] [InlineData("key")] [InlineData("payload")] [InlineData("command")]
    [InlineData("target")] [InlineData("time")] [InlineData("refusal")] [InlineData("version")] [InlineData("correlation")]
    [InlineData("unknown-before")] [InlineData("unknown-after")] [InlineData("private-proof")] [InlineData("changed-total")] [InlineData("changed-maker")]
    public async Task Return_receipt_rejects_mismatched_or_unknown_native_artifacts_even_with_recomputed_audit_integrity(string fault)
    {
        var f = await ReturnReceiptFixture(true); var a = f.Attempt; var audit = f.Audit;
        switch (fault)
        {
            case "company": a.CompanyId = Guid.NewGuid(); break; case "actor": a.ActorId = "SENTETIK-other"; break;
            case "key": a.IdempotencyKeyHash = new('a', 64); break; case "payload": a.RequestHash = new('b', 64); break;
            case "command": a.CommandName = SalesCustomerReturnCommands.SubmitV1; break; case "target": a.TargetId = Guid.NewGuid(); break;
            case "time": a.RecordedAtUtc = a.RecordedAtUtc.AddTicks(1); break; case "refusal": a.RefusalJson = "{}"; break;
            case "version": audit = audit with { EntityVersionAfter = 4 }; break; case "correlation": audit = audit with { CorrelationId = "SENTETIK-other" }; break;
            default:
                var before = fault == "unknown-before"; var json = JsonNode.Parse(before ? audit.BeforeJson : audit.AfterJson)!.AsObject();
                if (fault is "unknown-before" or "unknown-after") json["unexpected"] = true;
                if (fault == "private-proof") json["originalDecisionReceipt"] = new JsonObject();
                if (fault == "changed-total") json["lineTotalAmount"] = 999;
                if (fault == "changed-maker") json["createdBy"] = "SENTETIK-other";
                audit = before ? audit with { BeforeJson = json.ToJsonString() } : audit with { AfterJson = json.ToJsonString() }; break;
        }
        Assert.Null(RestoreReturn(f with { Audit = audit }));
    }
    [Fact]
    public async Task Return_original_approved_decision_survives_current_finalized_projection_without_relabeling_the_native_replay()
    {
        var f = await ReturnReceiptFixture(true); var facts = f.Item.Facts with { FinalizedBy = "SENTETIK-finalizer", FinalizedAtUtc = Now.AddMinutes(2) };
        var later = SalesCustomerReturn.Restore(f.Item.Id, f.Item.CompanyId, f.Item.Header, f.Item.ShipmentId, f.Item.CustomerDocumentNo, f.Item.CustomerDocumentDate,
            SalesCustomerReturnState.Finalized, facts, f.Item.CreatedBy, f.Item.CreatedAtUtc, 4, Now.AddMinutes(2), f.Item.Lines);
        var current = SalesCustomerReturnService.View(later); var observed = f.Answer with { CustomerReturn = current, IsReplay = true };
        var r = Assert.IsType<SalesCustomerReturnOriginalReceiptV1>(RestoreReturn(f with { Item = later, Answer = observed }));
        Assert.Equal(("Approved", "3", "Finalized", "4"), (r.OriginalDecision.State, r.OriginalDecision.Version, observed.CustomerReturn.State, observed.CustomerReturn.Version));
        Assert.Equal(SalesCustomerReturnDecisionDigestsV1.SourceFactsDigest(Company, current), r.SourceFactsDigest);
    }
    private static SalesCustomerReturnOriginalReceiptV1? RestoreReturn(ReturnReceiptFixtureV1 f) => SalesStore.RestoreCustomerReturnDecisionReceipt(f.Attempt,
        AuditEvent.Create(new(f.Audit, AuditEventIntegrity.Compute(f.Audit))), f.Item, 2, Approver, f.Request, f.Answer);
    private static async Task<ReturnReceiptFixtureV1> ReturnReceiptFixture(bool approve, string version = "02")
    {
        var f = SafetyWorld(); var before = ReturnNativeAuditPicture(f.Store.Stored!); var request = new SalesDraftStateCommandRequest(version, approve ? null : "  SENTETIK ret  ");
        var answer = await DecideSafe(f, request, approve); var item = f.Store.Stored!; var command = approve ? SalesCustomerReturnCommands.ApproveV1 : SalesCustomerReturnCommands.RejectV1;
        var key = SalesStore.IdempotencyKeyHash(Metadata.IdempotencyKey);
        var attempt = new CustomerReturnAttemptRow { CompanyId = Company, TargetId = ReturnId, CustomerReturnId = ReturnId, CommandName = command, ActorId = Approver,
            IdempotencyKeyHash = key, RequestHash = approve ? SalesCustomerReturnHash.Compute(command, ReturnId, request) : SalesCustomerReturnHash.ComputeDecision(command, ReturnId, request),
            Outcome = 1, CorrelationId = Metadata.CorrelationId, RecordedAtUtc = Now };
        var audit = new AuditEventEnvelope(Guid.NewGuid(), Company, Now, approve ? "sales.customer-return.approved.v1" : "sales.customer-return.rejected.v1", "Sales.CustomerReturn",
            ReturnId.ToString("D"), approve ? "ApproveCustomerReturn" : "RejectCustomerReturn", 2, 3, AuditActorTypes.User, Approver, SalesPermissionManifestV1.ReturnsApprove,
            "[]", before, ReturnNativeAuditPicture(item), "SENTETIK decision", null, AuditAiInvolvement.None, null, "Web", null, null, Metadata.CorrelationId, null, key, AuditOutcomes.Succeeded, 1);
        return new(item, request, answer, attempt, audit);
    }
    // Differential golden of the EXISTING native CustomerReturnAuditState summary, without another calculator.
    private static string ReturnNativeAuditPicture(SalesCustomerReturn item) => JsonSerializer.Serialize(new
    { item.Id, item.Header.OriginalInvoiceId, item.Header.OriginalDocumentNo, item.ShipmentId, item.CustomerDocumentNo, item.CustomerDocumentDate,
        state = item.State.ToString(), version = item.Version, item.CreatedBy, item.Facts.SubmittedBy, item.Facts.ApprovedBy, item.Facts.FinalizedBy,
        item.Facts.RejectedBy, item.Facts.RejectionReason, item.Facts.CancelledBy, item.Facts.CancellationReason, lineCount = item.Lines.Count,
        lineTotalAmount = item.Totals.LineTotal.Amount, currencyId = item.Header.CurrencyId, item.NeedsReview }, SalesDraftJson.Options);
    private sealed record ReturnReceiptFixtureV1(SalesCustomerReturn Item, SalesDraftStateCommandRequest Request, SalesCustomerReturnCommandResult Answer,
        CustomerReturnAttemptRow Attempt, AuditEventEnvelope Audit);
}
