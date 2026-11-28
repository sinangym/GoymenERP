using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Remainder_pure_receipt_binds_native_raw_body_original_version_and_genuine_audit_without_fabricating_an_original_full_response(bool approve)
    {
        var f = await RemainderReceiptFixture(approve); var receipt = Assert.IsType<SalesRemainderOriginalReceiptV1>(RestoreRemainder(f));
        Assert.Equal((6L, 7L, 1, Actor), (receipt.ExpectedVersion, receipt.ResultVersion, receipt.ChangeNo, receipt.ActorId));
        Assert.Equal(SalesRemainderDecisionDigestsV1.OriginalDecisionDigest(receipt), receipt.OriginalDecisionDigest);
        Assert.Equal(SalesRemainderDecisionDigestsV1.CurrentResponseDigest(f.Answer), receipt.CurrentResponseDigest);
        var proven = f.Answer with { OriginalRemainderDecisionReceipt = receipt };
        Assert.Equal(JsonSerializer.Serialize(f.Answer, SalesDraftJson.Options), JsonSerializer.Serialize(proven, SalesDraftJson.Options));
        var privateWire = JsonSerializer.SerializeToElement(receipt, SalesDraftJson.Options); Assert.False(privateWire.TryGetProperty("draftCreatedBy", out _)); Assert.False(privateWire.TryGetProperty("creatorProofEstablished", out _));
        Assert.Equal(approve ? null : "SENTETIK ret", receipt.OriginalChange.DecisionReason);
    }

    [Theory]
    [InlineData("company")] [InlineData("actor")] [InlineData("key")] [InlineData("payload")] [InlineData("command")]
    [InlineData("refusal")] [InlineData("result-json")] [InlineData("decision-version")] [InlineData("decision-correlation")]
    [InlineData("unknown-root")] [InlineData("unknown-change")] [InlineData("unknown-line")] [InlineData("changed-line")]
    [InlineData("changed-requester")] [InlineData("observed-correlation")]
    public async Task Remainder_pure_receipt_rejects_malformed_or_cross_bound_native_artifacts_even_with_a_valid_audit_integrity_hash(string fault)
    {
        var f = await RemainderReceiptFixture(true); var attempt = f.Attempt; var decision = f.Decision; var envelope = f.Audit; var observed = f.Answer;
        switch (fault)
        {
            case "company": attempt.CompanyId = Guid.NewGuid(); break;
            case "actor": attempt.ActorId = "SENTETIK other"; break;
            case "key": attempt.IdempotencyKeyHash = new('a', 64); break;
            case "payload": attempt.RequestHash = new('b', 64); break;
            case "command": attempt.CommandName = SalesDraftCommands.RequestRemainderChangeV1; break;
            case "refusal": attempt.RefusalJson = "{}"; break;
            case "result-json": attempt.ResultJson = "{}"; break;
            case "decision-version": decision.DraftVersion++; break;
            case "decision-correlation": decision.CorrelationId = "SENTETIK other"; break;
            case "observed-correlation": observed = observed with { OriginalCorrelationId = "SENTETIK other" }; break;
            default:
                var json = JsonNode.Parse(envelope.AfterJson)!.AsObject();
                if (fault == "unknown-root") json["unexpected"] = true;
                if (fault == "unknown-change") json["change"]!["approvalReceipt"] = new JsonObject();
                if (fault == "unknown-line") json["remainder"]!["lines"]![0]!["unexpected"] = true;
                if (fault == "changed-line") json["remainder"]!["lines"]![0]!["quantity"] = "999";
                if (fault == "changed-requester") json["change"]!["reason"] = "SENTETIK other";
                envelope = envelope with { AfterJson = json.ToJsonString() }; break;
        }
        var audit = AuditEvent.Create(new(envelope, AuditEventIntegrity.Compute(envelope)));
        Assert.Null(SalesStore.RestoreOriginalRemainderReceipt(attempt, audit, decision, f.Order, 6, 1, Actor, f.Request, observed));
    }

    private static SalesRemainderOriginalReceiptV1? RestoreRemainder(RemainderReceiptFixtureV1 f) => SalesStore.RestoreOriginalRemainderReceipt(f.Attempt,
        AuditEvent.Create(new(f.Audit, AuditEventIntegrity.Compute(f.Audit))), f.Decision, f.Order, 6, 1, Actor, f.Request, f.Answer);
    private static async Task<RemainderReceiptFixtureV1> RemainderReceiptFixture(bool approve)
    {
        var (w, order) = await RemainderProtocolWorld(); w.Bindings = w.Bindings with { ApprovePermission = SalesPermissionManifestV1.OrdersApprove };
        var before = JsonSerializer.Serialize(RemainderLinesPicture(order), SalesDraftJson.Options); var request = new SalesDraftRemainderDecisionRequest("06", approve ? null : "  SENTETIK ret  ");
        var command = approve ? SalesDraftCommands.ApproveRemainderChangeV1 : SalesDraftCommands.RejectRemainderChangeV1;
        var metadata = Metadata("SENTETIK-remainder-proof", correlation: "SENTETIK-remainder-proof-original");
        var service = w.Service().WithRemainderDecisionSafety(); var answer = approve ? await service.ApproveRemainderChangeAsync(order.Id, 1, request, metadata) : await service.RejectRemainderChangeAsync(order.Id, 1, request, metadata);
        var facts = Assert.Single(order.RemainderChanges); var native = facts.Decision!; var r = facts.Request;
        // Golden of the EXISTING native RemainderAudit/Remainders shape; not a second business calculation.
        var after = JsonSerializer.Serialize(new { remainder = RemainderLinesPicture(order), change = new
        { r.ChangeNo, r.LineId, kind = r.Kind.ToString(), state = facts.State.ToString(), r.OrderedQuantityText, r.RequestedQuantityText, r.Reason, requestFloor = r.Floor,
            decision = new { native.DecidedBy, native.Reason, native.ResultQuantityText, floor = native.Floor } } }, SalesDraftJson.Options);
        var key = SalesStore.IdempotencyKeyHash(metadata.IdempotencyKey);
        var attempt = new DraftAttemptRow { CompanyId = Company, DraftId = order.Id, CommandName = command, ActorId = Actor, IdempotencyKeyHash = key,
            RequestHash = SalesDraftRequestHash.Compute(command, order.Id, 1, request), Outcome = 1, CorrelationId = metadata.CorrelationId, RecordedAtUtc = native.DecidedAtUtc };
        var decision = new DraftRemainderDecisionRow { CompanyId = Company, DraftId = order.Id, ChangeNo = 1, Outcome = (int)native.Outcome, DecidedBy = Actor,
            DecidedAtUtc = native.DecidedAtUtc, Reason = native.Reason, ResultQuantity = native.ResultQuantity, ResultQuantityText = native.ResultQuantityText,
            LoadedQuantity = native.Floor?.Loaded, ShippedQuantity = native.Floor?.Shipped, DeliveredQuantity = native.Floor?.Delivered, DraftVersion = 7, CorrelationId = metadata.CorrelationId };
        var audit = new AuditEventEnvelope(Guid.NewGuid(), Company, native.DecidedAtUtc, approve ? "sales.order-draft.remainder-change-approved.v1" : "sales.order-draft.remainder-change-rejected.v1",
            "Sales.OrderDraft", order.Id.ToString("D"), approve ? "ApproveRemainderChange" : "RejectRemainderChange", 6, 7, AuditActorTypes.User, Actor,
            SalesPermissionManifestV1.OrdersApprove, "[]", before, after, "SENTETIK karar", null, AuditAiInvolvement.None, null, "Web", null, null, metadata.CorrelationId, null, key, AuditOutcomes.Succeeded, 1);
        return new(order, request, answer, attempt, decision, audit);
    }
    private static object RemainderLinesPicture(SalesOrderDraft order) => new
    { version = order.Version, lines = order.Lines.Select(line => new { line.LineNo, line.Id, quantity = line.QuantityText, remainder = line.Remainder.ToString() }).ToArray() };
    private sealed record RemainderReceiptFixtureV1(SalesOrderDraft Order, SalesDraftRemainderDecisionRequest Request, SalesOrderDraftResponse Answer,
        DraftAttemptRow Attempt, DraftRemainderDecisionRow Decision, AuditEventEnvelope Audit);
}
