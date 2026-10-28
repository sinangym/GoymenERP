namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// SAT-D27 (1): Sales-owned consumer port for the satış sorumlusu picker — the people of the company who are active there AND
/// hold the eligibility name the Sales owner was given (<c>sales.drafts.create</c>). The central adapter delegates one bounded
/// search to Identity's user directory, which measures both the membership and the held permission in its own store; Sales
/// never reads Identity persistence and never decides who holds a grant.
/// </summary>
public interface ISalesOwnerCandidateSourceV1
{
    Task<SalesOwnerCandidateAnswerV1> SearchAsync(
        SalesOwnerCandidateRequestV1 request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>CompanyId</c> and <c>ActorId</c> are copies of the server-resolved context. <c>Query</c> is the already-trimmed display-name
/// fragment or null; <c>Limit</c> is 1..<see cref="SalesOwnerPickerLimitsV1.MaximumLimit"/>; <c>EligibilityPermission</c> is the
/// server composition's binding, never a caller value.
/// </summary>
public sealed record SalesOwnerCandidateRequestV1(
    Guid CompanyId,
    string ActorId,
    string? Query,
    int Limit,
    string EligibilityPermission);

public enum SalesOwnerCandidateOutcomeV1
{
    Success,
    Denied,
    Unavailable,
}

public sealed record SalesOwnerCandidateAnswerV1(
    SalesOwnerCandidateOutcomeV1 Outcome,
    IReadOnlyList<SalesOwnerOptionV1> Items,
    bool HasMore);

/// <summary>One eligible person: the Identity user id a Sales record would carry, and the owner's display name. Nothing else.</summary>
public sealed record SalesOwnerOptionV1(Guid UserId, string DisplayName);

/// <summary>
/// <c>GET /api/v1/sales/references/sales-owners</c>: the eligible people of <paramref name="CompanyId"/>, ordered by display
/// name then id. <paramref name="HasMore"/>: more people match than the page holds (narrow the query).
/// </summary>
public sealed record SalesOwnerOptionsV1(Guid CompanyId, IReadOnlyList<SalesOwnerOptionV1> Owners, bool HasMore);

public sealed record SalesOwnerQueryV1(string? Q, int Limit);

/// <summary>Technical bounds of one picker call (page size, fragment length) — not a business policy.</summary>
public static class SalesOwnerPickerLimitsV1
{
    public const int DefaultLimit = 25;
    public const int MaximumLimit = 50;
    public const int MaximumQueryLength = 100;
}
