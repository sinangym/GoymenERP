using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.ApprovalRequests;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Over_request_receipt_genuinely_binds_raw_native_body_request_and_independent_order_versions_without_wire_fields(bool approve)
    {
        var f = await OverReceiptFixture(approve); var receipt = Assert.IsType<SalesOverDeliveryRequestOriginalReceiptV1>(RestoreOverReceipt(f));
        Assert.Equal((1L, 2L, f.Source.Request.Id, f.Source.Order.Id), (receipt.ExpectedRequestVersion, receipt.ResultRequestVersion, receipt.Target.RequestId, receipt.Target.OrderId));
        Assert.Equal(approve ? (long?)f.OrderBefore : null, receipt.OriginalOrderVersionBefore);
        Assert.Equal(approve ? (long?)(f.OrderBefore + 1) : null, receipt.OriginalOrderVersionAfter);
        Assert.Equal(f.Attempt.RequestHash, receipt.PayloadHash); Assert.Equal("01", f.Body.ExpectedVersion);
        Assert.Equal(f.Response.Request, receipt.OriginalRequest); Assert.Equal(approve ? null : "  SENTETIK ret  ", receipt.OriginalRequest.DecisionReason);
        var response = f.Response with { IsReplay = true, OriginalOverDeliveryReceipt = receipt };
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, SalesDraftJson.Options));
        Assert.Equal(3, json.RootElement.EnumerateObject().Count()); Assert.False(json.RootElement.TryGetProperty("originalOverDeliveryReceipt", out _));
        Assert.False(json.RootElement.TryGetProperty("originalDecisionReceipt", out _));
        var replay = Assert.IsType<SalesOverDeliveryRequestOriginalReceiptV1>(RestoreOverReceipt(f, observed: f.Response with { IsReplay = true }));
        Assert.Equal(receipt.OriginalDecisionDigest, replay.OriginalDecisionDigest);
    }

    [Theory]
    [InlineData("missing")] [InlineData("duplicate")] [InlineData("actor")] [InlineData("company")] [InlineData("hash")] [InlineData("key")]
    [InlineData("correlation")] [InlineData("timestamp")] [InlineData("target-id")] [InlineData("target-version")] [InlineData("raw-reason")]
    [InlineData("extra-root")] [InlineData("private-field")] [InlineData("missing-field")] [InlineData("quantity")]
    public async Task Over_request_receipt_rejects_legacy_unknown_closed_world_fields_or_wrong_original_provenance(string fault)
    {
        var f = await OverReceiptFixture(false); var audit = f.Audit; var decision = f.Command; var body = f.Body;
        if (fault == "actor") f.Attempt.ActorId = "SENTETIK-other";
        if (fault == "company") f.Attempt.CompanyId = Guid.NewGuid();
        if (fault == "hash") f.Attempt.RequestHash = new('a', 64);
        if (fault == "key") f.Attempt.IdempotencyKeyHash = new('A', 64);
        if (fault == "correlation") f.Attempt.CorrelationId = "SENTETIK-other";
        if (fault == "timestamp") f.Attempt.RecordedAtUtc = f.Attempt.RecordedAtUtc.AddTicks(1);
        if (fault == "target-id") decision = decision with { Target = decision.Target with { RequestId = Guid.NewGuid() } };
        if (fault == "target-version") decision = decision with { Target = decision.Target with { RequestVersion = "2" } };
        if (fault == "raw-reason") body = body with { Reason = "SENTETIK ret" };
        if (fault == "extra-root") audit = audit with { AfterJson = audit.AfterJson[..^1] + ",\"unexpected\":true}" };
        if (fault == "private-field") audit = audit with { AfterJson = audit.AfterJson[..^1] + ",\"originalOverDeliveryReceipt\":{}}" };
        if (fault == "missing-field") audit = audit with { BeforeJson = "{}" };
        if (fault == "quantity") audit = audit with { AfterJson = audit.AfterJson.Replace("1.28", "1.29", StringComparison.Ordinal) };
        var stored = OverEvent(audit); IReadOnlyList<AuditEvent> events = fault == "missing" ? [] : fault == "duplicate" ? [stored, stored] : [stored];
        Assert.Null(SalesStore.RestoreOverDeliveryRequestReceipt(f.Attempt, events, [], f.Source.Order.DocumentNo, Actor, body, decision, f.Response));
    }

    [Theory]
    [InlineData("missing-order")] [InlineData("wrong-order-version")] [InlineData("seen-total")] [InlineData("shipper")] [InlineData("extra-fact")]
    public async Task Over_request_approval_receipt_requires_the_same_native_order_audit_and_recorded_seen_total(string fault)
    {
        var f = await OverReceiptFixture(true); var order = f.OrderAudit!;
        if (fault == "wrong-order-version") order = order with { EntityVersionAfter = order.EntityVersionAfter + 1 };
        if (fault == "seen-total") order = order with { AfterJson = order.AfterJson.Replace("1.28", "1.29", StringComparison.Ordinal) };
        if (fault == "shipper") order = order with { AfterJson = order.AfterJson.Replace(Shipper, "SENTETIK-other-shipper", StringComparison.Ordinal) };
        if (fault == "extra-fact") order = order with { AfterJson = order.AfterJson[..^1] + ",\"extra\":true}" };
        Assert.Null(SalesStore.RestoreOverDeliveryRequestReceipt(f.Attempt, [OverEvent(f.Audit)], fault == "missing-order" ? [] : [OverEvent(order)], f.Source.Order.DocumentNo, Actor, f.Body, f.Command, f.Response));
    }

    [Fact]
    public async Task Over_request_historical_source_uses_original_terminal_request_and_never_the_current_order_version_or_current_maker()
    {
        var f = await OverReceiptFixture(true); var receipt = Assert.IsType<SalesOverDeliveryRequestOriginalReceiptV1>(RestoreOverReceipt(f));
        var world = f.World; world.Store.OverOriginal = new(SalesOverDeliveryRequestDecisionReadStateV1.Found, receipt);
        var company = new GoymenERP.BuildingBlocks.Multitenancy.CompanyContext(); company.Set(Company);
        var source = new SalesOverDeliveryRequestWorkInboxSourceV1(new SalesWorkInboxApprovalFakeStore(), company, world.User, world.Authority, world.Service());
        var context = new WorkInboxSourceContextV1(Company, Actor, "SENTETIK", null);
        var snapshot = await source.ReadDecisionVersionAsync(context, f.Source.Request.Id, "1", default); Assert.NotNull(snapshot);
        Assert.Equal("1", snapshot.SourceVersion); Assert.False(snapshot.Pending); Assert.Equal(2L, snapshot.SalesOverDeliveryRequestProof!.RequestVersion);
        Assert.Equal(Shipper, snapshot.Item.PreparedBy); Assert.Equal(receipt.SourceFactsDigest, snapshot.SalesOverDeliveryRequestProof.SourceFactsDigest);
        Assert.Equal("Approved", snapshot.SalesOverDeliveryRequestProof.RequestStatus);
        Assert.True(SalesOverDeliveryRequestDecisionBindingV1.MatchesDecisionProof(snapshot.Item, snapshot.SalesOverDeliveryRequestProof));
        world.Store.OverOriginal = new(SalesOverDeliveryRequestDecisionReadStateV1.NotFound, null) { CurrentVersion = 2 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionVersionAsync(context, f.Source.Request.Id, "1", default));
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Over_request_historical_completed_consumer_cancel_or_revoke_never_releases_proof(int boundary)
    {
        foreach (var cancel in new[] { true, false })
        {
            var f = await OverReceiptFixture(false); f.World.Store.OverOriginal = new(SalesOverDeliveryRequestDecisionReadStateV1.Found, RestoreOverReceipt(f));
            using var cancellation = new CancellationTokenSource(); var calls = 0;
            Task Fence(CancellationToken _) { if (++calls == boundary) { if (cancel) cancellation.Cancel(); else f.World.Authority.Denied.Add(OverDeliveryPermission); } return Task.CompletedTask; }
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.World.Service().ReadOverDeliveryRequestOriginalVersionAsync(f.Source.Request.Id, "1", Fence, cancellation.Token));
            else { var result = await f.World.Service().ReadOverDeliveryRequestOriginalVersionAsync(f.Source.Request.Id, "1", Fence); Assert.Equal(SalesOverDeliveryRequestDecisionReadStateV1.Denied, result.State); Assert.Null(result.Receipt); }
            Assert.Equal(boundary, calls);
        }
    }

    private static SalesOverDeliveryRequestOriginalReceiptV1? RestoreOverReceipt(OverOriginalFixture f, SalesApprovalRequestCommandResult? observed = null) =>
        SalesStore.RestoreOverDeliveryRequestReceipt(f.Attempt, [OverEvent(f.Audit)], f.OrderAudit is null ? [] : [OverEvent(f.OrderAudit)], f.Source.Order.DocumentNo, Actor, f.Body, f.Command, observed ?? f.Response);
    private static AuditEvent OverEvent(AuditEventEnvelope envelope) => AuditEvent.Create(new(envelope, AuditEventIntegrity.Compute(envelope)));
    private static async Task<OverOriginalFixture> OverReceiptFixture(bool approve)
    {
        var (world, source) = await OverHumanSource("Pending"); var request = source.Request; var order = source.Order;
        var before = OverAuditJson(request); var orderBefore = order.Version; var line = order.Lines[0]; var at = world.Clock.GetUtcNow();
        var body = new SalesDraftStateCommandRequest("01", approve ? null : "  SENTETIK ret  ");
        if (approve) { request.Approve(1, Actor, at); order.ApproveOverDelivery(order.Version, Actor, new(line.Id, request.SubjectRevision, request.Ordered!, request.RequestedTotalValue!.Value, [Shipper]), at); }
        else request.Reject(1, Actor, body.Reason, at);
        var command = approve ? SalesApprovalRequestCommands.ApproveOverDeliveryV1 : SalesApprovalRequestCommands.RejectOverDeliveryV1;
        var response = new SalesApprovalRequestCommandResult(SalesDraftService.ProjectOverDeliveryRequest(request), false, "SENTETIK-over-original");
        var attempt = new ApprovalRequestAttemptRow { CompanyId = Company, CommandName = command, IdempotencyKeyHash = SalesStore.IdempotencyKeyHash("SENTETIK-over-key"),
            RequestHash = SalesApprovalRequestHash.Compute(command, order.Id, 1, body), ActorId = Actor, Outcome = 1, SubjectId = order.Id, RequestId = request.Id,
            CorrelationId = response.CorrelationId, RecordedAtUtc = at };
        AuditEventEnvelope Event(string type, string entityType, string entityId, string operation, long oldVersion, long newVersion, string oldJson, string newJson) =>
            new(Guid.NewGuid(), Company, at, type, entityType, entityId, operation, oldVersion, newVersion, AuditActorTypes.User, Actor, OverDeliveryPermission,
                "[]", oldJson, newJson, "SENTETIK karar", null, AuditAiInvolvement.None, null, "test", null, null, response.CorrelationId, null, attempt.IdempotencyKeyHash, AuditOutcomes.Succeeded, 1);
        var audit = Event(approve ? "sales.order.over-delivery-request-approved.v1" : "sales.order.over-delivery-request-rejected.v1", "Sales.ApprovalRequest", request.Id.ToString("D"),
            approve ? "ApproveOverDeliveryRequest" : "RejectOverDeliveryRequest", 1, 2, before, OverAuditJson(request));
        AuditEventEnvelope? orderAudit = null;
        if (approve)
        {
            var facts = order.OverDeliveryApprovals[^1];
            orderAudit = Event("sales.order-draft.over-delivery-approved.v1", "Sales.OrderDraft", order.Id.ToString("D"), "ApproveOverDeliveryRequest", orderBefore, order.Version,
                JsonSerializer.Serialize(new { version = orderBefore }, SalesDraftJson.Options), JsonSerializer.Serialize(new { version = order.Version,
                    overDeliveryApproval = new { facts.LineId, line.LineNo, facts.ApprovalNo, facts.BasisRevision, facts.Ordered, facts.ApprovedShipped, facts.ApprovedBy, facts.ApprovedAtUtc, requestNo = 1, shippedBy = new[] { Shipper } } }, SalesDraftJson.Options));
        }
        return new(world, source, new(new(request.Id, order.Id, 1, request.SubjectRevision, line.Id, "1"), body.ExpectedVersion, body.Reason), body, response, attempt, audit, orderAudit, orderBefore);
    }
    private static string OverAuditJson(SalesApprovalRequest r) => JsonSerializer.Serialize(new { r.Id, kind = r.Kind.ToString(), r.SubjectId, r.RequestNo, r.SubjectRevision, r.LineId,
        r.Ordered, r.Quantity, r.RequestedTotal, r.SourceInstructionId, r.SourceShipmentId, r.Note, state = r.State.ToString(), version = r.Version,
        r.RequestedBy, r.RequestedAtUtc, decidedBy = r.Decision?.DecidedBy, decidedAtUtc = r.Decision?.DecidedAtUtc, decisionReason = r.Decision?.Reason }, SalesDraftJson.Options);
    private sealed record OverOriginalFixture(World World, SalesOverDeliveryRequestDecisionSourceV1 Source, SalesOverDeliveryRequestDecisionCommandV1 Command,
        SalesDraftStateCommandRequest Body, SalesApprovalRequestCommandResult Response, ApprovalRequestAttemptRow Attempt, AuditEventEnvelope Audit, AuditEventEnvelope? OrderAudit, long OrderBefore);
}
