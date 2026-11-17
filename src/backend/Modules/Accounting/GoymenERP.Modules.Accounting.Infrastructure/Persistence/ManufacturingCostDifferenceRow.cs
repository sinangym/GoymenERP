namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

internal sealed class ManufacturingCostDifferenceRow : IAccountingTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ManufacturingOrderId { get; set; }
    public Guid GenerationId { get; set; }
    public string GenerationVersion { get; set; } = "";
    public string SourceHash { get; set; } = "";
    public string SourceJson { get; set; } = "";
    public DateTimeOffset SourceOccurredAtUtc { get; set; }
    public string SourceCorrelationId { get; set; } = "";
    public string SignedTlDifference { get; set; } = "";
    public int Status { get; set; }
    public long Version { get; set; }
    public Guid? PeriodId { get; set; }
    public DateOnly? AccountingDate { get; set; }
    public DateOnly? TransactionDate { get; set; }
    public Guid? CurrencyId { get; set; }
    public Guid? DebitAccountId { get; set; }
    public Guid? CreditAccountId { get; set; }
    public string? AccountPlanVersion { get; set; }
    public string? AccountPlanSha256 { get; set; }
    public string? AccountPlanProofJson { get; set; }
    public Guid? PreparedBy { get; set; }
    public DateTimeOffset? PreparedAtUtc { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public Guid? JournalId { get; set; }
    public string? Reason { get; set; }
}
