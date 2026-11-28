namespace GoymenERP.BuildingBlocks.Work;

/// <summary>Private selected-request identity and current impact binding, never an original applied-permission snapshot.</summary>
public sealed record WorkIdentityAccessGrantProofV1(Guid CompanyId, Guid RequestId, Guid RequestedBy, Guid TargetUserId,
    Guid RoleId, bool RequiresSecondApproval, DateTimeOffset RequestedAtUtc, DateTimeOffset? ValidUntilUtc,
    string RoleName, string TargetName, string Status, string Version,
    string SourceFactsDigest, string CurrentImpactDigest, string PreviewDigest);
