namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

/// <summary>
/// F-019 / API-11 v2 (SAT-D291 (1), SAT-D294): the separate reversing credit of one accepted customer return invoice, and
/// its receipt — idempotency (key hash, payload hash, actor, stored answer) and the acceptance facts. Append-only; at most
/// one per return document. <see cref="AppliedAmountText"/> is the part that reduced the original invoice's open items
/// (the sum of its <see cref="FinanceCustomerReturnCreditItemRow"/> rows) and <see cref="AdvanceAmountText"/> the rest, the
/// customer's advance (SAT-D294 (2)-(3)); together they are <see cref="AmountText"/>.
/// </summary>
internal sealed class FinanceCustomerReturnCreditRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public Guid DocumentId { get; set; }
    public string DocumentVersion { get; set; } = string.Empty;
    public Guid OriginalInvoiceId { get; set; }
    public string OriginalInvoiceVersion { get; set; } = string.Empty;
    public Guid PartyId { get; set; }
    public Guid CompanyAccountId { get; set; }
    public Guid CurrencyId { get; set; }
    public string AmountText { get; set; } = string.Empty;
    public string AppliedAmountText { get; set; } = string.Empty;
    public string AdvanceAmountText { get; set; } = string.Empty;
    public string? FunctionalAmountText { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? CausationId { get; set; }
    public DateTimeOffset AcceptedAtUtc { get; set; }
}

/// <summary>
/// F-019: one original open item reduced by a reversing credit — the reduction fact (how much of the item's outstanding
/// amount the return took). Append-only; a credit reduces an item at most once, an item may be reduced by several returns.
/// </summary>
internal sealed class FinanceCustomerReturnCreditItemRow : IFinanceTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid CreditId { get; set; }
    public Guid OpenItemId { get; set; }
    public string ReducedAmountText { get; set; } = string.Empty;
}

/// <summary>
/// F-019: one concurrency aggregate per original sales invoice that has accepted returns. <see cref="ReturnedAmountText"/>
/// is the sum of the invoice's accepted return amounts, so the total stays within the invoice's original total; the
/// version keeps two concurrent returns of the same invoice from both passing that check.
/// </summary>
internal sealed class FinanceCustomerReturnInvoiceStateRow : IFinanceTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid OriginalInvoiceId { get; set; }
    public string ReturnedAmountText { get; set; } = string.Empty;
    public long Version { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal static class FinanceCustomerReturnCreditRules
{
    /// <summary>The credit's own source kind: a customer return invoice reported by Sales (API-40).</summary>
    public const string SourceKind = "official.customer-return-invoice.v1";
}
