namespace GoymenERP.Api.Ai.Email;

// Server-captured identity only; never bind these fields from external email or model output.
public sealed record AiEmailScopeV1(Guid CompanyId, Guid ActorId, string SessionId);
public sealed record AiEmailRecipientV1(Guid PartyId, Guid AccountId, string AccountVersion, string Address);
public sealed record AiEmailContentV1(string Subject, string Body);
public sealed record AiEmailApprovalV1(Guid OperationId, AiEmailScopeV1 Scope,
    IReadOnlyList<AiEmailRecipientV1> Recipients, AiEmailContentV1 Content, string Digest);
public enum AiEmailStateV1 { Unavailable = 1, Refused = 2, Unknown = 3, Queued = 4, Delivered = 5 }
// Content-free receipt. Queued is not a delivery claim.
public sealed record AiEmailReceiptV1(Guid OperationId, Guid CompanyId, string Digest, AiEmailStateV1 State);

/// <summary>Unconfigured until a real send authority/account owner exists. Must revalidate current actor,
/// session, company and existing permissions; this port does not grant a new permission.</summary>
public interface IAiEmailAuthorityV1
{
    Task<bool> IsCurrentAsync(AiEmailScopeV1 scope, CancellationToken cancellationToken);
}

/// <summary>Future DURABLE owner, not an in-memory ledger or provider. Availability requires account,
/// explicit send authority and durable outbox readiness. Enqueue must atomically bind operation+company+digest,
/// verify independently stored human approval (this DTO is not proof), persist its binding and enqueue at most once.
/// Persist Unknown before any ambiguous effect; absence must never erase an attempted operation. A changed digest must refuse, including concurrent calls.
/// Recheck fresh authority and registered recipient versions inside the owner's atomic admission boundary.
/// Unknown is resolved only by FindAsync; it must never cause another external send.</summary>
public interface IAiApprovedEmailOwnerV1
{
    bool IsAvailable { get; }
    Task<AiEmailReceiptV1?> FindAsync(AiEmailScopeV1 scope, Guid operationId, CancellationToken cancellationToken);
    Task<AiEmailReceiptV1> EnqueueApprovedOnceAsync(AiEmailApprovalV1 approval, CancellationToken cancellationToken);
}
