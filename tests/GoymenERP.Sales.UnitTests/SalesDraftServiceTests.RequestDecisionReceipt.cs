using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task Original_request_decision_proof_does_not_invent_an_original_HTTP_detail_from_current_replay()
    {
        var f = await ReceiptFixture();
        var first = Assert.IsType<SalesRequestOriginalDecisionReceiptV1>(Restore(f));
        Assert.Equal(("Submitted", "Approved", "1", "2"), (f.Before.Request.State, first.State, first.ExpectedVersion, first.ResultVersion));
        var rejected = await f.World.Service().RejectRequestAsync(f.Response.Detail.Request.Id, Decision("2", "  SENTETIK ret  "), Metadata("receipt-reject"));
        var replay = await f.World.Service().ApproveRequestAsync(f.Response.Detail.Request.Id, Decision("1"), Metadata("receipt-approve", correlation: "later"));
        Assert.True(replay.IsReplay); Assert.Equal(rejected.Detail.Request, replay.Detail.Request);
        var oldDecision = Assert.IsType<SalesRequestOriginalDecisionReceiptV1>(Restore(f, replay));
        Assert.Equal(("Approved", "2", "receipt-original"), (oldDecision.State, oldDecision.ResultVersion, oldDecision.OriginalCorrelationId));
        Assert.Equal(first.OriginalDecisionDigest, oldDecision.OriginalDecisionDigest);
        Assert.NotEqual(first.CurrentResponseDigest, oldDecision.CurrentResponseDigest);
        Assert.Equal(SalesRequestDecisionDigestsV1.CurrentResponseDigest(replay), oldDecision.CurrentResponseDigest);
        var response = replay with { OriginalDecisionReceipt = oldDecision };
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, SalesDraftJson.Options));
        Assert.Equal(3, json.RootElement.EnumerateObject().Count());
        Assert.False(json.RootElement.TryGetProperty("originalDecisionReceipt", out _));
        Assert.Null(f.Attempt.ResultJson);
    }

    [Theory]
    [InlineData("missing")] [InlineData("duplicate")] [InlineData("company")] [InlineData("request")]
    [InlineData("key")] [InlineData("actor")] [InlineData("payload")] [InlineData("correlation")]
    [InlineData("permission")] [InlineData("event")] [InlineData("version")]
    [InlineData("malformed")] [InlineData("extra-key")] [InlineData("reason")]
    [InlineData("snapshot-forgery")] [InlineData("timestamp")]
    public async Task Original_request_decision_proof_requires_exactly_one_matching_immutable_attempt_and_Audit(string fault)
    {
        var f = await ReceiptFixture(); var audit = f.Audit;
        if (fault == "company") f.Attempt.CompanyId = Guid.NewGuid();
        if (fault == "request") f.Attempt.RequestId = Guid.NewGuid();
        if (fault == "key") f.Attempt.IdempotencyKeyHash = new('A', 64);
        if (fault == "actor") f.Attempt.ActorId = "other";
        if (fault == "payload") f.Attempt.RequestHash = new('b', 64);
        if (fault == "correlation") f.Attempt.CorrelationId = "other";
        if (fault == "snapshot-forgery") f.Attempt.ResultJson = "{}";
        if (fault == "timestamp") f.Attempt.RecordedAtUtc = f.Attempt.RecordedAtUtc.AddTicks(1);
        if (fault == "permission") audit = audit with { PermissionCode = SalesPermissionManifestV1.RequestsRead };
        if (fault == "event") audit = audit with { Operation = "Reject" };
        if (fault == "version") audit = audit with { EntityVersionAfter = 3 };
        if (fault == "malformed") audit = audit with { BeforeJson = "{}" };
        if (fault == "extra-key") audit = audit with { AfterJson = audit.AfterJson[..^1] + ",\"extra\":1}" };
        if (fault == "reason") audit = audit with { AfterJson = ReceiptAuditState(f.Response.Detail with { Decision = f.Response.Detail.Decision! with { RejectionReason = "unexpected" } }) };
        var storedAudit = AuditEvent.Create(new(audit, AuditEventIntegrity.Compute(audit)));
        IReadOnlyList<AuditEvent> candidates = fault == "missing" ? [] : fault == "duplicate" ? [storedAudit, storedAudit] : [storedAudit];
        Assert.Null(SalesStore.RestoreOriginalRequestDecisionReceipt(f.Attempt, candidates, f.Envelope, f.Hash,
            SalesRequestCommands.ApproveV1, f.Response));
    }

    private static SalesRequestOriginalDecisionReceiptV1? Restore(RequestReceiptFixture f, SalesRequestResponse? current = null) =>
        SalesStore.RestoreOriginalRequestDecisionReceipt(f.Attempt, [AuditEvent.Create(new(f.Audit, AuditEventIntegrity.Compute(f.Audit)))],
            f.Envelope, f.Hash, SalesRequestCommands.ApproveV1, current ?? f.Response);

    private static async Task<RequestReceiptFixture> ReceiptFixture()
    {
        var world = new World(); var service = world.Service();
        var before = (await service.CreateRequestAsync(RequestOf(lines: 2), Metadata("receipt-create"))).Detail;
        var response = await service.ApproveRequestAsync(before.Request.Id, Decision("1"), Metadata("receipt-approve", correlation: "receipt-original"));
        var hash = SalesRequestHash.Compute(SalesRequestCommands.ApproveV1, before.Request.Id, Decision("1"));
        var key = SalesStore.IdempotencyKeyHash("receipt-approve");
        var envelope = new SalesDraftWriteEnvelope(Actor, SalesPermissionManifestV1.RequestsApprove, [], "receipt-original", null, "receipt-approve", "Web");
        var attempt = new RequestAttemptRow { CompanyId = Company, RequestId = before.Request.Id,
            CommandName = SalesRequestCommands.ApproveV1, IdempotencyKeyHash = key, RequestHash = hash, ActorId = Actor,
            Outcome = 1, CorrelationId = "receipt-original", RecordedAtUtc = response.Detail.Decision!.DecidedAtUtc };
        var audit = new AuditEventEnvelope(Guid.NewGuid(), Company, attempt.RecordedAtUtc, "sales.request.approved.v1", "Sales.Request",
            before.Request.Id.ToString("D"), "Approve", 1, 2, AuditActorTypes.User, Actor, SalesPermissionManifestV1.RequestsApprove,
            "[]", ReceiptAuditState(before), ReceiptAuditState(response.Detail), "SENTETIK karar", null,
            AuditAiInvolvement.None, null, "Web", null, null, "receipt-original", null, key, AuditOutcomes.Succeeded, 1);
        return new(world, before, response, attempt, envelope, hash, audit);
    }
    private static string ReceiptAuditState(SalesRequestDetailView detail) => JsonSerializer.Serialize(new
    { detail.Request.Id, detail.Request.DocumentNo, detail.Request.State, version = long.Parse(detail.Request.Version, System.Globalization.CultureInfo.InvariantCulture),
        detail.Decision, detail.Request.PartyId, detail.Request.CompanyAccountId, detail.Request.BusinessDate, lines = (object?)null }, SalesDraftJson.Options);
    private sealed record RequestReceiptFixture(World World, SalesRequestDetailView Before, SalesRequestResponse Response,
        RequestAttemptRow Attempt, SalesDraftWriteEnvelope Envelope, string Hash, AuditEventEnvelope Audit);
}
