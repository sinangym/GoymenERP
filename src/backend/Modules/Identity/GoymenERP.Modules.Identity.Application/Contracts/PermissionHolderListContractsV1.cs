namespace GoymenERP.Modules.Identity.Application.Contracts;

/// <summary>
/// WP2 (YOL-D17, YOL-D28, AI-D29): the active members of the server-resolved company who hold ONE registered permission NOW,
/// with the scope of their holding — for Work's approval follow-up cycle (the "şimdi" notification of a waiting approval, the
/// one reminder, the escalation and "sahipsiz onay"). System work: no person asks, so the fence is the resolved company only;
/// no user is impersonated. The same measurement as the authorization service (active assignment inside its validity, active
/// role carrying the permission, a scope in the company; a company-wide-only permission needs the company scope). Only user
/// ids and scope ids leave the module: no name, role, assignment or other permission. A module-to-module port, not an endpoint.
/// Bounded: more than <see cref="PermissionHolderListLimitsV1.MaximumHolders"/> holders answers
/// <see cref="UserDirectoryOutcomeV1.Unavailable"/> (never a partial list).
/// </summary>
public interface IPermissionHolderListPortV1
{
    Task<PermissionHolderListAnswerV1> ListAsync(PermissionHolderListRequestV1 request, CancellationToken cancellationToken = default);
}

public static class PermissionHolderListLimitsV1
{
    public const int MaximumHolders = 500;
}

/// <summary><c>CompanyId</c> must be the server-resolved company; <c>Permission</c> a registered name spelled exactly.</summary>
public sealed record PermissionHolderListRequestV1(Guid CompanyId, string Permission);

public sealed record PermissionHolderV1(
    Guid UserId,
    bool IsCompanyWide,
    IReadOnlyList<Guid> SiteIds,
    IReadOnlyList<Guid> LocationIds,
    IReadOnlyList<Guid> WorkCenterIds);

/// <summary><c>Holders</c> (ascending by user id) is meaningful only with <see cref="UserDirectoryOutcomeV1.Found"/>.</summary>
public sealed record PermissionHolderListAnswerV1(UserDirectoryOutcomeV1 Outcome, IReadOnlyList<PermissionHolderV1> Holders);
