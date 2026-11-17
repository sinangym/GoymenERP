namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>Append-only valuation captured with a real physical movement; never a historical current-average backfill.</summary>
internal sealed class MovementCostStampRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid MovementId { get; set; }
    public int LineNumber { get; set; }
    public Guid CurrencyId { get; set; }
    public int Status { get; set; }
    public decimal? UnitCostPerKg { get; set; }
    public decimal? Amount { get; set; }
    public string? UnitCostRatioJson { get; set; }
    public string? PoolEffectJson { get; set; }
    public long? LedgerVersion { get; set; }
    public long StampVersion { get; set; }
    public string? Reason { get; set; }
    public Guid? SourceIssueMovementId { get; set; }
    public int EntryCurrencyStatus { get; set; }
    public Guid? OriginalCurrencyId { get; set; }
    public decimal? ForeignUnitCostPerKg { get; set; }
    public string? ForeignUnitCostRatioJson { get; set; }
    public decimal? EntryExchangeRate { get; set; }
    public string? EntryExchangeRateRatioJson { get; set; }
    public string? FirstEntryRateJson { get; set; }
    public string CurrencySourceVersion { get; set; } = string.Empty;
    public string? CurrencyReason { get; set; }
    public string EvidenceJson { get; set; } = "{}";
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// The independent USD display average (URT-D82). It is never produced by dividing the current TL average by today's FX.
/// Every value enters at its own recorded entry/invoice rate. No company profile or activation is created here.
/// </summary>
internal sealed class CurrencyAverageCostLedgerRow : IInventoryTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid LocationId { get; set; }
    public Guid ProductId { get; set; }
    public Guid CurrencyId { get; set; }
    public int Status { get; set; }
    public int? UnreadableReason { get; set; }
    public decimal? QuantityKg { get; set; }
    public decimal? AverageCostPerKg { get; set; }
    public long Version { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
