namespace GoymenERP.Modules.Organization.Domain.CompanySetup;

/// <summary>Organization-owned append-only company opening event.</summary>
public sealed class OrganizationCompanySetupOutboxMessage
{
    private OrganizationCompanySetupOutboxMessage() { }
    public OrganizationCompanySetupOutboxMessage(Guid id, Guid companyId, string payloadJson, DateTimeOffset occurredAtUtc,
        string correlationId, string? causationId)
    {
        if (id == Guid.Empty || companyId == Guid.Empty || string.IsNullOrWhiteSpace(payloadJson)
            || string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 128 || causationId?.Length > 128
            || occurredAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Invalid company opening event.");
        Id = id; CompanyId = companyId; PayloadJson = payloadJson; OccurredAtUtc = occurredAtUtc;
        CorrelationId = correlationId; CausationId = causationId;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string PayloadJson { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string? CausationId { get; private set; }
    public ulong Position { get; private set; }
}
