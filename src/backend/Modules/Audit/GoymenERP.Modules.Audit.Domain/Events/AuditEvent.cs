namespace GoymenERP.Modules.Audit.Domain.Events;

public sealed class AuditEvent
{
    private AuditEvent()
    {
    }

    private AuditEvent(AuditEventData data)
    {
        ArgumentNullException.ThrowIfNull(data.Envelope);
        var envelope = data.Envelope;
        Id = RequireId(envelope.Id, nameof(envelope.Id));
        CompanyId = RequireId(envelope.CompanyId, nameof(envelope.CompanyId));
        OccurredAtUtc = envelope.OccurredAtUtc;
        EventType = RequireText(envelope.EventType, nameof(envelope.EventType), 160);
        EntityType = RequireText(envelope.EntityType, nameof(envelope.EntityType), 160);
        EntityId = RequireText(envelope.EntityId, nameof(envelope.EntityId), 128);
        Operation = RequireText(envelope.Operation, nameof(envelope.Operation), 64);
        EntityVersionBefore = envelope.EntityVersionBefore;
        EntityVersionAfter = envelope.EntityVersionAfter;
        ActorType = RequireText(envelope.ActorType, nameof(envelope.ActorType), 32);
        ActorId = RequireText(envelope.ActorId, nameof(envelope.ActorId), 128);
        PermissionCode = RequireText(
            envelope.PermissionCode,
            nameof(envelope.PermissionCode),
            160);
        RoleCodesJson = RequireText(envelope.RoleCodesJson, nameof(envelope.RoleCodesJson), 2000);
        BeforeJson = RequireText(envelope.BeforeJson, nameof(envelope.BeforeJson), int.MaxValue);
        AfterJson = RequireText(envelope.AfterJson, nameof(envelope.AfterJson), int.MaxValue);
        Reason = RequireText(envelope.Reason, nameof(envelope.Reason), 1000);
        ApprovalContextJson = NormalizeOptional(envelope.ApprovalContextJson);
        AiInvolvement = RequireText(
            envelope.AiInvolvement,
            nameof(envelope.AiInvolvement),
            32);
        AiContextJson = NormalizeOptional(envelope.AiContextJson);
        SourceChannel = RequireText(envelope.SourceChannel, nameof(envelope.SourceChannel), 32);
        ClientIpAddress = NormalizeOptional(
            envelope.ClientIpAddress,
            nameof(envelope.ClientIpAddress),
            64);
        UserAgent = NormalizeOptional(envelope.UserAgent, nameof(envelope.UserAgent), 512);
        CorrelationId = RequireText(
            envelope.CorrelationId,
            nameof(envelope.CorrelationId),
            128);
        CausationId = NormalizeOptional(
            envelope.CausationId,
            nameof(envelope.CausationId),
            128);
        IdempotencyKeyHash = RequireSha256(
            envelope.IdempotencyKeyHash,
            nameof(envelope.IdempotencyKeyHash));
        Outcome = RequireText(envelope.Outcome, nameof(envelope.Outcome), 32);
        SchemaVersion = envelope.SchemaVersion > 0
            ? envelope.SchemaVersion
            : throw new ArgumentOutOfRangeException(nameof(envelope.SchemaVersion));
        IntegrityHash = RequireSha256(data.IntegrityHash, nameof(data.IntegrityHash));
        if (!string.Equals(
                IntegrityHash,
                AuditEventIntegrity.Compute(ToIntegrityEnvelope()),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "IntegrityHash kalıcı canonical audit zarfıyla eşleşmelidir.",
                nameof(data.IntegrityHash));
        }
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

    public AuditEventEnvelope ToIntegrityEnvelope()
    {
        return new AuditEventEnvelope(
            Id,
            CompanyId,
            OccurredAtUtc,
            EventType,
            EntityType,
            EntityId,
            Operation,
            EntityVersionBefore,
            EntityVersionAfter,
            ActorType,
            ActorId,
            PermissionCode,
            RoleCodesJson,
            BeforeJson,
            AfterJson,
            Reason,
            ApprovalContextJson,
            AiInvolvement,
            AiContextJson,
            SourceChannel,
            ClientIpAddress,
            UserAgent,
            CorrelationId,
            CausationId,
            IdempotencyKeyHash,
            Outcome,
            SchemaVersion);
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

public sealed record AuditEventEnvelope(
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
    int SchemaVersion);

public sealed record AuditEventData(
    AuditEventEnvelope Envelope,
    string IntegrityHash);
