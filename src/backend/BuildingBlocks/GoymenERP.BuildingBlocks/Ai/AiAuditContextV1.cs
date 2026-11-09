using System.Text.Json;

namespace GoymenERP.BuildingBlocks.Ai;

/// <summary>
/// Server-owned approval evidence for business audit. No prompt, command, difference, source title,
/// provider response or secret is serialized. This DTO does not create an approval or an audit event.
/// </summary>
public sealed record AiAuditContextV1
{
    public int SchemaVersion => 1;
    public Guid CompanyId { get; }
    public string ActorId { get; }
    public string SessionId { get; }
    public string ScopeRevision { get; }
    public string AuthorityRevision { get; }
    public string CorrelationId { get; }
    public string? CausationId { get; }
    public string ToolId { get; }
    public string ToolVersion { get; }
    public AiContractRevisionV1 Contract { get; }
    public Guid PreviewId { get; }
    public string PreviewHash { get; }
    public Guid ApprovalId { get; }
    public string ApprovedBy { get; }
    public DateTimeOffset ApprovedAtUtc { get; }
    public string? ModelDeployment { get; }
    public string? PromptPolicyVersion { get; }

    public AiAuditContextV1(AiServerContextV1 context, AiToolDescriptorV1 tool, AiPreviewV1 preview,
        Guid approvalId, string approvedBy, DateTimeOffset approvedAtUtc)
    {
        PreviewHash = AiCanonicalJson.PreviewHash(context, tool, preview);
        AiContractGuard.Id(approvalId); AiContractGuard.Utc(approvedAtUtc);
        if (approvedAtUtc < preview.CreatedAtUtc || approvedAtUtc >= preview.ExpiresAtUtc)
            throw new ArgumentException("Approval evidence falls outside the preview interval.");
        CompanyId = context.CompanyId; ActorId = context.ActorId; SessionId = context.SessionId;
        ScopeRevision = context.ScopeRevision; AuthorityRevision = context.AuthorityRevision;
        CorrelationId = context.CorrelationId; CausationId = context.CausationId;
        ToolId = tool.ToolId; ToolVersion = tool.Version; Contract = tool.Contract;
        PreviewId = preview.Id; ApprovalId = approvalId; ApprovedBy = AiContractGuard.Text(approvedBy);
        ApprovedAtUtc = approvedAtUtc; ModelDeployment = context.ModelDeployment; PromptPolicyVersion = context.PromptPolicyVersion;
    }

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
