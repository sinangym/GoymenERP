namespace GoymenERP.Modules.Audit.Domain.Events;

public sealed class AuditEvent
{
    private AuditEvent()
    {
    }

    private AuditEvent(AuditEventData data)
    {
        Id = RequireId(data.Id, nameof(data.Id));
        CompanyId = RequireId(data.CompanyId, nameof(data.CompanyId));
        OccurredAtUtc = data.OccurredAtUtc;
        EventType = RequireText(data.EventType, nameof(data.EventType), 160);
        EntityType = RequireText(data.EntityType, nameof(data.EntityType), 160);
        EntityId = RequireText(data.EntityId, nameof(data.EntityId), 128);
        Operation = RequireText(data.Operation, nameof(data.Operation), 64);
        EntityVersionBefore = data.EntityVersionBefore;
        EntityVersionAfter = data.EntityVersionAfter;
        ActorType = RequireText(data.ActorType, nameof(data.ActorType), 32);
        ActorId = RequireText(data.ActorId, nameof(data.ActorId), 128);
        PermissionCode = RequireText(data.PermissionCode, nameof(data.PermissionCode), 160);
        RoleCodesJson = RequireText(data.RoleCodesJson, nameof(data.RoleCodesJson), 2000);
        BeforeJson = RequireText(data.BeforeJson, nameof(data.BeforeJson), int.MaxValue);
        AfterJson = RequireText(data.AfterJson, nameof(data.AfterJson), int.MaxValue);
        Reason = RequireText(data.Reason, nameof(data.Reason), 1000);
        ApprovalContextJson = NormalizeOptional(data.ApprovalContextJson);
        AiInvolvement = RequireText(data.AiInvolvement, nameof(data.AiInvolvement), 32);
        AiContextJson = NormalizeOptional(data.AiContextJson);
        SourceChannel = RequireText(data.SourceChannel, nameof(data.SourceChannel), 32);
        ClientIpAddress = NormalizeOptional(
            data.ClientIpAddress,
            nameof(data.ClientIpAddress),
            64);
        UserAgent = NormalizeOptional(data.UserAgent, nameof(data.UserAgent), 512);
        CorrelationId = RequireText(data.CorrelationId, nameof(data.CorrelationId), 128);
        CausationId = NormalizeOptional(data.CausationId, nameof(data.CausationId), 128);
        IdempotencyKeyHash = RequireSha256(
            data.IdempotencyKeyHash,
            nameof(data.IdempotencyKeyHash));
        Outcome = RequireText(data.Outcome, nameof(data.Outcome), 32);
        SchemaVersion = data.SchemaVersion > 0
            ? data.SchemaVersion
            : throw new ArgumentOutOfRangeException(nameof(data.SchemaVersion));
        IntegrityHash = RequireSha256(data.IntegrityHash, nameof(data.IntegrityHash));
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public string EntityType { get; private set; } = string.Empty;

    public string EntityId { get; private set; } = string.Empty;

    public string Operation { get; private set; } = string.Empty;

    public long? EntityVersionBefore { get; private set; }

    public long? EntityVersionAfter { get; private set; }

    public string ActorType { get; private set; } = string.Empty;

    public string ActorId { get; private set; } = string.Empty;

    public string PermissionCode { get; private set; } = string.Empty;

    public string RoleCodesJson { get; private set; } = string.Empty;

    public string BeforeJson { get; private set; } = string.Empty;

    public string AfterJson { get; private set; } = string.Empty;

    public string Reason { get; private set; } = string.Empty;

    public string? ApprovalContextJson { get; private set; }

    public string AiInvolvement { get; private set; } = string.Empty;

    public string? AiContextJson { get; private set; }

    public string SourceChannel { get; private set; } = string.Empty;

    public string? ClientIpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public string CorrelationId { get; private set; } = string.Empty;

    public string? CausationId { get; private set; }

    public string IdempotencyKeyHash { get; private set; } = string.Empty;

    public string Outcome { get; private set; } = string.Empty;

    public int SchemaVersion { get; private set; }

    public string IntegrityHash { get; private set; } = string.Empty;

    public static AuditEvent Create(AuditEventData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new AuditEvent(data);
    }

    private static Guid RequireId(Guid value, string parameterName)
    {
        return value == Guid.Empty
            ? throw new ArgumentException("Kimlik boş olamaz.", parameterName)
            : value;
    }

    private static string RequireText(
        string value,
        string parameterName,
        int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Değer boş olamaz.", parameterName);
        }

        return normalized.Length > maximumLength
            ? throw new ArgumentOutOfRangeException(
                parameterName,
                $"Değer {maximumLength} karakterden uzun olamaz.")
            : normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeOptional(
        string? value,
        string parameterName,
        int maximumLength)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : RequireText(value, parameterName, maximumLength);
    }

    private static string RequireSha256(string value, string parameterName)
    {
        var normalized = RequireText(value, parameterName, 64);
        return normalized.Length == 64
            && normalized.All(character =>
                character is >= '0' and <= '9'
                    or >= 'A' and <= 'F')
            ? normalized
            : throw new ArgumentException(
                "Değer 64 karakterlik büyük harf SHA-256 hexadecimal özeti olmalıdır.",
                parameterName);
    }
}

public sealed record AuditEventData(
    Guid Id,
    Guid CompanyId,
    DateTimeOffset OccurredAtUtc,
    string EventType,
    string EntityType,
    string EntityId,
    string Operation,
    long? EntityVersionBefore,
    long? EntityVersionAfter,
    string ActorType,
    string ActorId,
    string PermissionCode,
    string RoleCodesJson,
    string BeforeJson,
    string AfterJson,
    string Reason,
    string? ApprovalContextJson,
    string AiInvolvement,
    string? AiContextJson,
    string SourceChannel,
    string? ClientIpAddress,
    string? UserAgent,
    string CorrelationId,
    string? CausationId,
    string IdempotencyKeyHash,
    string Outcome,
    int SchemaVersion,
    string IntegrityHash);
