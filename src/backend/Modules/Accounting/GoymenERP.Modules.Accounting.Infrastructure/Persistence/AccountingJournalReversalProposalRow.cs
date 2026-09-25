namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

internal sealed class AccountingJournalReversalProposalRow : IAccountingTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SourceJournalId { get; set; }
    public Guid? ReversalJournalId { get; set; }
    public Guid PeriodId { get; set; }
    public DateOnly TransactionDate { get; set; }
    public DateOnly AccountingDate { get; set; }
    public DateOnly? ValueDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid ProposedByUserId { get; set; }
    public DateTimeOffset ProposedAtUtc { get; set; }
    public int Status { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public int Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
