namespace GoymenERP.Modules.Organization.Domain.CompanySetup;

/// <summary>Owner-local immutable dedup; saved atomically with the lifecycle change it acknowledges.</summary>
public sealed class CompanySetupInboxReceipt
{
    private CompanySetupInboxReceipt() { }
    public CompanySetupInboxReceipt(Guid companyId, Guid eventId, string payloadHash, DateTimeOffset processedAtUtc)
    {
        if (companyId == Guid.Empty || eventId == Guid.Empty || payloadHash.Length != 64
            || payloadHash.Any(c => c is not (>= '0' and <= '9' or >= 'A' and <= 'F'))
            || processedAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Invalid company setup receipt.");
        CompanyId = companyId; EventId = eventId; PayloadHash = payloadHash; ProcessedAtUtc = processedAtUtc;
    }
    public Guid CompanyId { get; private set; }
    public Guid EventId { get; private set; }
    public string PayloadHash { get; private set; } = string.Empty;
    public DateTimeOffset ProcessedAtUtc { get; private set; }
}
