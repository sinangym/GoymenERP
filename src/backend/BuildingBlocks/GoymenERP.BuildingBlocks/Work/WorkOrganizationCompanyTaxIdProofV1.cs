namespace GoymenERP.BuildingBlocks.Work;

/// <summary>Private current decision identity; full facts and genuine original receipt bind each distinct digest.</summary>
public sealed record WorkOrganizationCompanyTaxIdProofV1(Guid CompanyId, Guid RequestId, long CurrentCompanyVersion,
    long CompanyVersionAtRequest, string RequestedBy, DateTimeOffset RequestedAtUtc, string Status, bool IsCurrent,
    bool FirstVkn, string ProposedVkn, string SourceFactsDigest, string PreviewDigest);
