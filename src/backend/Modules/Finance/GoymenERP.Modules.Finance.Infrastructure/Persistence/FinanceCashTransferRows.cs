namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

/// <summary>
/// FNS-D24 (API-32/34): one committed transfer between two treasury accounts of the company. The row carries BOTH legs
/// (source and target account, each with its currency, amount text and money profile) and, for a currency exchange,
/// the transaction rate; it is the single money fact of the transfer, so no separate outgoing and incoming movement is
/// written. Append-only: a correction is a separate, later decision (never an update or a delete).
/// </summary>
internal sealed class CashTransferRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public int Kind { get; set; }
    public int SourceKind { get; set; }
    public Guid SourceAccountId { get; set; }
    public Guid SourceCurrencyId { get; set; }
    public string SourceAmountText { get; set; } = string.Empty;
    public string SourceProfileId { get; set; } = string.Empty;
    public string SourceProfileVersion { get; set; } = string.Empty;
    public int TargetKind { get; set; }
    public Guid TargetAccountId { get; set; }
    public Guid TargetCurrencyId { get; set; }
    public string TargetAmountText { get; set; } = string.Empty;
    public string TargetProfileId { get; set; } = string.Empty;
    public string TargetProfileVersion { get; set; } = string.Empty;
    public string? RateText { get; set; }
    public DateOnly OccurredOn { get; set; }
    public DateOnly? ValueOn { get; set; }
    public string? Reference { get; set; }
    public string? Description { get; set; }
    public bool TransferConfirmed { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

internal static class CashTransferKinds
{
    public const int SameCurrency = 0;
    public const int CurrencyExchange = 1;
}

internal static class CashTransferAccountKinds
{
    public const int Bank = 0;
    public const int Cash = 1;
}

/// <summary>
/// One accepted transfer commit attempt (ADR-011), kept beside the transfer rather than in
/// <see cref="FinanceCommandRow"/>. (company, command, key hash) is unique; only the key's SHA-256 is kept. Only a
/// successful commit is stored (a refusal writes nothing, so a corrected retry may reuse the key). Append-only.
/// </summary>
internal sealed class CashTransferCommandRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public Guid TransferId { get; set; }
    public string ResultJson { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}
