namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>Mutable owner pointer; immutable movement/generation effects remain the valuation history.</summary>
internal sealed class ExactCostPoolRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid LocationId { get; set; }
    public Guid ProductId { get; set; }
    public Guid CurrencyId { get; set; }
    public int Status { get; set; }
    public int? UnreadableReason { get; set; }
    public string? QuantityRatioJson { get; set; }
    public decimal? AverageCostPerKg { get; set; }
    public long Version { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
