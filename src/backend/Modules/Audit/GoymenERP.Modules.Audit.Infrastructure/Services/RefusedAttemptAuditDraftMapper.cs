using System.Text.Json;
using System.Text.RegularExpressions;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Audit.Application.Contracts;

namespace GoymenERP.Modules.Audit.Infrastructure.Services;

/// <summary>
/// Validates a <see cref="RefusedAttemptV1"/> and maps it to the <c>Refused</c> audit draft (F-023, F023-1). Every
/// text is a closed code or an identifier pattern, so no free text, field value, e-mail or spaces can reach the row.
/// The column bounds are the persisted audit column lengths (<c>AuditEventModelBuilderExtensions</c>).
/// </summary>
internal static partial class RefusedAttemptAuditDraftMapper
{
    /// <summary>The empty before-state of a refusal: nothing changed.</summary>
    internal const string BeforeJson = "{}";

    /// <summary>
    /// A refusal without an idempotency key still needs a digest (the column is required): the digest of this
    /// technical prefix and the correlation identity, never a user value.
    /// </summary>
    internal const string MissingIdempotencyKeyPrefix = "refused-attempt:no-idempotency-key:";

    private const int EventTypeMaxLength = 160;
    private const int EntityTypeMaxLength = 160;
    private const int EntityIdMaxLength = 128;
    private const int OperationMaxLength = 64;
    private const int ActorIdMaxLength = 128;
    private const int PermissionCodeMaxLength = 160;
    private const int CorrelationMaxLength = 128;
    private const int CodeMaxLength = 128;
    private const int FieldNameMaxLength = 256;
    private const int RoleCodeMaxLength = 128;
    private const int RoleCodesJsonMaxLength = 2000;

    /// <summary>A contract bound on the field error list of one refusal; it keeps one row small.</summary>
    internal const int MaxFieldErrors = 64;

    private const int ClientErrorMinimum = 400;
    private const int ClientErrorMaximum = 499;
    private const int Unauthorized = 401;
    private const int Forbidden = 403;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static AuditEventDraft ToDraft(RefusedAttemptV1 attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        if (attempt.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("A refused attempt needs the resolved company.", nameof(attempt));
        }

        var commandName = Matching(attempt.CommandName, CommandNamePattern(), EventTypeMaxLength, nameof(attempt.CommandName));
        var operation = Matching(attempt.Operation, OperationPattern(), OperationMaxLength, nameof(attempt.Operation));
        var targetType = attempt.TargetType == RefusedAttemptV1.NoTarget
            ? RefusedAttemptV1.NoTarget
            : Matching(attempt.TargetType, TargetTypePattern(), EntityTypeMaxLength, nameof(attempt.TargetType));
        var targetId = attempt.TargetId == RefusedAttemptV1.NoTarget
            ? RefusedAttemptV1.NoTarget
            : Matching(attempt.TargetId, IdentifierPattern(), EntityIdMaxLength, nameof(attempt.TargetId));
        if (attempt.ExpectedVersion is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), "The expected version cannot be negative.");
        }

        var actorType = Allowed(attempt.ActorType, nameof(attempt.ActorType),
            AuditActorTypes.User, AuditActorTypes.System, AuditActorTypes.Integration, AuditActorTypes.Ai);
        var actorId = Matching(attempt.ActorId, IdentifierPattern(), ActorIdMaxLength, nameof(attempt.ActorId));
        var permissionCode = Matching(attempt.PermissionCode, PermissionCodePattern(), PermissionCodeMaxLength,
            nameof(attempt.PermissionCode));
        var roleCodes = RoleCodes(attempt.RoleCodes);
        var httpStatus = HttpStatus(attempt.HttpStatus);
        var problemCode = Matching(attempt.ProblemCode, CodePattern(), CodeMaxLength, nameof(attempt.ProblemCode));
        var fieldErrors = FieldErrors(attempt.FieldErrors);
        var refusalClass = RefusedAttemptClassesV1.IsDefined(attempt.RefusalClass)
            ? attempt.RefusalClass
            : throw new ArgumentOutOfRangeException(nameof(attempt), attempt.RefusalClass, "Unknown refusal class.");
        var correlationId = Matching(attempt.CorrelationId, IdentifierPattern(), CorrelationMaxLength,
            nameof(attempt.CorrelationId));
        var causationId = attempt.CausationId is null
            ? null
            : Matching(attempt.CausationId, IdentifierPattern(), CorrelationMaxLength, nameof(attempt.CausationId));
        var sourceChannel = Allowed(attempt.SourceChannel, nameof(attempt.SourceChannel),
            SourceChannels.Web, SourceChannels.Mobile, SourceChannels.Import, SourceChannels.Integration,
            SourceChannels.AiAssisted, SourceChannels.System);
        var idempotencyKey = string.IsNullOrWhiteSpace(attempt.IdempotencyKey)
            ? MissingIdempotencyKeyPrefix + correlationId
            : attempt.IdempotencyKey.Trim();
        var aiInvolvement = Allowed(attempt.AiInvolvement, nameof(attempt.AiInvolvement),
            AuditAiInvolvement.None, AuditAiInvolvement.Assisted, AuditAiInvolvement.Proposed, AuditAiInvolvement.Executed);
        var aiContextJson = AiContextJson(attempt, aiInvolvement, correlationId);

        var afterJson = JsonSerializer.Serialize(new
        {
            httpStatus,
            problemCode,
            refusalClass,
            fieldErrors = fieldErrors.Select(error => new { field = error.Field, code = error.Code }),
        }, Json);

        return new AuditEventDraft(
            attempt.CompanyId,
            commandName,
            targetType,
            targetId,
            operation,
            EntityVersionBefore: attempt.ExpectedVersion,
            EntityVersionAfter: null,
            actorType,
            actorId,
            permissionCode,
            roleCodes,
            BeforeJson,
            afterJson,
            RefusedAttemptReasonTextsV1.For(refusalClass),
            ApprovalContextJson: null,
            aiInvolvement,
            aiContextJson,
            sourceChannel,
            ClientIpAddress: null,
            UserAgent: null,
            correlationId,
            causationId,
            idempotencyKey,
            AuditOutcomes.Refused);
    }

    private static string? AiContextJson(RefusedAttemptV1 attempt, string aiInvolvement, string correlationId)
    {
        if (aiInvolvement == AuditAiInvolvement.None)
        {
            return attempt.AiContext is null
                ? null
                : throw new ArgumentException("An AI context needs an AI involvement.", nameof(attempt));
        }

        var context = attempt.AiContext
            ?? throw new ArgumentException("An AI involvement needs the trusted AI context.", nameof(attempt));
        if (context.CompanyId != attempt.CompanyId
            || !string.Equals(context.CorrelationId, correlationId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The AI context belongs to another company or request.", nameof(attempt));
        }

        return context.ToJson();
    }

    private static int HttpStatus(int status)
    {
        // 401 and 403 are refusals at the permission fence: the security channel only (KIM-D10). 5xx is not a refusal.
        return status is < ClientErrorMinimum or > ClientErrorMaximum or Unauthorized or Forbidden
            ? throw new ArgumentOutOfRangeException(nameof(status), status, "Only a business 4xx refusal is recorded.")
            : status;
    }

    private static string[] RoleCodes(IReadOnlyList<string>? roleCodes)
    {
        ArgumentNullException.ThrowIfNull(roleCodes);
        var canonical = roleCodes
            .Select(code => code?.Trim() ?? string.Empty)
            .ToArray();
        if (canonical.Any(code => code.Length == 0 || code.Length > RoleCodeMaxLength || code.Any(char.IsControl)))
        {
            throw new ArgumentException("A role code is empty, too long or carries control characters.", nameof(roleCodes));
        }

        var distinct = canonical.Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).ToArray();
        return JsonSerializer.Serialize(distinct).Length > RoleCodesJsonMaxLength
            ? throw new ArgumentException("The role codes exceed the audit column.", nameof(roleCodes))
            : distinct;
    }

    private static RefusedAttemptFieldErrorV1[] FieldErrors(IReadOnlyList<RefusedAttemptFieldErrorV1>? fieldErrors)
    {
        ArgumentNullException.ThrowIfNull(fieldErrors);
        if (fieldErrors.Count > MaxFieldErrors)
        {
            throw new ArgumentException("Too many field errors for one refusal.", nameof(fieldErrors));
        }

        return fieldErrors
            .Select(error => error is null
                ? throw new ArgumentException("A field error is missing.", nameof(fieldErrors))
                : new RefusedAttemptFieldErrorV1(
                    Matching(error.Field, FieldNamePattern(), FieldNameMaxLength, nameof(error.Field)),
                    Matching(error.Code, CodePattern(), CodeMaxLength, nameof(error.Code))))
            .ToArray();
    }

    private static string Allowed(string? value, string parameterName, params string[] allowed)
    {
        return value is not null && allowed.Contains(value, StringComparer.Ordinal)
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, value, "The value is not in the closed list.");
    }

    private static string Matching(string? value, Regex pattern, int maximumLength, string parameterName)
    {
        return value is not null && value.Length <= maximumLength && pattern.IsMatch(value)
            ? value
            : throw new ArgumentException("The value is not a canonical code or identifier.", parameterName);
    }

    // purchasing.purchase-request.approve.v1
    [GeneratedRegex(@"\A[a-z][a-z0-9-]*(?:\.[a-z][a-z0-9-]*)+\.v[1-9][0-9]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex CommandNamePattern();

    // Approve, Create, Record-Receipt
    [GeneratedRegex(@"\A[A-Za-z][A-Za-z0-9-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex OperationPattern();

    // Purchasing.PurchaseRequest
    [GeneratedRegex(@"\A[A-Za-z][A-Za-z0-9]*(?:\.[A-Za-z][A-Za-z0-9]*)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex TargetTypePattern();

    // purchasing.requests.approve
    [GeneratedRegex(@"\A[a-z][a-z0-9-]*(?:\.[a-z][a-z0-9-]*)+\z", RegexOptions.CultureInvariant)]
    private static partial Regex PermissionCodePattern();

    // validation_failed, same_key_payload_mismatch, max_length
    [GeneratedRegex(@"\A[a-z][a-z0-9]*(?:[_.-][a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    // lines[0].quantity, expectedVersion
    [GeneratedRegex(@"\A[A-Za-z][A-Za-z0-9_]*(?:\[[0-9]+\])*(?:\.[A-Za-z][A-Za-z0-9_]*(?:\[[0-9]+\])*)*\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex FieldNamePattern();

    // Opaque identities (GUID text, correlation identities): the request metadata alphabet of the correlation
    // middleware (ASCII letters, digits, '-', '_', '.', ':'); no spaces, no '@', no free text.
    [GeneratedRegex(@"\A[A-Za-z0-9._:-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}
