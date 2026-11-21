namespace GoymenERP.Modules.Organization.Domain.CompanySetup;

/// <summary>Organization-owned global opening key: changing actor or target cannot mint another company under it.</summary>
public sealed class CompanyOpeningExecution
{
    private CompanyOpeningExecution() { }
    public CompanyOpeningExecution(string keyHash, Guid companyId, Guid actorUserId, string requestHash,
        int statusCode, string responseJson, string originalCorrelationId, DateTimeOffset recordedAtUtc)
    {
        if (!Hash(keyHash) || !Hash(requestHash) || companyId == Guid.Empty || actorUserId == Guid.Empty
            || statusCode is not (201 or 409) || string.IsNullOrWhiteSpace(responseJson)
            || string.IsNullOrWhiteSpace(originalCorrelationId) || originalCorrelationId.Length > 128
            || recordedAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Invalid company-opening terminal.");
        IdempotencyKeyHash = keyHash; CompanyId = companyId; ActorUserId = actorUserId; RequestHash = requestHash;
        StatusCode = statusCode; ResponseJson = responseJson; OriginalCorrelationId = originalCorrelationId; RecordedAtUtc = recordedAtUtc;
    }
    public string IdempotencyKeyHash { get; private set; } = string.Empty;
    public Guid CompanyId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string RequestHash { get; private set; } = string.Empty;
    public int StatusCode { get; private set; }
    public string ResponseJson { get; private set; } = string.Empty;
    public string OriginalCorrelationId { get; private set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; private set; }
    private static bool Hash(string value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');
}
