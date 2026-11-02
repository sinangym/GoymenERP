using GoymenERP.Modules.Finance.Contracts;

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

    /// <summary>
    /// MOD-15: <see cref="FinanceOpenItemDirectionCodes.Receivable"/> or <see cref="FinanceOpenItemDirectionCodes.Payable"/>,
    /// always the one its <see cref="SourceKind"/> maps to (the database checks the pair, CK_OpenItems_Direction).
    /// </summary>
    public int Direction { get; set; } = FinanceOpenItemDirectionCodes.Receivable;
    public Guid SourceId { get; set; }
    public Guid SourceLineId { get; set; }
    public string SourceVersion { get; set; } = string.Empty;
    public Guid SourceBatchId { get; set; }
    public string SourceBatchVersion { get; set; } = string.Empty;
    public string SourceBatchChecksum { get; set; } = string.Empty;
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

internal static class FinanceOpenItemDirectionCodes
{
    public const int Receivable = (int)FinanceOpenItemDirectionV1.Receivable;
    public const int Payable = (int)FinanceOpenItemDirectionV1.Payable;

    /// <summary>The stored value for a source kind, or <c>null</c> for a kind Finance does not know.</summary>
    public static int? ForSourceKind(string? sourceKind) =>
        Contracts.FinanceOpenItemDirections.FromSourceKind(sourceKind) is { } direction ? (int)direction : null;
}

internal static class FinanceOpenItemStatuses
{
    public const int Open = 1;
    public const int Closed = 2;
}
