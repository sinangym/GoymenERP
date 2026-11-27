using GoymenERP.BuildingBlocks.Ai;

namespace GoymenERP.Api.Ai.Pilot;

// Human-approved metadata only. No candidate is preapproved by this package.
public sealed record AiExternalReadPilotToolV1(string ToolId, string Version, string DescriptionSha256,
    string FieldPolicyRevision, bool NonSensitiveDataApproved, IReadOnlyList<AiFieldTagV1> OutputFields);
public sealed record AiExternalReadPilotPolicyV1(Guid CompanyId, string ActorId, string PolicyId, int Version,
    bool Approved, IReadOnlyList<AiExternalReadPilotToolV1> Tools);

/// <summary>Future reader of the explicit human-approved pilot/user/data list. Null means closed.
/// No implementation, default approval, Settings writer, account, protocol or activation is supplied here.
/// Approval never upgrades Internal to Public or overrides local-only/knowledge/field classification rules.
/// Approval must also attest the existing provider/KVKK/security/person/budget gates; this DTO cannot grant them.</summary>
public interface IAiExternalReadPilotPolicyV1
{
    Task<AiExternalReadPilotPolicyV1?> ReadApprovedAsync(Guid companyId, string actorId, CancellationToken cancellationToken);
}
