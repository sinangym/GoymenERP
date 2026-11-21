namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

/// <summary>
/// FNS-D25 (API-25): the payment not tied to a party, from preparation through a separately confirmed record. The
/// recorded row is the single money fact of the payment (no second movement is written for it). The expense item
/// is kept by identity (<see cref="ExpenseItemId"/>), never by text.
/// </summary>
internal sealed class ExpensePaymentRow : IFinanceNumberedSource
{
    public string? DocumentNumber { get; set; }
    public string? DocumentNumberFormatVersion { get; set; }
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public int SourceKind { get; set; }
    public Guid TreasuryAccountId { get; set; }
    public Guid ExpenseItemId { get; set; }
    public Guid CurrencyId { get; set; }
    public string AmountText { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string ProfileVersion { get; set; } = string.Empty;
    public DateOnly OccurredOn { get; set; }
    public DateOnly? ValueOn { get; set; }
    public string? Reference { get; set; }
    public string Description { get; set; } = string.Empty;
    public int Status { get; set; }
    public string PreparedBy { get; set; } = string.Empty;
    public DateTimeOffset PreparedAtUtc { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public string? RecordedBy { get; set; }
    public DateTimeOffset? RecordedAtUtc { get; set; }
    public bool PaymentConfirmed { get; set; }
}

internal static class ExpensePaymentStatuses
{
    public const int Draft = 0;
    public const int Approved = 1;
    public const int Recorded = 2;
}

internal static class ExpensePaymentSourceKinds
{
    public const int Bank = 0;
    public const int Cash = 1;
}

/// <summary>
/// One accepted expense-payment command attempt (ADR-011), kept beside the payment rather than in
/// <see cref="FinanceCommandRow"/>, so the shared command table and its target check stay untouched. (company,
/// command, key hash) is unique; only the key's SHA-256 is kept. A durable refusal for a missing payment has no
/// target. Append-only.
/// </summary>
internal sealed class ExpensePaymentCommandRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public Guid? PaymentId { get; set; }
    public string ResultJson { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}
