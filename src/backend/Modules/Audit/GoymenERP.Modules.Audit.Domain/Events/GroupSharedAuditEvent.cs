using System.Security.Cryptography;
using System.Text.Json;

namespace GoymenERP.Modules.Audit.Domain.Events;

// An explicit subject, not a nullable CompanyId or a company chosen as global owner.
public sealed record GroupSharedProductAuditSubject
{
    public GroupSharedProductAuditSubject(Guid productId, Guid originCompanyId, string authorityRevision)
    {
        if (productId == Guid.Empty || originCompanyId == Guid.Empty)
            throw new ArgumentException("Product and server-origin identities must be non-empty.");
        GroupSharedAuditEventEnvelope.RequireCanonical(authorityRevision, 128, nameof(authorityRevision));
        ProductId = productId;
        OriginCompanyId = originCompanyId;
        AuthorityRevision = authorityRevision;
    }

    public string Kind => "GroupSharedProduct";
    public Guid ProductId { get; }
    public Guid OriginCompanyId { get; }
    public string AuthorityRevision { get; }
}

// V2 is separate so the existing AuditEventEnvelope and V1 serialization/hash do not change.
// This is the canonical Audit owner's envelope, not a second persisted audit ledger.
public sealed record GroupSharedAuditEventEnvelope(
    Guid Id,
    GroupSharedProductAuditSubject Subject,
    DateTimeOffset OccurredAtUtc,
    string EventType,
    string EntityType,
    string EntityId,
    string Operation,
    long? EntityVersionBefore,
    long EntityVersionAfter,
    string ActorType,
    string ActorId,
    string PermissionCode,
    string RoleCodesJson,
    string BeforeJson,
    string AfterJson,
    string Reason,
    string AiInvolvement,
    string? AiContextJson,
    string SourceChannel,
    string? ClientIpAddress,
    string? UserAgent,
    string CorrelationId,
    string? CausationId,
    string IdempotencyKeyHash,
    string Outcome,
    int SchemaVersion)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Subject);
        if (Id == Guid.Empty || SchemaVersion != 2 || OccurredAtUtc.Offset != TimeSpan.Zero
            || EntityType != "Product.Product" || EntityId != Subject.ProductId.ToString("D")
            || ActorType != "User" || !Guid.TryParseExact(ActorId, "D", out var actorId) || actorId == Guid.Empty
            || Outcome != "Succeeded" || RoleCodesJson != "[\"SYSTEM_ADMINISTRATOR\"]")
            throw new ArgumentException("The global Product audit envelope is invalid.");
        var validOperation = Operation switch
        {
            "DraftCreate" => EventType == "product.draft.created.v1" && PermissionCode == "product.draft.create"
                && EntityVersionBefore is null && EntityVersionAfter == 1,
            "DraftBasicEdit" => EventType == "product.draft.basic-edited.v1" && PermissionCode == "product.draft.edit"
                && EntityVersionBefore is >= 1 and < long.MaxValue && EntityVersionAfter == EntityVersionBefore + 1,
            _ => false,
        };
        if (!validOperation) throw new ArgumentException("The Product audit operation/version transition is invalid.");
        RequireCanonical(Reason, 1000, nameof(Reason));
        RequireCanonical(CorrelationId, 128, nameof(CorrelationId));
        if (CausationId is not null) RequireCanonical(CausationId, 128, nameof(CausationId));
        if (ClientIpAddress is not null) RequireCanonical(ClientIpAddress, 64, nameof(ClientIpAddress));
        if (UserAgent is not null) RequireCanonical(UserAgent, 512, nameof(UserAgent));
        if (IdempotencyKeyHash is null || IdempotencyKeyHash.Length != 64
            || IdempotencyKeyHash.Any(c => c is not (>= '0' and <= '9' or >= 'A' and <= 'F')))
            throw new ArgumentException("The Product audit idempotency hash is not canonical.");
        if (SourceChannel is not ("Web" or "Mobile" or "Import" or "Integration" or "AiAssisted" or "System")
            || AiInvolvement is not ("None" or "Assisted" or "Proposed" or "Executed"))
            throw new ArgumentException("The Product audit source metadata is invalid.");
        using var before = JsonDocument.Parse(BeforeJson);
        using var after = JsonDocument.Parse(AfterJson);
        if (AiContextJson is not null) { using var ai = JsonDocument.Parse(AiContextJson); }
    }

    internal static void RequireCanonical(string value, int limit, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > limit || value != value.Trim() || value.Any(char.IsControl))
            throw new ArgumentException("A canonical non-empty audit value is required.", name);
    }
}

public sealed record GroupSharedAuditEventData(GroupSharedAuditEventEnvelope Envelope, string IntegrityHash)
{
    public static GroupSharedAuditEventData Create(GroupSharedAuditEventEnvelope envelope) => new(envelope, Compute(envelope));

    public void ValidateIntegrity()
    {
        if (!string.Equals(IntegrityHash, Compute(Envelope), StringComparison.Ordinal))
            throw new ArgumentException("The canonical V2 audit integrity hash does not match its envelope.");
    }

    private static string Compute(GroupSharedAuditEventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        envelope.Validate();
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(envelope)));
    }
}
