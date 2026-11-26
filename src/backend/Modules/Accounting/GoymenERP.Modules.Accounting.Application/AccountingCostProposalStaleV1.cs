namespace GoymenERP.Modules.Accounting.Application;

/// <summary>YOL-D40: closure evidence only; original preparation/source amounts are retained unchanged.</summary>
public sealed record AccountingCostProposalStaleClosureV1(Guid EventId, long PreparedVersion, Guid PreparedBy,
    DateTimeOffset ClosedAtUtc, string ReasonCode, string ReasonText, string CorrelationId, string? CausationId);

/// <summary>Fresh proposal lineage. The prior stale proposal remains closed and immutable.</summary>
public sealed record AccountingCostProposalRenewalV1(Guid PreviousProposalId, long PreviousClosedVersion, Guid ClosureEventId);

/// <summary>Price-free immutable owner event. PreparedBy is the recorded preparer, never the approving caller.</summary>
public sealed record AccountingCostProposalStalePayloadV1(Guid CompanyId, Guid ProposalId, long ProposalVersion,
    Guid PreparedBy, string ReasonCode, string ReasonText, DateTimeOffset ClosedAtUtc);

public static class AccountingCostProposalStaleEventV1
{
    public const string SourceName = "accounting.cost-proposals";
    public const string EventType = "accounting.inventory-cost.proposal-stale.v1";
    public const string ReasonCode = "inventory_cost_preparation_stale";
    public const string ReasonText = "Bayatladı";
}
