namespace GoymenERP.Modules.Identity.Domain.Approvals;

/// <summary>How an approved request was approved. Null on every request that is not approved.</summary>
public enum AccessGrantApprovalMode
{
    /// <summary>A second person, neither the requester nor the target, approved it (KIM-D1, ADR-004 section 6).</summary>
    SecondApproval,

    /// <summary>
    /// KIM-D5 path (a): the active founder assigned the Critical role directly; the founder is the requester and the
    /// decider, and the row records that the second approval was not applied.
    /// </summary>
    FounderDirect,
    /// <summary>KIM-D18: only the company-opened inbox under the company Admin lock.</summary>
    CompanyOpener,
}
