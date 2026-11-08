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
    /// <c>required</c> and without a default: every writer names the direction, a forgotten one never becomes a
    /// silent receivable (review B P3).
    /// </summary>
    public required int Direction { get; set; }
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

    /// <summary>
    /// F2a (SAT-D172, ALS-D41): the invoice's single exchange rate for a foreign-currency invoice, exact text; null for a TL
    /// invoice and for every row admitted before F2a. Present exactly when <see cref="FunctionalAmountText"/> is.
    /// </summary>
    public string? ExchangeRateText { get; set; }

    /// <summary>F2a: the installment's functional-currency (TL) equivalent at that rate, as the producer computed it.</summary>
    public string? FunctionalAmountText { get; set; }
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

/// <summary>F2a: the stored length of an invoice exchange rate (<c>OpenItems.ExchangeRateText</c>).</summary>
internal static class FinanceOpenItemExchange
{
    public const int RateMaximumLength = 64;
}

internal static class FinanceOpenItemStatuses
{
    public const int Open = 1;
    public const int Closed = 2;

    /// <summary>F2b (SAT-D96): closed by its invoice's cancellation; reversed by an <c>InvoiceCancellationItems</c> row.</summary>
    public const int Cancelled = 3;
}

/// <summary>F2b: the stored length of a cancellation reason (<c>InvoiceCancellations.Reason</c>).</summary>
internal static class FinanceInvoiceCancellationRules
{
    public const int ReasonMaximumLength = 512;
}
