using GoymenERP.BuildingBlocks.Ai;

namespace GoymenERP.Modules.Audit.Application.Contracts;

/// <summary>
/// The closed refusal classes of a refused business attempt (F-023, F023-1; KIM-D7). Authorization, identity and
/// company-boundary refusals are not here: they belong to the security event channel only (KIM-D10).
/// </summary>
public static class RefusedAttemptClassesV1
{
    public const string Validation = "Validation";

    public const string BusinessRule = "BusinessRule";

    public const string Concurrency = "Concurrency";

    public const string Idempotency = "Idempotency";

    public const string RequestShape = "RequestShape";

    public static IReadOnlyList<string> All { get; } =
        [Validation, BusinessRule, Concurrency, Idempotency, RequestShape];

    public static bool IsDefined(string? value) =>
        value is not null && All.Contains(value, StringComparer.Ordinal);
}

/// <summary>
/// One refused field: the stable field name of the module's OpenAPI contract and the error code. A field value is
/// never carried (F-023 forbidden fields: "değer yok" rule).
/// </summary>
public sealed record RefusedAttemptFieldErrorV1(string Field, string Code);

/// <summary>
/// The restricted draft of a refused business attempt (F-023, F023-1; KIM-D7). The property list is the allow list of
/// the specification: adding a property is a contract change. There is no free text, no field value, no raw request
/// body, no raw idempotency key in the stored row (it is hashed), no IP address and no User-Agent.
/// </summary>
public sealed record RefusedAttemptV1
{
    /// <summary>The target placeholder used when the refused command has no route target.</summary>
    public const string NoTarget = "-";

    /// <summary>The server-resolved company (<c>ICompanyContext</c>); never a client header value.</summary>
    public required Guid CompanyId { get; init; }

    /// <summary>The versioned command name, for example <c>purchasing.purchase-request.approve.v1</c>.</summary>
    public required string CommandName { get; init; }

    /// <summary>The module's operation constant, for example <c>Approve</c>.</summary>
    public required string Operation { get; init; }

    /// <summary>The target entity type, or <see cref="NoTarget"/>.</summary>
    public required string TargetType { get; init; }

    /// <summary>The route target identity, or <see cref="NoTarget"/>.</summary>
    public required string TargetId { get; init; }

    /// <summary>The expected version of the request when it could be parsed.</summary>
    public long? ExpectedVersion { get; init; }

    /// <summary>One of <see cref="AuditActorTypes"/>.</summary>
    public required string ActorType { get; init; }

    /// <summary>The internal actor identity of the write envelope; never a user name or e-mail.</summary>
    public required string ActorId { get; init; }

    public required string PermissionCode { get; init; }

    public required IReadOnlyList<string> RoleCodes { get; init; }

    /// <summary>The HTTP status of the refusal: 4xx only; 401 and 403 belong to the security channel.</summary>
    public required int HttpStatus { get; init; }

    /// <summary>The problem code of the response; the problem <c>detail</c> text is never recorded.</summary>
    public required string ProblemCode { get; init; }

    public IReadOnlyList<RefusedAttemptFieldErrorV1> FieldErrors { get; init; } = [];

    /// <summary>One of <see cref="RefusedAttemptClassesV1"/>.</summary>
    public required string RefusalClass { get; init; }

    public required string CorrelationId { get; init; }

    public string? CausationId { get; init; }

    public required string SourceChannel { get; init; }

    /// <summary>The idempotency key header when present; only its SHA-256 digest is stored.</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>One of <see cref="AuditAiInvolvement"/>; <see cref="AuditAiInvolvement.None"/> without AI.</summary>
    public string AiInvolvement { get; init; } = AuditAiInvolvement.None;

    /// <summary>The trusted AI gateway context; required exactly when <see cref="AiInvolvement"/> is not None.</summary>
    public AiAuditContextV1? AiContext { get; init; }
}

/// <summary>The closed failure kinds of a standalone refused attempt write (metric tag values, low cardinality).</summary>
public static class RefusedAttemptRecordFailureKindsV1
{
    public const string InvalidAttempt = "invalid_attempt";

    public const string CompanyContextMismatch = "company_context_mismatch";

    public const string StoreUnavailable = "store_unavailable";

    public const string Timeout = "timeout";

    public static IReadOnlyList<string> All { get; } =
        [InvalidAttempt, CompanyContextMismatch, StoreUnavailable, Timeout];
}

/// <summary>
/// The result of <see cref="IRefusedAttemptRecorderV1.RecordStandaloneAsync"/>. A failed write never changes the
/// refusal the caller returns (KIM-D11): the caller answers its refusal either way.
/// </summary>
public sealed record RefusedAttemptRecordResultV1
{
    private RefusedAttemptRecordResultV1(bool recorded, string? failureKind)
    {
        Recorded = recorded;
        FailureKind = failureKind;
    }

    public bool Recorded { get; }

    /// <summary>One of <see cref="RefusedAttemptRecordFailureKindsV1"/> when <see cref="Recorded"/> is false.</summary>
    public string? FailureKind { get; }

    public static RefusedAttemptRecordResultV1 Written() => new(recorded: true, failureKind: null);

    public static RefusedAttemptRecordResultV1 NotWritten(string failureKind)
    {
        if (!RefusedAttemptRecordFailureKindsV1.All.Contains(failureKind, StringComparer.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind), failureKind, "Unknown refused attempt failure kind.");
        }

        return new(recorded: false, failureKind);
    }
}

/// <summary>
/// The only write port of refused business attempts into the audit log (F-023, F023-1; KIM-D7, KIM-D11). Modules do
/// not write the audit table outside this port (ADR-009 section 1).
/// </summary>
public interface IRefusedAttemptRecorderV1
{
    /// <summary>
    /// Terminal refusal: leaves the <c>Refused</c> audit draft on the request's audit collector so the module's own
    /// <c>SaveChanges</c> writes it in the same SQL transaction as the attempt row. Throws on an invalid attempt or a
    /// company that is not the resolved request company: that is a programming fault of the caller.
    /// </summary>
    void Record(RefusedAttemptV1 attempt);

    /// <summary>
    /// Separate short write (422, 400, losing concurrency or idempotency refusal). It never throws except for the
    /// caller's own cancellation; every failure increments <c>audit_refused_attempt_write_failed</c>.
    /// </summary>
    Task<RefusedAttemptRecordResultV1> RecordStandaloneAsync(
        RefusedAttemptV1 attempt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A replayed refusal (same idempotency key) is not a new row; it is only counted (KIM-D11).
    /// </summary>
    void NoteReplayedRefusal(string refusalClass);
}
