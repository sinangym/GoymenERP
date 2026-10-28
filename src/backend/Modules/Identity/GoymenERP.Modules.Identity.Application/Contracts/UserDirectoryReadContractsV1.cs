namespace GoymenERP.Modules.Identity.Application.Contracts;

/// <summary>
/// MOD-08 (SATIS_HAZIRLIK_PLANI_v1): the Identity-owned, read-only, company-scoped user directory port another module's
/// central adapter calls to show a person by name (the Sales "satış sorumlusu", approver and actor ids) or to offer a
/// picker of the people of the company. It is a module-to-module port, not an endpoint.
/// <para>
/// Fail-closed fence on every call: the request's company and actor must be copies of the server-resolved context
/// (resolved company, authenticated caller who may access that company), and the caller must still hold an effective
/// assignment in that company when the directory is read. Anything else is <see cref="UserDirectoryOutcomeV1.Denied"/>
/// with no item; an owner failure is <see cref="UserDirectoryOutcomeV1.Unavailable"/> with no item.
/// </para>
/// <para>
/// Only a display name and the company-activity flag leave the module: no e-mail, issuer, external subject, role,
/// permission, scope or assignment. Membership is the same predicate as the company-access claim
/// (<c>IExternalIdentityResolver</c>): an active user with an active assignment of an active role inside its validity
/// and with at least one scope in the company. This port does NOT decide who may be a sales owner: the consumer names the
/// eligibility it was given as one registered permission (<see cref="UserDirectorySearchRequestV1.HoldingPermission"/>,
/// SAT-D27: the Sales owner picker names <c>sales.drafts.create</c>), and the port answers only "who holds it here", measured
/// the way the authorization service measures a grant. The permission itself never leaves the module in an answer.
/// </para>
/// <para>
/// The consumer authorizes its own record or screen first; this port answers only "who is this person in this company".
/// </para>
/// </summary>
public interface IUserDirectoryReadPortV1
{
    /// <summary>
    /// Display names for user ids the caller already holds (for example the actor ids of a record it authorized).
    /// A user with no assignment at all in the company is not answered (the same answer as an unknown id), so the port
    /// never confirms a person of another company. A user who was a member and no longer is answers with
    /// <see cref="UserDirectoryEntryV1.IsActiveInCompany"/> false, so a historical actor keeps a name.
    /// </summary>
    Task<UserDirectoryResolveAnswerV1> ResolveAsync(
        UserDirectoryResolveRequestV1 request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The people currently active in the company (see the membership predicate above), optionally narrowed by a
    /// display-name fragment and by one held permission, ordered by display name then id, at most
    /// <see cref="UserDirectoryLimitsV1.MaximumSearchLimit"/>.
    /// </summary>
    Task<UserDirectorySearchAnswerV1> SearchActiveAsync(
        UserDirectorySearchRequestV1 request,
        CancellationToken cancellationToken = default);
}

/// <summary>Technical bounds of one call (batch and page size), not a business policy.</summary>
public static class UserDirectoryLimitsV1
{
    public const int MaximumResolveCount = 100;
    public const int MaximumSearchLimit = 50;
    public const int MaximumQueryLength = 100;
}

public enum UserDirectoryOutcomeV1
{
    Unavailable = 0,
    Denied = 1,
    Found = 2,
}

/// <summary>
/// <c>CompanyId</c> and <c>ActorId</c> are copies of the server-resolved context, never caller overrides.
/// <c>UserIds</c>: 1..<see cref="UserDirectoryLimitsV1.MaximumResolveCount"/> distinct, non-empty ids.
/// </summary>
public sealed record UserDirectoryResolveRequestV1(
    Guid CompanyId,
    string ActorId,
    IReadOnlyList<Guid> UserIds);

/// <summary>
/// <c>Query</c>: an optional display-name fragment (trimmed, at most <see cref="UserDirectoryLimitsV1.MaximumQueryLength"/>
/// characters; blank means no narrowing). <c>Limit</c>: 1..<see cref="UserDirectoryLimitsV1.MaximumSearchLimit"/>.
/// <c>HoldingPermission</c> (additive, SAT-D27): when given, only the active people who hold this registered permission in the
/// company NOW are listed — an effective assignment (active, inside its validity) of an active role carrying the permission,
/// under a scope in the company; a permission that is company-wide only needs that assignment's company scope, exactly the
/// authorization service's measurement. An unknown, blank or untrimmed name is a refused request (Denied), never "everyone".
/// </summary>
public sealed record UserDirectorySearchRequestV1(
    Guid CompanyId,
    string ActorId,
    string? Query,
    int Limit,
    string? HoldingPermission = null);

public sealed record UserDirectoryResolveAnswerV1(
    UserDirectoryOutcomeV1 Outcome,
    IReadOnlyList<UserDirectoryEntryV1> Items);

/// <summary><c>HasMore</c>: more active people match than the page holds (narrow the query).</summary>
public sealed record UserDirectorySearchAnswerV1(
    UserDirectoryOutcomeV1 Outcome,
    IReadOnlyList<UserDirectoryEntryV1> Items,
    bool HasMore);

/// <summary>
/// One person as the directory shows them in the requested company. <c>IsActiveInCompany</c>: the user is active and
/// holds an effective assignment in the company now (the company-access predicate).
/// </summary>
public sealed record UserDirectoryEntryV1(
    Guid UserId,
    string DisplayName,
    bool IsActiveInCompany);
