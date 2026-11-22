using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Work.Domain;

namespace GoymenERP.Modules.Work.Infrastructure.ApprovalRules;

/// <summary>
/// WP1 (YOL-D18): the decided default of the approval rule table, version 0 of every kind until the company records one.
/// The dispatch note, the over-plan shipment, the weighing difference and the production plan are in the hour-scale
/// class; every other kind is in the business-day class. The class lengths themselves (4 hours, 2 business days) are the
/// Settings duration policy's decided defaults, not this table's. Changing a kind's class or escalation permission is a new
/// recorded version (Ayarlar → Onay süreleri); this list is never read once a version exists.
/// </summary>
public static class WorkApprovalRuleDefaultsV1
{
    public const string Basis = "YOL-D18";
    public const long Version = 0;

    public static IReadOnlySet<string> HourScaleKinds { get; } = new HashSet<string>(
    [
        "logistics.dispatch-note.v1",
        "logistics.plan-excess.v1",
        "logistics.weighing-difference.v1",
        "manufacturing.plan.v1",
    ], StringComparer.Ordinal);

    public static WorkApprovalDurationClassV1 DurationClassOf(string kind) =>
        HourScaleKinds.Contains(kind) ? WorkApprovalDurationClassV1.Hours : WorkApprovalDurationClassV1.BusinessDays;
}

/// <summary>The state of one rule against its owner's current declaration.</summary>
public static class WorkApprovalRuleStatesV1
{
    /// <summary>The rule names the approver permission the owner declares today.</summary>
    public const string Current = "Current";

    /// <summary>
    /// The latest recorded version was recorded against another approver permission than the owner declares now: the
    /// inbox shows the kind as unavailable (fail closed) until a new version is recorded against the owner's permission.
    /// </summary>
    public const string ApproverPermissionChanged = "ApproverPermissionChanged";
}

/// <summary>Wire names of <see cref="WorkApprovalDurationClassV1"/> and their stored codes (1 = Hours, 2 = BusinessDays).</summary>
public static class WorkApprovalDurationClassCodesV1
{
    public static int Code(WorkApprovalDurationClassV1 value) => value switch
    {
        WorkApprovalDurationClassV1.Hours => 1,
        WorkApprovalDurationClassV1.BusinessDays => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static WorkApprovalDurationClassV1? FromCode(int code) => code switch
    {
        1 => WorkApprovalDurationClassV1.Hours,
        2 => WorkApprovalDurationClassV1.BusinessDays,
        _ => null,
    };

    public static WorkApprovalDurationClassV1? FromWire(string? value) => value switch
    {
        nameof(WorkApprovalDurationClassV1.Hours) => WorkApprovalDurationClassV1.Hours,
        nameof(WorkApprovalDurationClassV1.BusinessDays) => WorkApprovalDurationClassV1.BusinessDays,
        _ => null,
    };
}

public static class WorkApprovalRuleLimitsV1
{
    public const int ReasonMaximumLength = 500;
    public const int MaximumVersionsShown = 20;
}

/// <summary>Problem codes of the approval rule table endpoints.</summary>
public static class WorkApprovalRuleProblemCodesV1
{
    public const string ValidationFailed = "work_approval_rule_validation_failed";
    public const string KindUnknown = "work_approval_rule_kind_unknown";
    public const string VersionConflict = "work_approval_rule_version_conflict";
    public const string Unchanged = "work_approval_rule_unchanged";
    public const string SameKeyPayloadMismatch = "work_approval_rule_same_key_payload_mismatch";
    public const string CommandMetadataInvalid = "work_approval_rule_command_metadata_invalid";
    public const string ConcurrentChange = "work_approval_rule_concurrent_change";
    public const string PermissionUnregistered = "work_approval_rule_permission_unregistered";
}

public sealed class WorkApprovalRuleException(int status, string code, bool isReplay = false, string? originalCorrelationId = null)
    : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public bool IsReplay { get; } = isReplay;
    public string? OriginalCorrelationId { get; } = originalCorrelationId;
}

/// <summary>One recorded version of a kind's rule as the history shows it.</summary>
public sealed record WorkApprovalRuleVersionViewV1(
    string Version,
    string DurationClass,
    string ApproverPermission,
    string? EscalationPermission,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc,
    string Reason);

/// <summary>
/// One row of the rule table: the kind and the module that owns it (<see cref="OwnerModule"/>, "sahibi"), the owner's
/// approver permission, the duration class and escalation permission in force, the version in force
/// (<see cref="Version"/>; "0" with <see cref="IsDefault"/> = the decided default, <see cref="DefaultBasis"/> names the
/// decision), its state against the owner's current declaration, who recorded it, when and why, and the newest versions.
/// A null <see cref="EscalationPermission"/> is "not defined": nothing escalates (WP2 reads it).
/// </summary>
public sealed record WorkApprovalRuleViewV1(
    string Kind,
    string OwnerModule,
    string ApproverPermission,
    string DurationClass,
    string? EscalationPermission,
    string Version,
    string VersionText,
    bool IsDefault,
    string? DefaultBasis,
    string State,
    string? RecordedBy,
    DateTimeOffset? RecordedAtUtc,
    string? Reason,
    IReadOnlyList<WorkApprovalRuleVersionViewV1> Versions);

public sealed record WorkApprovalRuleTableV1(Guid CompanyId, IReadOnlyList<WorkApprovalRuleViewV1> Rules);

/// <summary>
/// The request body of <c>PUT /api/v1/work/approval-rules/{kind}</c>: the version the caller saw (<c>"0"</c> for the
/// default), the new duration class (<c>Hours</c> / <c>BusinessDays</c>), the escalation permission (a registered name, or
/// null for "not defined") and the reason.
/// </summary>
public sealed record RecordWorkApprovalRuleRequestV1(
    string? ExpectedVersion,
    string? DurationClass,
    string? EscalationPermission,
    string? Reason);

public sealed record WorkApprovalRuleCommandResultV1(WorkApprovalRuleViewV1 Rule, bool Replayed, string CorrelationId);

/// <summary>
/// The rule the inbox applies to one kind: its duration class, escalation permission, version (0 = default) and whether
/// the rule names the approver permission the owner declares now. A rule that does not is not applied (fail closed).
/// </summary>
public sealed record WorkApprovalRuleResolutionV1(
    string Kind,
    WorkApprovalDurationClassV1 DurationClass,
    string? EscalationPermission,
    long Version,
    bool IsDefault,
    bool ApproverPermissionMatches)
{
    public string VersionText => WorkApprovalRuleVersionTextV1.Of(Kind, Version);
}

public static class WorkApprovalRuleVersionTextV1
{
    public static string Of(string kind, long version) =>
        $"work.approval-rule:{kind}:{version.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>
/// The rule table's read for the Work inbox: the rule in force for each registered source, read under the caller's
/// resolved company. No permission is needed (the inbox shows the caller's own items; rules carry no record data). Returns
/// null when the table could not be read; the inbox then shows every duration as unavailable (nothing is assumed).
/// </summary>
public interface IWorkApprovalRuleReaderV1
{
    Task<IReadOnlyDictionary<string, WorkApprovalRuleResolutionV1>?> ResolveAsync(
        Guid companyId,
        IReadOnlyList<IWorkInboxSourceV1> sources,
        CancellationToken cancellationToken);
}

/// <summary>
/// WP1: the rule table in Ayarlar. Read and change need <c>settings.work-approval-duration.manage</c> (company-wide), the
/// same permission that records the class lengths; no new permission is registered and none is granted to a role.
/// </summary>
public interface IWorkApprovalRuleServiceV1
{
    Task<WorkApprovalRuleTableV1> ListAsync(CancellationToken cancellationToken = default);

    Task<WorkApprovalRuleCommandResultV1> RecordAsync(
        string kind,
        RecordWorkApprovalRuleRequestV1 request,
        WorkCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// D6 (WP1): the business time zone the inbox counts business days and local clock times in. The existing source is the
/// P7 / SAT-D10 decision — the Europe/Istanbul rules of the server (<see cref="SystemWorkBusinessTimeZoneSourceV1"/>), the
/// same zone Sales, Purchasing and Organization use — never a fixed offset. Null means the rules are unavailable: the
/// inbox then shows business-day durations as unavailable rather than guessing.
/// </summary>
public interface IWorkBusinessTimeZoneSourceV1
{
    TimeZoneInfo? BusinessTimeZone { get; }
}

public sealed class SystemWorkBusinessTimeZoneSourceV1 : IWorkBusinessTimeZoneSourceV1
{
    /// <summary>P7 (SINAN_KARARLARI_2026-10-21) / SAT-D10: the business day is the Europe/Istanbul calendar day.</summary>
    public const string TimeZoneId = "Europe/Istanbul";

    private static readonly Lazy<TimeZoneInfo?> Zone = new(() =>
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    });

    public TimeZoneInfo? BusinessTimeZone => Zone.Value;
}
