namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

internal sealed class FinanceJournalSourceRow : IAccountingTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SourceId { get; set; }
    public required string SourceKind { get; set; }
    public required string SourceVersion { get; set; }
    public Guid CurrencyId { get; set; }
    public required string AmountText { get; set; }
    public required string OwnerProfileId { get; set; }
    public required string OwnerProfileVersion { get; set; }
    public DateOnly OccurredOn { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public required string SourceHash { get; set; }
    public required string SourceJson { get; set; }
}
