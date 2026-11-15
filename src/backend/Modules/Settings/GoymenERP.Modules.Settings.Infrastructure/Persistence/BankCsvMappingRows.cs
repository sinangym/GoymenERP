namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// One immutable CSV column mapping version of one bank of a company (FNS-D52; insert-only). The codes are stored as their
/// published names, so a stored row reads back without a numbering table. Column numbers are 1-based; null means the bank's
/// file has no such column.
/// </summary>
internal sealed class BankCsvMappingVersionRow
{
    public Guid CompanyId { get; set; }
    public string BankKey { get; set; } = string.Empty;
    public long Version { get; set; }
    public Guid ProfileId { get; set; }
    public string BankDisplayName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Delimiter { get; set; } = string.Empty;
    public int SkipLeadingRows { get; set; }
    public bool HasHeaderRow { get; set; }
    public string DecimalSeparator { get; set; } = string.Empty;
    public string ThousandsSeparator { get; set; } = string.Empty;
    public string DateFormat { get; set; } = string.Empty;
    public string AmountLayout { get; set; } = string.Empty;
    public string? SignedAmountPositiveMeans { get; set; }
    public int TransactionDateColumn { get; set; }
    public int? ValueDateColumn { get; set; }
    public int DescriptionColumn { get; set; }
    public int? AmountColumn { get; set; }
    public int? InflowColumn { get; set; }
    public int? OutflowColumn { get; set; }
    public int? BalanceColumn { get; set; }
    public int? ReferenceColumn { get; set; }
    public int? CurrencyColumn { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
