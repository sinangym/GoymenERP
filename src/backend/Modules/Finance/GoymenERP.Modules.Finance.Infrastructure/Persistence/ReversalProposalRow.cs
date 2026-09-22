namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

/// <summary>
/// FIN-T06/B1-B2 persistent maker-checker proposal. The immutable original is
/// referenced; approval links the separately-created reversing source.
/// </summary>
public sealed class ReversalProposalRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid OriginalSourceId { get; set; }
    public DateOnly OccurredOn { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int Status { get; set; }
    public string ProposedBy { get; set; } = string.Empty;
    public DateTimeOffset ProposedAtUtc { get; set; }
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public Guid? ReversalSourceId { get; set; }
    public long Version { get; set; }
}

internal static class ReversalProposalStatuses
{
    public const int Pending = 0;
    public const int Approved = 1;
    public const int Rejected = 2;
}
