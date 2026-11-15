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
    public const string ProviderUnavailable = "settings_provider_unavailable";
    public const string AccessDenied = "settings_access_denied";
}

/// <summary>
/// A new segregation of duties version of the server-resolved company. <c>PreparerMayApprove</c> is mandatory (no code
/// default in the request): <c>false</c> keeps the decided default (the preparer may not approve; a different person is
/// required), <c>true</c> is the NS-01 relaxation for a company run by one person. The version takes effect when it is
/// saved. <c>ExpectedVersion</c> is the latest stored version ("0" when none).
/// </summary>
public sealed record RecordSegregationOfDutiesPolicyRequestV1(
    string? ExpectedVersion,
    bool? PreparerMayApprove,
    string? Reason);

public sealed record SegregationOfDutiesPolicyVersionViewV1(
    string Version,
    bool PreparerMayApprove,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc,
    string Reason);

/// <summary>
/// The company's segregation of duties setting. <c>PreparerMayApprove</c> is the setting in force (the latest version, or the
/// decided default <c>false</c> when none is recorded); <c>Version</c> is the latest stored version ("0" when none) — the next
/// record's expected version. <c>Versions</c> is the history, newest first.
/// </summary>
public sealed record SegregationOfDutiesPolicyViewV1(
    Guid CompanyId,
    bool PreparerMayApprove,
    string Version,
    IReadOnlyList<SegregationOfDutiesPolicyVersionViewV1> Versions);

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
/// expected version and one audit event per real change (none for a no-op).
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
/// default (the preparer may not approve).
/// </summary>
public interface ISettingsSegregationOfDutiesPolicyProviderV1
{
    Task<SettingsSegregationOfDutiesPolicyV1> GetSegregationOfDutiesPolicyAsync(Guid companyId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The one deterministic rule every approving module applies (NS-01): a different person may always approve; the preparer
/// may approve only when the company's setting allows it. Persons are compared as the server's actor ids (trimmed, ordinal);
/// a missing preparer or approver never passes.
/// </summary>
public static class SegregationOfDutiesRuleV1
{
    /// <summary>The decided default when the company has recorded no version: the preparer may not approve.</summary>
    public const bool DefaultPreparerMayApprove = false;

    public static bool IsSamePerson(string? preparedBy, string? approvedBy)
    {
        var preparer = preparedBy?.Trim();
        var approver = approvedBy?.Trim();
        return !string.IsNullOrEmpty(preparer) && !string.IsNullOrEmpty(approver)
            && string.Equals(preparer, approver, StringComparison.Ordinal);
    }

    public static bool PermitsApproval(SettingsSegregationOfDutiesPolicyV1 policy, string? preparedBy, string? approvedBy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (string.IsNullOrWhiteSpace(preparedBy) || string.IsNullOrWhiteSpace(approvedBy))
        {
            return false;
        }

        return !IsSamePerson(preparedBy, approvedBy) || policy.PreparerMayApprove;
    }
}
