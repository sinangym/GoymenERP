namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

internal sealed class FinanceCreditMovementRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid CreditAccountId { get; set; }
    public Guid CurrencyId { get; set; }
    public long Version { get; set; }
    public int Status { get; set; }
    public int Kind { get; set; }
    public required string SourceJson { get; set; }
    public required string SourceIdentityHash { get; set; }
    public required string MoneyProfileJson { get; set; }
    public string? OpeningPeriodVersion { get; set; }
    public Guid? BankAccountId { get; set; }
    public string? BankAmountText { get; set; }
    public Guid? ReplacesOpeningMovementId { get; set; }
    public Guid? ReversedByMovementId { get; set; }
    public required string ProposedBy { get; set; }
    public DateTimeOffset ProposedAtUtc { get; set; }
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public string? RejectionReason { get; set; }
}
internal sealed class FinanceCreditMovementCommandRow : IFinanceTenantRow
{
    public Guid CompanyId { get; set; }
    public required string Command { get; set; }
    public required string KeyHash { get; set; }
    public required string ActorId { get; set; }
    public required string PayloadHash { get; set; }
    public required string ResultJson { get; set; }
}
internal sealed class FinanceOutboxRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public ulong Position { get; set; }
    public Guid AggregateId { get; set; }
    public long AggregateVersion { get; set; }
    public required string EventType { get; set; }
    public int SchemaVersion { get; set; }
    public required string PayloadJson { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public required string CorrelationId { get; set; }
    public string? CausationId { get; set; }
}
