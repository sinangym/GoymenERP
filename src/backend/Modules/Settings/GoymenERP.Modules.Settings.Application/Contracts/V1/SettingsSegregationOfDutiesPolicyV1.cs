namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// F-024 (NS-01 of the 14th answer on Issue #66): the Settings owner's name for maintaining the company's segregation of
/// duties setting — whether the person who prepared a document may also approve it. Registered, NOT granted: no role, seed
/// or user receives it here (a human grant gate). Company-wide only. Not Critical: no approved source adds grant-time dual
/// control.
/// </summary>
public static class SettingsSegregationOfDutiesPolicyPermissionManifestV1
{
    /// <summary>Read the segregation of duties setting's history and record a new version.</summary>
    public const string Manage = "settings.segregation-of-duties.manage";
}

/// <summary>Technical bounds of the inputs (storage and transport shape, not policy).</summary>
public static class SegregationOfDutiesPolicyInputLimitsV1
{
    /// <summary>SAT-D68 precedent: the mandatory reason of every change.</summary>
    public const int ReasonMaximumLength = 4000;
}

/// <summary>Published problem codes of the segregation of duties routes.</summary>
public static class SegregationOfDutiesPolicyProblemCodesV1
{
    public const string RequestInvalid = "settings_segregation_of_duties_request_invalid";
    public const string CommandMetadataInvalid = "settings_segregation_of_duties_command_metadata_invalid";
    public const string ReasonRequired = "settings_segregation_of_duties_reason_required";
    public const string ReasonTooLong = "settings_segregation_of_duties_reason_too_long";
    public const string IdempotencyConflict = "settings_segregation_of_duties_idempotency_conflict";
    public const string VersionConflict = "settings_segregation_of_duties_version_conflict";

    /// <summary>
    /// KIM-D39: the relaxation (<c>PreparerMayApprove = true</c>) may be recorded only while the company has exactly one active
    /// user; with a second active user it is refused (409, stored for the key).
    /// </summary>
    public const string RelaxationRequiresSingleUser = "settings_segregation_of_duties_relaxation_requires_single_user";
    public const string ProviderUnavailable = "settings_provider_unavailable";
    public const string AccessDenied = "settings_access_denied";
}

/// <summary>
/// A new segregation of duties version of the server-resolved company. <c>PreparerMayApprove</c> is mandatory (no code
/// default in the request): <c>false</c> keeps the decided default (the preparer may not approve; a different person is
/// required), <c>true</c> is the NS-01 relaxation for a company run by one person. KIM-D39: <c>true</c> is accepted only
/// while the company has exactly one active user, checked when it is recorded (otherwise
/// <see cref="SegregationOfDutiesPolicyProblemCodesV1.RelaxationRequiresSingleUser"/>). The version takes effect when it is
/// saved. <c>ExpectedVersion</c> is the latest stored version ("0" when none).
/// </summary>
public sealed record RecordSegregationOfDutiesPolicyRequestV1(
    string? ExpectedVersion,
    bool? PreparerMayApprove,
    string? Reason);

/// <summary>
/// One stored version. <c>RecordedAutomatically</c> (KIM-D39): the version was written by the system when a second active
/// user closed the relaxation (<c>RecordedBy</c> is then <see cref="SegregationOfDutiesRuleV1.AutomaticCloseActorId"/>), not
/// by a person.
/// </summary>
public sealed record SegregationOfDutiesPolicyVersionViewV1(
    string Version,
    bool PreparerMayApprove,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc,
    string Reason,
    bool RecordedAutomatically);

/// <summary>
/// The company's segregation of duties setting. <c>PreparerMayApprove</c> is the setting in force (the latest version, or the
/// decided default <c>false</c> when none is recorded; KIM-D39: never <c>true</c> while the company has more than one active
/// user); <c>Version</c> is the latest stored version ("0" when none) — the next record's expected version. <c>Versions</c> is
/// the history, newest first. <c>RelaxationPermitted</c> (KIM-D39): the company has exactly one active user now, so the
/// relaxation may be recorded.
/// </summary>
public sealed record SegregationOfDutiesPolicyViewV1(
    Guid CompanyId,
    bool PreparerMayApprove,
    string Version,
    IReadOnlyList<SegregationOfDutiesPolicyVersionViewV1> Versions,
    bool RelaxationPermitted);

/// <summary>A command's terminal answer (stored for replay). <c>Code</c> is null for a success.</summary>
public sealed record SegregationOfDutiesPolicyCommandAnswerV1(int StatusCode, string? Code, string Title, string Detail);

public sealed record SegregationOfDutiesPolicyCommandResultV1(
    SegregationOfDutiesPolicyCommandAnswerV1 Answer,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>
/// The segregation of duties administration (F-024). Every read and command is authorized first on
/// <see cref="SettingsSegregationOfDutiesPolicyPermissionManifestV1.Manage"/> company-wide in the server-resolved company and
/// re-measured before every save. <see cref="UnauthorizedAccessException"/> means 403. A mandatory reason, Idempotency-Key,
/// expected version and one audit event per real change (none for a no-op). KIM-D39: when the read finds a stored relaxation
/// while the company has more than one active user, it first closes it with a system version and a system audit event.
/// </summary>
public interface ISettingsSegregationOfDutiesPolicyAdministrationV1
{
    Task<SegregationOfDutiesPolicyViewV1> GetAsync(CancellationToken cancellationToken = default);

    Task<SegregationOfDutiesPolicyCommandResultV1> RecordAsync(RecordSegregationOfDutiesPolicyRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}

/// <summary>
/// The company's segregation of duties setting as a consumer reads it. <c>Version</c> is the stored version the answer came
/// from (0 = none recorded: the decided default applies, the preparer may not approve). A consumer that records an approval
/// keeps the version beside it, so the approval names the setting it was checked under.
/// </summary>
public sealed record SettingsSegregationOfDutiesPolicyV1(
    Guid CompanyId,
    bool PreparerMayApprove,
    long Version);

/// <summary>
/// The read port an approving module (dispatch notes, invoices, returns, …) asks before it lets a person approve a document
/// that person prepared. The caller passes the company of the document; it must equal the server-resolved company, otherwise
/// the answer is <c>settings_access_denied</c> (the company is never taken from the caller alone). Errors are
/// <see cref="SettingsPrerequisiteException"/> codes, in this order: cancelled → <see cref="OperationCanceledException"/>; no
/// authenticated user, no access to the company or another company → <c>settings_access_denied</c>; an unreadable store →
/// <c>settings_provider_unavailable</c>; malformed stored data → <c>settings_profile_invalid</c>. There is no fallback on an
/// error: a consumer refuses the approval rather than guess. No recorded version is not an error: it answers the decided
/// default (the preparer may not approve). KIM-D39: a stored relaxation is answered only while the company has exactly one
/// active user, measured on every read; with a second active user the answer is <c>PreparerMayApprove = false</c> (the
/// relaxation closed by itself), and an unreadable user count is <c>settings_provider_unavailable</c>, never the relaxation.
/// The answer is for the general "preparer ≠ approver" check only (KIM-D38, see <see cref="SegregationOfDutiesRuleV1"/>).
/// </summary>
public interface ISettingsSegregationOfDutiesPolicyProviderV1
{
    Task<SettingsSegregationOfDutiesPolicyV1> GetSegregationOfDutiesPolicyAsync(Guid companyId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The Settings consumer port onto the Identity owner's active users (composed by the host; Settings never reads Identity's
/// tables), KIM-D39: does the server-resolved company have exactly one active user now? The company and the actor are copies
/// of the server context. <see cref="SettingsPersonDirectoryOutcomeV1.Denied"/> is a refused fence (403);
/// <see cref="SettingsPersonDirectoryOutcomeV1.Unavailable"/> an owner failure (503). Only the yes/no answer leaves Identity.
/// </summary>
public interface ISettingsCompanyActiveUsersV1
{
    Task<SettingsCompanyActiveUsersAnswerV1> HasSingleActiveUserAsync(Guid companyId, string actorId,
        CancellationToken cancellationToken = default);
}

/// <summary><c>HasSingleActiveUser</c> is meaningful only with <see cref="SettingsPersonDirectoryOutcomeV1.Found"/>.</summary>
public sealed record SettingsCompanyActiveUsersAnswerV1(
    SettingsPersonDirectoryOutcomeV1 Outcome,
    bool HasSingleActiveUser);

/// <summary>
/// KIM-D38: which kind of "preparer ≠ approver" check a consumer applies. Only <see cref="General"/> honours the company's
/// NS-01 relaxation. <see cref="LockedDualControl"/> is every flow whose own decision locks the dual approval (KIM-D28/KIM-D35
/// tax number change, the Critical permission grant dual control, SAT-D290, and every other flow whose decision states
/// preparer ≠ approver): the preparer never approves there, whatever the setting.
/// </summary>
public enum SegregationOfDutiesControlKindV1
{
    General = 1,
    LockedDualControl = 2,
}

/// <summary>
/// The one deterministic rule every approving module applies (NS-01, KIM-D38): a different person may always approve; the
/// preparer may approve only in the general check and only when the company's setting allows it — a locked dual control never
/// relaxes. Persons are compared as the server's actor ids (trimmed, ordinal); a missing preparer or approver never passes.
/// </summary>
public static class SegregationOfDutiesRuleV1
{
    /// <summary>The decided default when the company has recorded no version: the preparer may not approve.</summary>
    public const bool DefaultPreparerMayApprove = false;

    /// <summary>
    /// KIM-D39: the <c>RecordedBy</c> of a version the system wrote when a second active user closed the relaxation, and the
    /// actor id of its audit event. Not a user id (user ids are GUIDs).
    /// </summary>
    public const string AutomaticCloseActorId = "system:settings.segregation-of-duties.auto-close";

    public static bool IsRecordedAutomatically(string? recordedBy) =>
        string.Equals(recordedBy, AutomaticCloseActorId, StringComparison.Ordinal);

    public static bool IsSamePerson(string? preparedBy, string? approvedBy)
    {
        var preparer = preparedBy?.Trim();
        var approver = approvedBy?.Trim();
        return !string.IsNullOrEmpty(preparer) && !string.IsNullOrEmpty(approver)
            && string.Equals(preparer, approver, StringComparison.Ordinal);
    }

    /// <summary>The general "preparer ≠ approver" check (<see cref="SegregationOfDutiesControlKindV1.General"/>).</summary>
    public static bool PermitsApproval(SettingsSegregationOfDutiesPolicyV1 policy, string? preparedBy, string? approvedBy) =>
        PermitsApproval(policy, SegregationOfDutiesControlKindV1.General, preparedBy, approvedBy);

    /// <summary>
    /// KIM-D38: the general check honours the setting; a locked dual control ignores it (the preparer never approves). An
    /// unknown kind is treated as locked (fail-closed).
    /// </summary>
    public static bool PermitsApproval(SettingsSegregationOfDutiesPolicyV1 policy, SegregationOfDutiesControlKindV1 kind,
        string? preparedBy, string? approvedBy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (string.IsNullOrWhiteSpace(preparedBy) || string.IsNullOrWhiteSpace(approvedBy))
        {
            return false;
        }

        if (!IsSamePerson(preparedBy, approvedBy))
        {
            return true;
        }

        return kind == SegregationOfDutiesControlKindV1.General && policy.PreparerMayApprove;
    }

    /// <summary>
    /// KIM-D38: a locked dual control (KIM-D28/KIM-D35, the Critical grant dual control, SAT-D290, …) needs no setting: only a
    /// different person approves.
    /// </summary>
    public static bool PermitsLockedDualControlApproval(string? preparedBy, string? approvedBy) =>
        !string.IsNullOrWhiteSpace(preparedBy) && !string.IsNullOrWhiteSpace(approvedBy)
        && !IsSamePerson(preparedBy, approvedBy);
}
