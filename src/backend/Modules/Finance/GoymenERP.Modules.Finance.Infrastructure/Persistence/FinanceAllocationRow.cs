namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

/// <summary>
/// One F-03 distribution of a recorded Finance source to one owner open item. It is not a second
/// money event: the row only records how an existing source is applied. The target owner contract
/// and allocation command are deliberately separate work; until they land, no runtime service can
/// create this row. Keeping the persistence owner here lets reversal fail closed as soon as a real
/// allocation exists instead of relying on the old "no subsystem" assumption.
/// </summary>
internal sealed class FinanceAllocationRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SourceId { get; set; }
    public Guid OpenItemId { get; set; }
    public Guid CurrencyId { get; set; }
    public string AmountText { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int Status { get; set; }
    public long Version { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string? ReleasedBy { get; set; }
    public DateTimeOffset? ReleasedAtUtc { get; set; }
    public string? ReleaseReason { get; set; }
}

internal static class FinanceAllocationStatuses
{
    public const int Active = 1;
    public const int Released = 2;
}
