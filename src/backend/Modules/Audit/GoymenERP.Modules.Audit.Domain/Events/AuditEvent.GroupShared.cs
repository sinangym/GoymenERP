namespace GoymenERP.Modules.Audit.Domain.Events;

public sealed partial class AuditEvent
{
    public const string CompanySubject = "Company";
    public const string GroupSharedProductSubject = "GroupSharedProduct";
    public const string GroupSharedProductEntityType = "Product.Product";

    public string SubjectKind { get; private set; } = CompanySubject;
    public Guid? OriginCompanyId { get; private set; }
    public string? AuthorityRevision { get; private set; }

    private AuditEvent(GroupSharedAuditEventData data)
    {
        data.ValidateIntegrity();
        var envelope = data.Envelope;
        Id = envelope.Id;
        SubjectKind = GroupSharedProductSubject;
        CompanyId = null;
        OriginCompanyId = envelope.Subject.OriginCompanyId;
        AuthorityRevision = envelope.Subject.AuthorityRevision;
        OccurredAtUtc = envelope.OccurredAtUtc;
        EventType = envelope.EventType;
        EntityType = envelope.EntityType;
        EntityId = envelope.EntityId;
        Operation = envelope.Operation;
        EntityVersionBefore = envelope.EntityVersionBefore;
        EntityVersionAfter = envelope.EntityVersionAfter;
        ActorType = envelope.ActorType;
        ActorId = envelope.ActorId;
        PermissionCode = envelope.PermissionCode;
        RoleCodesJson = envelope.RoleCodesJson;
        BeforeJson = envelope.BeforeJson;
        AfterJson = envelope.AfterJson;
        Reason = envelope.Reason;
        AiInvolvement = envelope.AiInvolvement;
        AiContextJson = envelope.AiContextJson;
        SourceChannel = envelope.SourceChannel;
        ClientIpAddress = envelope.ClientIpAddress;
        UserAgent = envelope.UserAgent;
        CorrelationId = envelope.CorrelationId;
        CausationId = envelope.CausationId;
        IdempotencyKeyHash = envelope.IdempotencyKeyHash;
        Outcome = envelope.Outcome;
        SchemaVersion = envelope.SchemaVersion;
        IntegrityHash = data.IntegrityHash;
    }

    public static AuditEvent CreateGroupShared(GroupSharedAuditEventData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new(data);
    }

    public Guid RequireCompanyId() => SubjectKind == CompanySubject
        && CompanyId is { } company && company != Guid.Empty && OriginCompanyId is null && AuthorityRevision is null
        ? company : throw new InvalidOperationException("A global audit subject is not a company-owned audit event.");

    public GroupSharedAuditEventData ToGroupSharedIntegrityData()
    {
        if (SubjectKind != GroupSharedProductSubject || SchemaVersion != 2 || CompanyId is not null
            || OriginCompanyId is not { } origin || AuthorityRevision is null || ApprovalContextJson is not null
            || EntityVersionAfter is not { } after || !Guid.TryParseExact(EntityId, "D", out var productId))
            throw new InvalidOperationException("The stored global audit subject/schema is invalid.");
        var data = new GroupSharedAuditEventData(new(Id, new(productId, origin, AuthorityRevision), OccurredAtUtc,
            EventType, EntityType, EntityId, Operation, EntityVersionBefore, after, ActorType, ActorId,
            PermissionCode, RoleCodesJson, BeforeJson, AfterJson, Reason, AiInvolvement, AiContextJson,
            SourceChannel, ClientIpAddress, UserAgent, CorrelationId, CausationId, IdempotencyKeyHash, Outcome, SchemaVersion), IntegrityHash);
        data.ValidateIntegrity();
        return data;
    }
}
