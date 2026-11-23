namespace GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;

/// <summary>
/// One human approval of one packaged knowledge version, valid for the whole group (AI-D46); never deleted. Like the
/// group-shared Product master it has no owning company: <see cref="OriginCompanyId"/> is only the verified company context in
/// which the approver held <c>ai.knowledge.approve</c> (causation, audit), and <see cref="WithdrawnOriginCompanyId"/> the same
/// for the withdrawal.
/// </summary>
internal sealed class AiKnowledgeApprovalRow
{
    public Guid OriginCompanyId { get; set; }
    public Guid Id { get; set; }
    public int SourceKind { get; set; }
    public string PackageSha256 { get; set; } = "";
    public int Status { get; set; }
    public long Version { get; set; }
    public Guid ApprovedByUserId { get; set; }
    public DateTimeOffset ApprovedAtUtc { get; set; }
    public Guid? WithdrawnByUserId { get; set; }
    public Guid? WithdrawnOriginCompanyId { get; set; }
    public DateTimeOffset? WithdrawnAtUtc { get; set; }
    public string? WithdrawReason { get; set; }
    public string CorrelationId { get; set; } = "";
    public string? CausationId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Idempotency attempt per (request company, actor, key hash); a recorded refusal replays with the same key.</summary>
internal sealed class AiKnowledgeCommandAttemptRow
{
    public Guid CompanyId { get; set; }
    public Guid ActorUserId { get; set; }
    public string IdempotencyKeySha256 { get; set; } = "";
    public string Command { get; set; } = "";
    public string RequestSha256 { get; set; } = "";
    public Guid TargetId { get; set; }
    public int Status { get; set; }
    public string? RefusalCode { get; set; }
    public string CorrelationId { get; set; } = "";
    public DateTimeOffset RecordedAtUtc { get; set; }
}

internal sealed class AiKnowledgeCompanyReference
{
    public Guid Id { get; set; }
}
