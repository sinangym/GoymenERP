using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Services;

namespace GoymenERP.Audit.UnitTests;

public sealed class GroupSharedAuditEnvelopeTests
{
    private static readonly Guid Product = Guid.Parse("55000000-0000-0000-0000-000000000001");
    private static readonly Guid Actor = Guid.Parse("53000000-0000-0000-0000-000000000001");
    private static readonly Guid Origin = Guid.Parse("54000000-0000-0000-0000-000000000001");

    [Fact]
    public void ExistingCompanyV1EnvelopeKeepsItsExactGoldenBytesAndHash()
    {
        var envelope = new AuditEventEnvelope(Product, Origin, new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero),
            "event", "entity", "id", "operation", null, 1, "User", Actor.ToString("D"), "permission", "[]",
            "{}", "{}", "reason", null, "None", null, "Web", null, null, "correlation", null, new string('A', 64), "Succeeded", 1);
        const string golden = """{"Id":"55000000-0000-0000-0000-000000000001","CompanyId":"54000000-0000-0000-0000-000000000001","OccurredAtUtc":"2026-09-12T00:00:00+00:00","EventType":"event","EntityType":"entity","EntityId":"id","Operation":"operation","EntityVersionBefore":null,"EntityVersionAfter":1,"ActorType":"User","ActorId":"53000000-0000-0000-0000-000000000001","PermissionCode":"permission","RoleCodesJson":"[]","BeforeJson":"{}","AfterJson":"{}","Reason":"reason","ApprovalContextJson":null,"AiInvolvement":"None","AiContextJson":null,"SourceChannel":"Web","ClientIpAddress":null,"UserAgent":null,"CorrelationId":"correlation","CausationId":null,"IdempotencyKeyHash":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","Outcome":"Succeeded","SchemaVersion":1}""";
        Assert.Equal(golden, JsonSerializer.Serialize(envelope));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(golden)));
        Assert.Equal(hash, AuditEventIntegrity.Compute(envelope));
        Assert.Equal(golden, JsonSerializer.Serialize(AuditEvent.Create(new(envelope, hash)).ToIntegrityEnvelope()));
    }

    [Fact]
    public void GlobalEnvelopeBindsSubjectOriginAuthorityAndOriginalMetadataWithoutOwnerCompany()
    {
        var data = GroupSharedAuditEventFactory.Create(Draft());
        data.ValidateIntegrity();
        Assert.Equal("GroupSharedProduct", data.Envelope.Subject.Kind);
        Assert.Equal(Product, data.Envelope.Subject.ProductId);
        Assert.Equal(Origin, data.Envelope.Subject.OriginCompanyId);
        Assert.Equal("revision-1", data.Envelope.Subject.AuthorityRevision);
        Assert.Equal("original-correlation", data.Envelope.CorrelationId);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("request-key"))), data.Envelope.IdempotencyKeyHash);
        Assert.DoesNotContain("\"CompanyId\"", JsonSerializer.Serialize(data.Envelope));
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("revision")]
    [InlineData("product")]
    [InlineData("correlation")]
    [InlineData("before")]
    [InlineData("after")]
    public void AlteredGlobalEvidenceCannotReuseItsOriginalHash(string change)
    {
        var data = GroupSharedAuditEventFactory.Create(Draft());
        var envelope = change switch
        {
            "origin" => data.Envelope with { Subject = new(Product, Guid.NewGuid(), "revision-1") },
            "revision" => data.Envelope with { Subject = new(Product, Origin, "revision-2") },
            "product" => data.Envelope with { Subject = new(Actor, Origin, "revision-1"), EntityId = Actor.ToString("D") },
            "correlation" => data.Envelope with { CorrelationId = "other-correlation" },
            "before" => data.Envelope with { BeforeJson = "{\"different\":true}" },
            "after" => data.Envelope with { AfterJson = "{\"different\":true}" },
            _ => throw new ArgumentException(nameof(change)),
        };
        Assert.Throws<ArgumentException>(() => (data with { Envelope = envelope }).ValidateIntegrity());
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("permission")]
    [InlineData("event")]
    [InlineData("version")]
    [InlineData("role")]
    public void UnsupportedEnvelopeCombinationsFailClosed(string change)
    {
        var data = GroupSharedAuditEventFactory.Create(Draft());
        var envelope = change switch
        {
            "schema" => data.Envelope with { SchemaVersion = 1 },
            "permission" => data.Envelope with { PermissionCode = "identity.audit.read" },
            "event" => data.Envelope with { EventType = "product.draft.basic-edited.v1" },
            "version" => data.Envelope with { EntityVersionAfter = 2 },
            "role" => data.Envelope with { RoleCodesJson = "[\"COMPANY_OPERATOR\"]" },
            _ => throw new ArgumentException(nameof(change)),
        };
        Assert.Throws<ArgumentException>(() => GroupSharedAuditEventData.Create(envelope));
    }

    // #329 (DEC-PRD-010): the card approval is evidence only as a one-step transition under its own
    // event type and permission; the draft event types and a skipped version stay refused.
    [Fact]
    public void ActivationIsAcceptedOnlyAsAOneStepApprovalTransition()
    {
        var data = GroupSharedAuditEventFactory.Create(Activation());
        Assert.Equal("Activate", data.Envelope.Operation);
        Assert.Equal("product.card.activate", data.Envelope.PermissionCode);
        data.ValidateIntegrity();
        foreach (var envelope in new[]
        {
            data.Envelope with { EventType = "product.draft.basic-edited.v1" },
            data.Envelope with { EventType = "product.draft.created.v1" },
            data.Envelope with { PermissionCode = "product.draft.edit" },
            data.Envelope with { EntityVersionAfter = 4 },
            data.Envelope with { EntityVersionBefore = null, EntityVersionAfter = 1 },
        })
            Assert.Throws<ArgumentException>(() => GroupSharedAuditEventData.Create(envelope));
    }

    // DEC-CAR-003 / PRD-PERM-001 (#99): deactivation and reactivation are evidence only as a one-step move
    // under their own event type and permission; neither borrows the other's, nor the approval's.
    [Theory]
    [InlineData(GroupSharedMasterOperation.Deactivate, "Deactivate", "product.card.deactivate", "product.card.deactivated.v1",
        "product.card.reactivated.v1", "product.card.reactivate")]
    [InlineData(GroupSharedMasterOperation.Reactivate, "Reactivate", "product.card.reactivate", "product.card.reactivated.v1",
        "product.card.deactivated.v1", "product.card.deactivate")]
    public void LifecycleMovesAreAcceptedOnlyAsOneStepTransitionsUnderTheirOwnNames(GroupSharedMasterOperation operation,
        string name, string permission, string eventType, string otherEvent, string otherPermission)
    {
        var data = GroupSharedAuditEventFactory.Create(new(
            GroupSharedMasterCommandContext.FromServerDecision(new(true, GroupSharedMasterKind.Product,
                operation, Actor, Origin, "revision-1"), operation),
            Product, eventType, 3, 4, "{\"lifecycle\":\"Active\"}", "{\"lifecycle\":\"Inactive\"}",
            "Üretimden kalktı", "Web", null, null, "original-correlation", null, "request-key", "None", null));
        Assert.Equal((name, permission, "Web"), (data.Envelope.Operation, data.Envelope.PermissionCode, data.Envelope.SourceChannel));
        data.ValidateIntegrity();
        foreach (var envelope in new[]
        {
            data.Envelope with { EventType = otherEvent },
            data.Envelope with { EventType = "product.card.activated.v1" },
            data.Envelope with { PermissionCode = otherPermission },
            data.Envelope with { PermissionCode = "product.card.activate" },
            data.Envelope with { EntityVersionAfter = 5 },
            data.Envelope with { EntityVersionBefore = null, EntityVersionAfter = 1 },
        })
            Assert.Throws<ArgumentException>(() => GroupSharedAuditEventData.Create(envelope));
    }

    [Fact]
    public void EmptySubjectAndNoncanonicalMetadataCannotBeNormalizedIntoValidEvidence()
    {
        Assert.Throws<ArgumentException>(() => new GroupSharedProductAuditSubject(Guid.Empty, Origin, "revision"));
        Assert.Throws<ArgumentException>(() => new GroupSharedProductAuditSubject(Product, Guid.Empty, "revision"));
        Assert.Throws<ArgumentException>(() => new GroupSharedProductAuditSubject(Product, Origin, " revision "));
        Assert.Throws<ArgumentException>(() => GroupSharedAuditEventFactory.Create(Draft() with { CorrelationId = " corr " }));
        Assert.Throws<ArgumentException>(() => GroupSharedAuditEventFactory.Create(Draft() with { IdempotencyKey = " key " }));
    }

    private static GroupSharedAuditEventDraft Draft() => new(
        GroupSharedMasterCommandContext.FromServerDecision(new(true, GroupSharedMasterKind.Product,
            GroupSharedMasterOperation.DraftCreate, Actor, Origin, "revision-1"), GroupSharedMasterOperation.DraftCreate),
        Product, "product.draft.created.v1", null, 1, "{}", "{\"name\":\"draft\"}", "Create draft", "Web",
        null, null, "original-correlation", null, "request-key", "None", null);

    private static GroupSharedAuditEventDraft Activation() => new(
        GroupSharedMasterCommandContext.FromServerDecision(new(true, GroupSharedMasterKind.Product,
            GroupSharedMasterOperation.Activate, Actor, Origin, "revision-1"), GroupSharedMasterOperation.Activate),
        Product, "product.card.activated.v1", 2, 3, "{\"lifecycle\":\"Draft\"}", "{\"lifecycle\":\"Active\"}",
        "Ürün kartı onayı", "Web", null, null, "original-correlation", null, "request-key", "None", null);
}
