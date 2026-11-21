namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

internal sealed class FinancePartyBalanceTransferRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PredecessorPartyId { get; set; }
    public Guid PredecessorAccountId { get; set; }
    public Guid SuccessorPartyId { get; set; }
    public Guid SuccessorAccountId { get; set; }
    public required string OwnerProofJson { get; set; }
    public required string EvidenceJson { get; set; }
    public required string CurrenciesJson { get; set; }
    public required string AssignmentSnapshotJson { get; set; }
    public required string SnapshotVersion { get; set; }
    public DateOnly OccurredOn { get; set; }
    public required string Reason { get; set; }
    public required string RecordedBy { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}

internal static class FinanceBalanceAssignmentKinds
{
    public const int OpenItem = 1;
    public const int Receipt = 2;
    public const int Payment = 3;
}

/// <summary>
/// A bounded available allocation amount of an existing Finance fact, held by one beneficiary. Original document/account identity never changes.
/// This is not a ledger movement. Later corrections still use the original account, as explicitly decided.
/// The immutable transfer's snapshot records every assignment's before/after and source version.
/// </summary>
internal sealed class FinancePartyBalanceAssignmentRow : IFinanceTenantRow
{
    public Guid CompanyId { get; set; }
    public int Kind { get; set; }
    public Guid SourceId { get; set; }
    public Guid OriginalPartyId { get; set; }
    public Guid OriginalAccountId { get; set; }
    public Guid CurrentPartyId { get; set; }
    public Guid CurrentAccountId { get; set; }
    public Guid CurrencyId { get; set; }
    public required string AvailableAmountText { get; set; }
    public Guid LastTransferId { get; set; }
    public long Version { get; set; }
}

internal sealed class FinancePartyBalanceTransferCommandRow : IFinanceTenantRow
{
    public Guid CompanyId { get; set; }
    public required string KeyHash { get; set; }
    public required string ActorId { get; set; }
    public required string PayloadHash { get; set; }
    public required string ResultJson { get; set; }
}
