namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

/// <summary>
/// API-23 maker-checker proposal to reverse a recorded manual bank payment. The recorded payment and its
/// outgoing money event never change; approval appends one <see cref="OutgoingMoneyEventReversalRow"/>.
/// Status values are <see cref="ReversalProposalStatuses"/>.
/// </summary>
internal sealed class PaymentReversalProposalRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PaymentId { get; set; }
    public DateOnly OccurredOn { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int Status { get; set; }
    public string ProposedBy { get; set; } = string.Empty;
    public DateTimeOffset ProposedAtUtc { get; set; }
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public Guid? ReversalEventId { get; set; }
    public long Version { get; set; }
}

/// <summary>
/// API-23: the append-only reverse of exactly one outgoing money event. It copies the original facts (party,
/// accounts, currency, amount, profile) so a reader never re-derives them, and dates the reversal on the
/// approved proposal's day. At most one per payment.
/// </summary>
internal sealed class OutgoingMoneyEventReversalRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PaymentId { get; set; }
    public Guid OriginalEventId { get; set; }
    public Guid ProposalId { get; set; }
    public Guid PartyId { get; set; }
    public Guid CompanyAccountId { get; set; }
    public Guid TreasuryAccountId { get; set; }
    public Guid CurrencyId { get; set; }
    public string AmountText { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string ProfileVersion { get; set; } = string.Empty;
    public DateOnly OccurredOn { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}
