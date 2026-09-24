namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

/// <summary>
/// Finance-owned operational open item (DEC-FIN-005). The commercial/accounting owner identity is
/// retained as source kind/id/version; no invoice or opening source is invented by this row. A later
/// owner adapter must validate those facts before insertion. Amounts remain lossless text and one
/// allocation changes only the outstanding projection in the same Finance transaction.
/// </summary>
internal sealed class FinanceOpenItemRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public Guid? SourceLineId { get; set; }
    public string SourceVersion { get; set; } = string.Empty;
    public Guid PartyId { get; set; }
    public Guid CompanyAccountId { get; set; }
    public Guid CurrencyId { get; set; }
    public string OriginalAmountText { get; set; } = string.Empty;
    public string OutstandingAmountText { get; set; } = string.Empty;
    public DateOnly? DueOn { get; set; }
    public int Status { get; set; }
    public long Version { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal static class FinanceOpenItemStatuses
{
    public const int Open = 1;
    public const int Closed = 2;
}
