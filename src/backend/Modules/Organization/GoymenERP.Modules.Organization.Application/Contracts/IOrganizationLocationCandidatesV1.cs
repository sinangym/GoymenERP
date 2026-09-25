namespace GoymenERP.Modules.Organization.Application.Contracts;

/// <summary>
/// Bounded Organization-owned discovery for another module's location selector. This is a module
/// read, not a user-facing Organization list: the consuming command authorizes its own operation.
/// The implementation still requires the supplied company to be the server-resolved company.
/// </summary>
public interface IOrganizationLocationCandidatesV1
{
    Task<OrganizationLocationCandidatesV1> SearchAsync(
        Guid companyId,
        OrganizationLocationCandidateQueryV1 query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <paramref name="Limit"/> is 1..100 (default 25). <paramref name="Q"/> is optional, at most
/// 200 characters after trimming, and may not contain control characters.
/// </summary>
public sealed record OrganizationLocationCandidateQueryV1(string? Q = null, int Limit = 25);

/// <summary>
/// <paramref name="SearchWindowFull"/> means more matching Organization rows exist outside this
/// bounded window. Empty items do not claim that every possible location has been examined.
/// </summary>
public sealed record OrganizationLocationCandidatesV1(
    IReadOnlyList<OrganizationLocationCandidateV1> Items,
    bool SearchWindowFull);

/// <summary>
/// An active, non-external location and its real parent-site identity. Site activity is deliberately
/// absent: no accepted rule says an inactive site changes the location's own eligibility.
/// </summary>
public sealed record OrganizationLocationCandidateV1(
    Guid LocationId,
    string LocationCode,
    string LocationName,
    Guid SiteId,
    string SiteCode,
    string SiteName);

/// <summary>The bounded candidate query is malformed; no Organization rows were read.</summary>
public sealed class OrganizationLocationCandidateQueryException : ArgumentException
{
    public const string ErrorCode = "organization_location_candidate_invalid_query";

    public OrganizationLocationCandidateQueryException()
        : base("Lokasyon aday araması geçersiz.")
    {
    }

    public string Code => ErrorCode;
}
