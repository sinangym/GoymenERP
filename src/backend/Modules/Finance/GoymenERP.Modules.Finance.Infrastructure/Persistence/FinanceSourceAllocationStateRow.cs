namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

/// <summary>
/// One concurrency aggregate per recorded Finance source. Its unallocated amount is the source
/// nominal amount minus active allocations. Allocation create/release updates this row and the
/// target open item in the same save; the version prevents two different targets from consuming
/// the same remaining source amount concurrently.
/// </summary>
internal sealed class FinanceSourceAllocationStateRow : IFinanceTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid SourceId { get; set; }
    public Guid CurrencyId { get; set; }
    public string OriginalAmountText { get; set; } = string.Empty;
    public string UnallocatedAmountText { get; set; } = string.Empty;
    public long Version { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
