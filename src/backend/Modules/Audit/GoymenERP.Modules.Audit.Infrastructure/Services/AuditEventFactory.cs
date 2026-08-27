using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;

namespace GoymenERP.Modules.Audit.Infrastructure.Services;

internal static class AuditEventFactory
{
    private const int CurrentSchemaVersion = 1;

    public static AuditEvent Create(AuditEventDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var eventType = CanonicalRequired(draft.EventType, nameof(draft.EventType));
        var entityType = CanonicalRequired(draft.EntityType, nameof(draft.EntityType));
        var entityId = CanonicalRequired(draft.EntityId, nameof(draft.EntityId));
        var operation = CanonicalRequired(draft.Operation, nameof(draft.Operation));
        var actorType = CanonicalRequired(draft.ActorType, nameof(draft.ActorType));
        var actorId = CanonicalRequired(draft.ActorId, nameof(draft.ActorId));
        var permissionCode = CanonicalRequired(
            draft.PermissionCode,
            nameof(draft.PermissionCode));
        var beforeJson = CanonicalRequired(draft.BeforeJson, nameof(draft.BeforeJson));
        var afterJson = CanonicalRequired(draft.AfterJson, nameof(draft.AfterJson));
        var reason = CanonicalRequired(draft.Reason, nameof(draft.Reason));
        var approvalContextJson = CanonicalOptional(draft.ApprovalContextJson);
        var aiInvolvement = CanonicalRequired(
            draft.AiInvolvement,
            nameof(draft.AiInvolvement));
        var aiContextJson = CanonicalOptional(draft.AiContextJson);
        var sourceChannel = CanonicalRequired(
            draft.SourceChannel,
            nameof(draft.SourceChannel));
        var clientIpAddress = CanonicalOptional(draft.ClientIpAddress);
        var userAgent = CanonicalOptional(draft.UserAgent);
        var correlationId = CanonicalRequired(
            draft.CorrelationId,
            nameof(draft.CorrelationId));
        var causationId = CanonicalOptional(draft.CausationId);
        var idempotencyKey = CanonicalRequired(
            draft.IdempotencyKey,
            nameof(draft.IdempotencyKey));
        var outcome = CanonicalRequired(draft.Outcome, nameof(draft.Outcome));

        ValidateJson(beforeJson, nameof(draft.BeforeJson));
        ValidateJson(afterJson, nameof(draft.AfterJson));
        ValidateOptionalJson(approvalContextJson, nameof(draft.ApprovalContextJson));
        ValidateOptionalJson(aiContextJson, nameof(draft.AiContextJson));
        ValidateAllowed(
            actorType,
            nameof(draft.ActorType),
            AuditActorTypes.User,
            AuditActorTypes.System,
            AuditActorTypes.Integration,
            AuditActorTypes.Ai);
        ValidateAllowed(
            aiInvolvement,
            nameof(draft.AiInvolvement),
            AuditAiInvolvement.None,
            AuditAiInvolvement.Assisted,
            AuditAiInvolvement.Proposed,
            AuditAiInvolvement.Executed);
        ValidateAllowed(
            sourceChannel,
            nameof(draft.SourceChannel),
            SourceChannels.Web,
            SourceChannels.Mobile,
            SourceChannels.Import,
            SourceChannels.Integration,
            SourceChannels.AiAssisted,
            SourceChannels.System);
        ValidateAllowed(outcome, nameof(draft.Outcome), AuditOutcomes.Succeeded);

        var id = Guid.NewGuid();
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var roleCodesJson = JsonSerializer.Serialize(
            draft.RoleCodes
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal));
        var idempotencyKeyHash = Hash(idempotencyKey);
        var envelope = new AuditEventEnvelope(
            id,
            draft.CompanyId,
            occurredAtUtc,
            eventType,
            entityType,
            entityId,
            operation,
            draft.EntityVersionBefore,
            draft.EntityVersionAfter,
            actorType,
            actorId,
            permissionCode,
            roleCodesJson,
            beforeJson,
            afterJson,
            reason,
            approvalContextJson,
            aiInvolvement,
            aiContextJson,
            sourceChannel,
            clientIpAddress,
            userAgent,
            correlationId,
            causationId,
            idempotencyKeyHash,
            outcome,
            CurrentSchemaVersion);
        var integrityHash = AuditEventIntegrity.Compute(envelope);

        return AuditEvent.Create(new AuditEventData(envelope, integrityHash));
    }

    private static void ValidateJson(string json, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Audit JSON değeri boş olamaz.", parameterName);
        }

        try
        {
            using var _ = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Audit JSON değeri geçersiz.", parameterName, exception);
        }
    }

    private static void ValidateOptionalJson(string? json, string parameterName)
    {
        if (!string.IsNullOrWhiteSpace(json))
        {
            ValidateJson(json, parameterName);
        }
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static string CanonicalRequired(string? value, string parameterName)
    {
        var canonical = value?.Trim();
        return string.IsNullOrWhiteSpace(canonical)
            ? throw new ArgumentException("Audit değeri boş olamaz.", parameterName)
            : canonical;
    }

    private static string? CanonicalOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void ValidateAllowed(
        string value,
        string parameterName,
        params string[] allowedValues)
    {
        if (!allowedValues.Contains(value, StringComparer.Ordinal))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Audit sözleşmesinde tanımlı olmayan değer kullanılamaz.");
        }
    }
}
