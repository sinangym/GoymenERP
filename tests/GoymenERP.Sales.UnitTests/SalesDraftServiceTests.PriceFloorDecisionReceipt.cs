using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task Floor_original_receipt_binds_real_raw_payload_hash_and_native_variants_without_public_wire_change()
    {
        var f = await FloorReceiptFixture(); var receipt = Assert.IsType<SalesPriceFloorOriginalReceiptV1>(FloorRestore(f));
        Assert.Equal(f.Hash, receipt.PayloadHash); Assert.Equal(f.Attempt.RecordedAtUtc, receipt.RecordedAtUtc);
        Assert.Equal(f.Expected, receipt.ExpectedVersion); Assert.Equal(f.Expected + 1, receipt.ResultVersion);
        Assert.Equal("SENTETIK gerekçe", Assert.Single(receipt.Lines).After.FloorException!.Reason);
        Assert.Equal(f.Response.Detail.Lines[0].LineId, receipt.Lines[0].LineId);
        // Audit has trimmed reasons; the literal leading-zero version and padded reason remain significant in the native hash.
        var normalized = f.Request with { ExpectedVersion = f.Expected.ToString(CultureInfo.InvariantCulture),
            Lines = f.Request.Lines.Select(x => x with { Reason = x.Reason.Trim() }).ToArray() };
        Assert.NotEqual(f.Hash, SalesDraftRequestHash.Compute(SalesDraftCommands.ApprovePriceFloorExceptionsV1, receipt.DraftId, normalized));
        var hidden = f.Response with { Detail = f.Stored.Withheld };
        var withheld = Assert.IsType<SalesPriceFloorOriginalReceiptV1>(FloorRestore(f, hidden));
        Assert.Equal(receipt.OriginalDecisionDigest, withheld.OriginalDecisionDigest);
        Assert.NotEqual(receipt.CurrentResponseDigest, withheld.CurrentResponseDigest);
        Assert.Equal(SalesPriceFloorDecisionDigestsV1.CurrentResponseDigest(hidden), withheld.CurrentResponseDigest);
        var withProof = hidden with { OriginalFloorApprovalReceipt = withheld };
        Assert.Equal(JsonSerializer.Serialize(hidden, SalesDraftJson.Options), JsonSerializer.Serialize(withProof, SalesDraftJson.Options));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(withProof, SalesDraftJson.Options));
        Assert.Equal(3, json.RootElement.EnumerateObject().Count()); Assert.False(json.RootElement.TryGetProperty("originalFloorApprovalReceipt", out _));
    }

    [Theory]
    [InlineData("missing-audit")] [InlineData("duplicate-audit")] [InlineData("company")]
    [InlineData("draft")] [InlineData("key")] [InlineData("actor")] [InlineData("payload")]
    [InlineData("command")] [InlineData("correlation")] [InlineData("time")] [InlineData("version")]
    [InlineData("permission")] [InlineData("missing-result")] [InlineData("unknown-result")]
    [InlineData("private-result")] [InlineData("unknown-audit")]
    [InlineData("changed-price")] [InlineData("changed-requester")] [InlineData("changed-reason")]
    public async Task Floor_original_receipt_rejects_unproven_or_changed_native_attempt_Audit_and_result(string fault)
    {
        var f = await FloorReceiptFixture(); var audit = f.Audit;
        if (fault == "company") f.Attempt.CompanyId = Guid.NewGuid();
        if (fault == "draft") f.Attempt.DraftId = Guid.NewGuid();
        if (fault == "key") f.Attempt.IdempotencyKeyHash = new('A', 64);
        if (fault == "actor") f.Attempt.ActorId = "other";
        if (fault == "payload") f.Attempt.RequestHash = new('a', 64);
        if (fault == "command") f.Attempt.CommandName = SalesDraftCommands.ApproveDiscountExceptionsV1;
        if (fault == "correlation") f.Attempt.CorrelationId = "other";
        if (fault == "time") f.Attempt.RecordedAtUtc = f.Attempt.RecordedAtUtc.AddTicks(1);
        if (fault == "version") audit = audit with { EntityVersionAfter = f.Expected + 2 };
        if (fault == "permission") audit = audit with { PermissionCode = SalesPermissionManifestV1.OrdersPriceRead };
        if (fault == "missing-result") f.Attempt.ResultJson = null;
        if (fault is "unknown-result" or "private-result")
        {
            var result = JsonNode.Parse(f.Attempt.ResultJson!)!;
            if (fault == "unknown-result") result["unexpected"] = true;
            else result["visible"]!["originalFloorApprovalReceipt"] = new JsonObject();
            f.Attempt.ResultJson = result.ToJsonString();
        }
        if (fault is "unknown-audit" or "changed-price" or "changed-requester" or "changed-reason")
        {
            var after = JsonNode.Parse(audit.AfterJson)!;
            if (fault == "unknown-audit") after["unexpected"] = true;
            if (fault == "changed-price") after["lines"]![0]!["unitPrice"] = "999";
            if (fault == "changed-requester") after["lines"]![0]!["floorException"]!["requestedBy"] = "other";
            if (fault == "changed-reason") after["lines"]![0]!["floorException"]!["reason"] = "";
            audit = audit with { AfterJson = after.ToJsonString() };
        }
        var stored = AuditEvent.Create(new(audit, AuditEventIntegrity.Compute(audit)));
        IReadOnlyList<AuditEvent> events = fault == "missing-audit" ? [] : fault == "duplicate-audit" ? [stored, stored] : [stored];
        Assert.Null(SalesStore.RestoreOriginalPriceFloorReceipt(f.Attempt, events, f.Envelope, f.Hash, f.Response));
    }

    [Fact]
    public async Task Floor_original_receipt_survives_a_later_real_commercial_revision_without_using_today_prices_or_requester()
    {
        var f = await FloorReceiptFixture(); var first = Assert.IsType<SalesPriceFloorOriginalReceiptV1>(FloorRestore(f));
        f.World.User.Id = "SENTETIK-next-requester";
        await f.World.Service().ReviseCommercialAsync(f.Response.Detail.Draft.Id, Revision(f.Response.Detail.Draft.Version,
            new SalesDraftLineCommercialV1(f.Response.Detail.Lines[0].LineId, "10.9", KgUnit, null,
                new SalesDraftPriceReferenceRequestV1(PriceList, 1))), Metadata("SENTETIK-floor-later-price"));
        f.World.User.Id = f.Envelope.ActorId;
        var replay = await f.World.Service().ApprovePriceFloorExceptionsAsync(f.Response.Detail.Draft.Id, f.Request,
            Metadata("SENTETIK-floor-receipt", correlation: "later"));
        var receipt = Assert.IsType<SalesPriceFloorOriginalReceiptV1>(FloorRestore(f, replay));
        Assert.Equal(first.OriginalDecisionDigest, receipt.OriginalDecisionDigest);
        Assert.Equal("11", receipt.Lines[0].After.UnitPrice); Assert.Equal(Actor, receipt.Lines[0].Before.FloorException!.RequestedBy);
        Assert.Equal(f.Expected + 2, Assert.Single(f.World.Store.Drafts).Version);
        Assert.True(replay.IsReplay); Assert.Equal(f.Response.Detail.Draft.Version, replay.Detail.Draft.Version);
    }

    private static SalesPriceFloorOriginalReceiptV1? FloorRestore(FloorReceiptFixtureV1 f, SalesOrderDraftResponse? response = null) =>
        SalesStore.RestoreOriginalPriceFloorReceipt(f.Attempt, [AuditEvent.Create(new(f.Audit, AuditEventIntegrity.Compute(f.Audit)))],
            f.Envelope, f.Hash, response ?? f.Response);

    // Synthetic immutable artifact codec fixture. Real same-TX artifacts are covered by the separate SQL source.
    private static async Task<FloorReceiptFixtureV1> FloorReceiptFixture()
    {
        var (world, id) = await FloorPurposeWorld(); var draft = Assert.Single(world.Store.Drafts);
        var expected = draft.Version; var before = FloorAuditPicture(draft);
        var request = new SalesDraftPriceFloorExceptionApprovalRequest("0" + expected.ToString(CultureInfo.InvariantCulture),
            draft.Lines.Select(x => new SalesDraftPriceFloorExceptionApprovalLineV1(x.Id, "  SENTETIK gerekçe  ")).ToArray());
        var response = await world.Service().ApprovePriceFloorExceptionsAsync(id, request, Metadata("SENTETIK-floor-receipt", correlation: "SENTETIK-floor-original"));
        var after = FloorAuditPicture(draft); var stored = world.Store.FloorPurposeStoredResult("SENTETIK-floor-receipt");
        var hash = SalesDraftRequestHash.Compute(SalesDraftCommands.ApprovePriceFloorExceptionsV1, id, request);
        var key = SalesStore.IdempotencyKeyHash("SENTETIK-floor-receipt"); var now = draft.Lines[0].Commercial.FloorException!.ApprovedAtUtc!.Value;
        var envelope = new SalesDraftWriteEnvelope(world.User.Id, SalesPermissionManifestV1.OrdersPriceFloorExceptionApprove, [],
            "SENTETIK-floor-original", null, "SENTETIK-floor-receipt", "Web");
        var attempt = new DraftAttemptRow { CompanyId = Company, DraftId = id, CommandName = SalesDraftCommands.ApprovePriceFloorExceptionsV1,
            IdempotencyKeyHash = key, RequestHash = hash, ActorId = world.User.Id, Outcome = 1,
            CorrelationId = response.OriginalCorrelationId, RecordedAtUtc = now, ResultJson = JsonSerializer.Serialize(stored, SalesDraftJson.Options) };
        var audit = new AuditEventEnvelope(Guid.NewGuid(), Company, now, "sales.order-draft.price-floor-exceptions-approved.v1", "Sales.OrderDraft",
            id.ToString("D"), "ApprovePriceFloorExceptions", expected, expected + 1, AuditActorTypes.User, world.User.Id,
            SalesPermissionManifestV1.OrdersPriceFloorExceptionApprove, "[]", before, after, "SENTETIK karar", null,
            AuditAiInvolvement.None, null, "Web", null, null, response.OriginalCorrelationId, null, key, AuditOutcomes.Succeeded, 1);
        return new(world, request, response, stored, expected, attempt, envelope, hash, audit);
    }

    private static string FloorAuditPicture(SalesOrderDraft draft) => JsonSerializer.Serialize(new
    {
        revision = draft.Revision, state = draft.ApprovalState.ToString(), approval = draft.Approval,
        lines = draft.Lines.Select(line => new { line.LineNo, line.Commercial.UnitPrice, line.Commercial.PriceUnitId,
            line.Commercial.DiscountPercent, line.Commercial.Reference, line.Commercial.DiscountAmount,
            line.Commercial.SurchargePercent, line.Commercial.SurchargeAmount, line.Commercial.FloorException,
            line.Commercial.DiscountCheck, netUnitPrice = SalesLinePriceCalculator.NetUnitPriceText(line.Commercial), tax = line.Tax }).ToArray(),
    }, SalesDraftJson.Options);
    private sealed record FloorReceiptFixtureV1(World World, SalesDraftPriceFloorExceptionApprovalRequest Request,
        SalesOrderDraftResponse Response, SalesDraftReplayResult Stored, long Expected, DraftAttemptRow Attempt,
        SalesDraftWriteEnvelope Envelope, string Hash, AuditEventEnvelope Audit);
}
