using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Identity.Application.Contracts;

/// <summary>A request preview, never a grant. Direct permission assignment has no native command and is not invented here.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IdentityRoleRequestImpactQueryV1([property: JsonRequired] Guid UserId, [property: JsonRequired] Guid RoleId);

public enum IdentityAccessRequestImpactOutcomeV1 { Unavailable = 0, Available = 1, Denied = 2, NotFound = 3 }

/// <summary>Permission CODE union only, from native effective-user and selected-role reads. AlreadyHeldCodes does not mean
/// the same scope is held. Null SoD/scope assessments mean NOT ASSESSED, never no conflict/safe/approval. Validity dates,
/// affected operations and similar people are outside this slice. No name, assignment, token or policy is copied.</summary>
public sealed record IdentityRoleRequestImpactV1(Guid CompanyId, Guid UserId, Guid RoleId,
    IReadOnlyList<IdentityAccessPermission> CurrentPermissionCodes, IReadOnlyList<IdentityAccessPermission> RequestedRolePermissionCodes,
    IReadOnlyList<string> AddedPermissionCodes, IReadOnlyList<string> AlreadyHeldCodes, bool RoleHasCriticalPermissions,
    bool? HasSegregationOfDutiesConflict, bool? ExpandsScope);

public sealed record IdentityAccessRequestImpactAnswerV1(IdentityAccessRequestImpactOutcomeV1 Outcome, IdentityRoleRequestImpactV1? Impact = null);

public interface IIdentityAccessRequestImpactReadV1
{
    /// <summary>Server company/current actor; native GetUser/GetRole authority is preserved and refreshed before release.
    /// This is not a command preview hash, assignment request, authorization decision or AI write tool.</summary>
    Task<IdentityAccessRequestImpactAnswerV1> ReadAsync(IdentityRoleRequestImpactQueryV1 query, CancellationToken cancellationToken = default);
}
