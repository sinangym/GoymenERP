namespace GoymenERP.Modules.Identity.Application.Contracts;

/// <summary>
/// The Identity module's own permission names that the central catalogue (<c>ErpPermissions</c>) is asked to
/// register. Registration grants nothing; a grant stays a human gate (ADR-004, AI_YETKI_SOZLESMESI).
/// </summary>
public static class IdentityPermissionManifestV1
{
    public const int Version = 1;

    /// <summary>
    /// AT-004 read-only access view (SY-01 / SY-02 read subset): per user, the roles, permissions, scopes, validity
    /// and approver they hold in the server-resolved company, and per role, its permissions and its holders there.
    /// <para>
    /// Company-wide only, argued from what the permission MEANS: the view answers "who holds what in this company",
    /// and a site- or location-scoped holder would see every assignment of the company anyway (an assignment has no
    /// narrower dimension of its own to filter by). The service also refuses a non-company-wide scope, which is
    /// corroboration, not the reason.
    /// </para>
    /// <para>
    /// Critical, by the same reasoning that made <c>identity.audit.read</c> Critical: it discloses identity
    /// governance (who can approve, who can move money), and <c>Critical</c> is the grant-time dual-control set
    /// for identity governance (ADR-004 §6: the requester or target may not approve their own grant). It adds no
    /// command-time behaviour. If Sinan decides otherwise, this is the one line that moves.
    /// </para>
    /// </summary>
    public const string AssignmentsRead = "identity.assignments.read";

    public static IReadOnlySet<string> Names { get; } = new HashSet<string>(
        [AssignmentsRead],
        StringComparer.Ordinal);
}

/// <summary>
/// AT-004 read-only access view. Every answer is fenced to the server-resolved company context and re-authorized
/// on <see cref="IdentityPermissionManifestV1.AssignmentsRead"/> company-wide. Nothing here writes, grants or
/// revokes: assignment and revocation are blocked (NEEDS-SINAN #66 5738113623, OPEN-013).
/// </summary>
public interface IIdentityAccessReadService
{
    /// <summary>Users holding at least one assignment (any state) in the context company. Empty when not authorized.</summary>
    Task<IReadOnlyList<IdentityAccessUserSummary>> GetUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// One user's assignments in the context company. Null — the same code-less 404 — for an unknown user, a user
    /// with no assignment in this company, and a caller without the company-wide read.
    /// </summary>
    Task<IdentityAccessUserDetail?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The role catalogue (roles are group-wide definitions) with holder counts in the context company.</summary>
    Task<IReadOnlyList<IdentityAccessRoleSummary>> GetRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>One role's permissions and its holders in the context company. Null for an unknown role or no read.</summary>
    Task<IdentityAccessRoleDetail?> GetRoleAsync(Guid roleId, CancellationToken cancellationToken = default);
}

/// <summary>Server-computed validity of one assignment at read time, so no client clock decides it.</summary>
public static class IdentityAccessValidity
{
    /// <summary>Counts toward authorization now.</summary>
    public const string Effective = "effective";

    /// <summary>Active, but its validity starts later.</summary>
    public const string Scheduled = "scheduled";

    /// <summary>Its validity ended, or it was marked expired.</summary>
    public const string Ended = "ended";

    /// <summary>Revoked.</summary>
    public const string Revoked = "revoked";

    /// <summary>Inside its validity but not effective: the role or the user is passive, or it has no scope.</summary>
    public const string Inactive = "inactive";
}

public sealed record IdentityAccessUserSummary(
    Guid UserId,
    string DisplayName,
    bool IsActive,
    int AssignmentCount,
    int EffectiveAssignmentCount);

public sealed record IdentityAccessUserDetail(
    Guid UserId,
    string DisplayName,
    bool IsActive,
    IReadOnlyList<IdentityAccessAssignment> Assignments,
    IReadOnlyList<IdentityAccessEffectivePermission> EffectivePermissions);

/// <summary>A user named on an assignment (its approver). Display name only: no e-mail, no external subject.</summary>
public sealed record IdentityAccessPerson(Guid UserId, string DisplayName);

/// <summary>One scope of an assignment. <c>Type</c> is Company, Site, Location or WorkCenter.</summary>
public sealed record IdentityAccessScope(string Type, Guid Id);

public sealed record IdentityAccessAssignment(
    Guid AssignmentId,
    Guid RoleId,
    string RoleCode,
    string RoleName,
    bool RoleIsActive,
    string Status,
    string Validity,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidUntilUtc,
    DateTimeOffset CreatedAtUtc,
    IdentityAccessPerson? ApprovedBy,
    IReadOnlyList<IdentityAccessScope> Scopes);

/// <summary>A permission the user holds NOW in this company, and which of their effective roles give it.</summary>
public sealed record IdentityAccessEffectivePermission(
    string Code,
    bool IsCritical,
    IReadOnlyList<string> RoleCodes);

public sealed record IdentityAccessPermission(string Code, bool IsCritical);

public sealed record IdentityAccessRoleSummary(
    Guid RoleId,
    string Code,
    string Name,
    bool IsSystem,
    bool IsActive,
    int PermissionCount,
    int HolderCount,
    int EffectiveHolderCount);

public sealed record IdentityAccessRoleDetail(
    Guid RoleId,
    string Code,
    string Name,
    bool IsSystem,
    bool IsActive,
    IReadOnlyList<IdentityAccessPermission> Permissions,
    IReadOnlyList<IdentityAccessRoleHolder> Holders);

/// <summary>One assignment of the role in this company, with the holder it belongs to.</summary>
public sealed record IdentityAccessRoleHolder(
    Guid AssignmentId,
    Guid UserId,
    string DisplayName,
    bool UserIsActive,
    string Status,
    string Validity,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidUntilUtc,
    IdentityAccessPerson? ApprovedBy,
    IReadOnlyList<IdentityAccessScope> Scopes);
