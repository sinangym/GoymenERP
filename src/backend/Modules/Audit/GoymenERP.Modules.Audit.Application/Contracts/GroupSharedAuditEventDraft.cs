using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Modules.Audit.Application.Contracts;

// Subject/origin extension proposal for the canonical Audit owner, not a second ledger.
// No persistence, collector registration or tenant-audit visibility is changed here.
public sealed record GroupSharedAuditEventDraft(
    GroupSharedMasterCommandContext Context,
    Guid ProductId,
    string EventType,
    long? EntityVersionBefore,
    long EntityVersionAfter,
    string BeforeJson,
    string AfterJson,
    string Reason,
    string SourceChannel,
    string? ClientIpAddress,
    string? UserAgent,
    string CorrelationId,
    string? CausationId,
    string IdempotencyKey,
    string AiInvolvement,
    string? AiContextJson)
{
    public string SubjectKind => "GroupSharedMaster";
    public string EntityType => "Product.Product";
    public Guid OriginCompanyId => Context.OriginCompanyId;
    public Guid LocalActorId => Context.LocalActorId;
}

public interface IGroupSharedAuditEventCollector
{
    // Enlist through the existing owner-supported transaction, never an out-of-band append.
    // Product + audit + durable result must commit or roll back together.
    void Enqueue(GroupSharedAuditEventDraft draft);
}
