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
        ValidateJson(draft.BeforeJson, nameof(draft.BeforeJson));
        ValidateJson(draft.AfterJson, nameof(draft.AfterJson));
        ValidateOptionalJson(draft.ApprovalContextJson, nameof(draft.ApprovalContextJson));
        ValidateOptionalJson(draft.AiContextJson, nameof(draft.AiContextJson));
        ValidateAllowed(
            draft.ActorType,
            nameof(draft.ActorType),
            AuditActorTypes.User,
            AuditActorTypes.System,
            AuditActorTypes.Integration,
            AuditActorTypes.Ai);
        ValidateAllowed(
            draft.AiInvolvement,
            nameof(draft.AiInvolvement),
            AuditAiInvolvement.None,
            AuditAiInvolvement.Assisted,
            AuditAiInvolvement.Proposed,
            AuditAiInvolvement.Executed);
        ValidateAllowed(
            draft.SourceChannel,
            nameof(draft.SourceChannel),
            SourceChannels.Web,
            SourceChannels.Mobile,
            SourceChannels.Import,
            SourceChannels.Integration,
            SourceChannels.AiAssisted,
            SourceChannels.System);
        ValidateAllowed(draft.Outcome, nameof(draft.Outcome), AuditOutcomes.Succeeded);

        var id = Guid.NewGuid();
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var roleCodesJson = JsonSerializer.Serialize(
            draft.RoleCodes
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal));
        var idempotencyKeyHash = Hash(draft.IdempotencyKey.Trim());
        var hashPayload = new
        {
            Id = id,
            draft.CompanyId,
            OccurredAtUtc = occurredAtUtc,
            draft.EventType,
            draft.EntityType,
            draft.EntityId,
            draft.Operation,
            draft.EntityVersionBefore,
            draft.EntityVersionAfter,
            draft.ActorType,
            draft.ActorId,
            draft.PermissionCode,
            RoleCodesJson = roleCodesJson,
            draft.BeforeJson,
            draft.AfterJson,
            draft.Reason,
            draft.ApprovalContextJson,
            draft.AiInvolvement,
            draft.AiContextJson,
            draft.SourceChannel,
            draft.ClientIpAddress,
            draft.UserAgent,
            draft.CorrelationId,
            draft.CausationId,
            IdempotencyKeyHash = idempotencyKeyHash,
            draft.Outcome,
            SchemaVersion = CurrentSchemaVersion,
        };
        var integrityHash = Hash(JsonSerializer.Serialize(hashPayload));

        return AuditEvent.Create(new AuditEventData(
            id,
            draft.CompanyId,
            occurredAtUtc,
            draft.EventType,
            draft.EntityType,
            draft.EntityId,
            draft.Operation,
            draft.EntityVersionBefore,
            draft.EntityVersionAfter,
            draft.ActorType,
            draft.ActorId,
            draft.PermissionCode,
            roleCodesJson,
            draft.BeforeJson,
            draft.AfterJson,
            draft.Reason,
            draft.ApprovalContextJson,
            draft.AiInvolvement,
            draft.AiContextJson,
            draft.SourceChannel,
            draft.ClientIpAddress,
            draft.UserAgent,
            draft.CorrelationId,
            draft.CausationId,
            idempotencyKeyHash,
            draft.Outcome,
            CurrentSchemaVersion,
            integrityHash));
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
