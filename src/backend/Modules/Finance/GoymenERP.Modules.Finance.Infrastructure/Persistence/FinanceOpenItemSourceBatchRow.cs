namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

/// <summary>
/// Append-only admission receipt for one approved source batch. It proves the exact version and
/// checksum admitted under one idempotency key; it is not the external file or an approval record.
/// </summary>
internal sealed class FinanceOpenItemSourceBatchRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty;
    public string SourceChecksum { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? CausationId { get; set; }
    public DateTimeOffset AdmittedAtUtc { get; set; }
}
