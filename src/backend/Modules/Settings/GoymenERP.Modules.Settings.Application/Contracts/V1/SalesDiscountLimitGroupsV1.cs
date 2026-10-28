namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// SAT-D61…SAT-D70 (<c>docs/decisions/SINAN_KARARLARI_2026-10-27_SATIS_EK10.md</c>, <c>…_EK11.md</c>): the Settings owner's
/// permission names for the Sales discount limit groups, their limits and the person → group bindings.
/// </summary>
public static class SettingsPermissionManifestV1
{
    /// <summary>
    /// SAT-D65/D68/D69/D70: ONE separate name for every edit of the limit groups (create, rename, deactivate), their limit
    /// (a fixed percent or a linked group + extra points) and the person → group bindings, and for reading them.
    /// Company-wide only; NOT Critical (SAT-D69). Registered, not granted: no role, seed or user receives it here — giving it
    /// to the board members and to the Admin persona is a human grant gate (SAT-D65, SAT-D69).
    /// </summary>
    public const string SalesDiscountLimitsManage = "settings.sales-discount-limits.manage";
}

/// <summary>The two forms of a limit record (SAT-D36, SAT-D62).</summary>
public static class SalesDiscountLimitKindsV1
{
    /// <summary>A fixed maximum discount percent, 0…100.</summary>
    public const string Fixed = "Fixed";

    /// <summary>SAT-D62: another group's limit + extra percentage points, capped at 100.</summary>
    public const string Linked = "Linked";

    public static bool IsKnown(string? kind) => kind is Fixed or Linked;
}

/// <summary>Technical bounds of the inputs (storage and transport shape, not business policy).</summary>
public static class SalesDiscountLimitInputLimitsV1
{
    public const int CodeMaximumLength = 32;
    public const int NameMaximumLength = 100;

    /// <summary>SAT-D68: the mandatory reason, the same bound as the other Sales/Settings reasons.</summary>
    public const int ReasonMaximumLength = 4000;

    /// <summary>Decimal places a percent or extra points may carry; a longer value is refused, never rounded.</summary>
    public const int PercentMaximumScale = 4;

    public const int PeopleSearchDefaultLimit = 25;
    public const int PeopleSearchMaximumLimit = 50;
    public const int PeopleSearchMaximumQueryLength = 100;
}

/// <summary>Published problem codes of the limit administration routes.</summary>
public static class SalesDiscountLimitProblemCodesV1
{
    public const string RequestInvalid = "settings_discount_limit_request_invalid";
    public const string CommandMetadataInvalid = "settings_discount_limit_command_metadata_invalid";
    public const string ReasonRequired = "settings_discount_limit_reason_required";
    public const string ReasonTooLong = "settings_discount_limit_reason_too_long";
    public const string IdempotencyConflict = "settings_discount_limit_idempotency_conflict";
    public const string VersionConflict = "settings_discount_limit_version_conflict";
    public const string GroupCodeTaken = "settings_discount_limit_group_code_taken";
    public const string GroupNameTaken = "settings_discount_limit_group_name_taken";
    public const string GroupInactive = "settings_discount_limit_group_inactive";
    public const string LinkedGroupNotFound = "settings_discount_limit_linked_group_not_found";
    public const string LinkedGroupInactive = "settings_discount_limit_linked_group_inactive";
    public const string LinkCycle = "settings_discount_limit_link_cycle";
    public const string EffectiveFromPast = "settings_discount_limit_effective_from_past";
    public const string EffectiveFromBeforeScheduled = "settings_discount_limit_effective_from_before_scheduled";
    public const string RecordInEffect = "settings_discount_limit_record_in_effect";
    public const string RecordWithdrawn = "settings_discount_limit_record_withdrawn";
    public const string BindingCompaniesMismatch = "settings_discount_limit_binding_companies_mismatch";
    public const string BindingPersonNotMember = "settings_discount_limit_binding_person_not_member";
    public const string BindingGroupNotFound = "settings_discount_limit_binding_group_not_found";
    public const string BindingGroupInactive = "settings_discount_limit_binding_group_inactive";
    public const string ProviderUnavailable = "settings_provider_unavailable";
}

/// <summary>Create a limit group in the server-resolved company. <c>Code</c> is the stable cross-company key (SAT-D66).</summary>
public sealed record CreateSalesDiscountLimitGroupRequestV1(string? Code, string? Name, string? Reason);

/// <summary>Rename a group; <c>ExpectedVersion</c> is the group's version as decimal text.</summary>
public sealed record RenameSalesDiscountLimitGroupRequestV1(string? ExpectedVersion, string? Name, string? Reason);

/// <summary>Deactivate a group (never deleted). A deactivated group contributes no limit.</summary>
public sealed record DeactivateSalesDiscountLimitGroupRequestV1(string? ExpectedVersion, string? Reason);

/// <summary>
/// Record the group's limit as a new immutable version (SAT-D36, SAT-D62): <c>Kind</c> <c>Fixed</c> with <c>Percent</c>
/// (0…100), or <c>Linked</c> with <c>LinkedGroupId</c> (another group of the same company) and <c>ExtraPoints</c> (0…100).
/// Percents travel as lossless decimal text. SAT-D78: <c>EffectiveFrom</c> is the business date the record takes effect
/// on (omitted = today's Europe/Istanbul business date); it may be a later date and then applies by itself on that date.
/// A date before today, or before the group's latest (possibly scheduled) record, is refused; a record on the same date as
/// a scheduled one supersedes it, and a scheduled record can be withdrawn or corrected before its date (SAT-D84).
/// </summary>
public sealed record SetSalesDiscountLimitRequestV1(
    string? ExpectedVersion,
    string? Kind,
    string? Percent,
    Guid? LinkedGroupId,
    string? ExtraPoints,
    string? Reason,
    DateOnly? EffectiveFrom = null);

/// <summary>
/// SAT-D84: withdraw the group's limit record <c>LimitVersion</c> (decimal text) BEFORE it takes effect. A record already
/// in force (its date is today or earlier) never changes: a new record is entered instead. <c>ExpectedVersion</c> is the
/// group's version.
/// </summary>
public sealed record WithdrawSalesDiscountLimitRequestV1(string? ExpectedVersion, string? LimitVersion, string? Reason);

/// <summary>
/// SAT-D84: correct a not-yet-effective record: it is withdrawn and the given limit is recorded in its place, in one save
/// and one audit event. <c>EffectiveFrom</c> omitted = the corrected record's date; the same date rules as a new record.
/// </summary>
public sealed record CorrectSalesDiscountLimitRequestV1(
    string? ExpectedVersion,
    string? LimitVersion,
    string? Kind,
    string? Percent,
    Guid? LinkedGroupId,
    string? ExtraPoints,
    string? Reason,
    DateOnly? EffectiveFrom = null);

/// <summary>One company of a binding write and the binding version the editor saw there ("0" = no binding yet).</summary>
public sealed record SalesDiscountLimitBindingCompanyVersionV1(Guid CompanyId, string? ExpectedVersion);

/// <summary>
/// SAT-D63/D66: the person's limit groups, by group code, written to EVERY company the actor can act in (the SAT-D66
/// default) in one all-or-nothing transaction. <c>Companies</c> names exactly those companies with the versions the
/// editor saw. An empty <c>GroupCodes</c> removes the person from every group.
/// </summary>
public sealed record SetSalesDiscountLimitBindingRequestV1(
    IReadOnlyList<string>? GroupCodes,
    IReadOnlyList<SalesDiscountLimitBindingCompanyVersionV1>? Companies,
    string? Reason);

/// <summary>
/// The company's limit groups (passive ones included): each one's record and limit in force today and its scheduled
/// (SAT-D78) records.
/// </summary>
public sealed record SalesDiscountLimitGroupListV1(
    Guid CompanyId,
    DateOnly BusinessDate,
    IReadOnlyList<SalesDiscountLimitGroupViewV1> Groups);

public sealed record SalesDiscountLimitGroupViewV1(
    Guid Id,
    string Code,
    string Name,
    bool IsActive,
    string Version,
    SalesDiscountLimitDefinitionViewV1? Limit,
    SalesDiscountLimitEffectiveViewV1 EffectiveLimit,
    IReadOnlyList<SalesDiscountLimitScheduledViewV1> Scheduled);

/// <summary>
/// SAT-D78: a record that starts after <see cref="SalesDiscountLimitGroupListV1.BusinessDate"/>, with the limit the Sales
/// check will read for the group on its first day (a linked limit uses the linked group's record of that day).
/// </summary>
public sealed record SalesDiscountLimitScheduledViewV1(
    SalesDiscountLimitDefinitionViewV1 Limit,
    SalesDiscountLimitEffectiveViewV1 EffectiveLimit);

/// <summary>
/// A limit record of the group: in <see cref="SalesDiscountLimitGroupViewV1.Limit"/> the one in force on the list's
/// business date (null when none is), in a scheduled entry the one that starts later.
/// </summary>
public sealed record SalesDiscountLimitDefinitionViewV1(
    string LimitVersion,
    string Kind,
    string? Percent,
    Guid? LinkedGroupId,
    string? ExtraPoints,
    DateOnly EffectiveFrom,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc,
    string Reason);

/// <summary>
/// The limit the Sales check would read for the group on <see cref="SalesDiscountLimitGroupListV1.BusinessDate"/>:
/// <c>Defined</c> with a percent, or <c>None</c> (no limit: every discount goes to approval, SAT-D34/D36).
/// </summary>
public sealed record SalesDiscountLimitEffectiveViewV1(string State, string? Percent);

public static class SalesDiscountLimitEffectiveStatesV1
{
    public const string Defined = "Defined";
    public const string None = "None";
}

public sealed record SalesDiscountLimitGroupCreatedV1(Guid Id, string Version);

/// <summary>A person's bindings in every company the reader can act in.</summary>
public sealed record SalesDiscountLimitBindingViewV1(
    Guid UserId,
    IReadOnlyList<SalesDiscountLimitBindingCompanyViewV1> Companies);

/// <summary>One company: the binding version ("0" = none) and the bound groups there.</summary>
public sealed record SalesDiscountLimitBindingCompanyViewV1(
    Guid CompanyId,
    string Version,
    IReadOnlyList<SalesDiscountLimitBoundGroupV1> Groups);

public sealed record SalesDiscountLimitBoundGroupV1(Guid Id, string Code, string Name, bool IsActive);

/// <summary>People active in the server-resolved company, for the binding editor's picker.</summary>
public sealed record SalesDiscountLimitPersonListV1(IReadOnlyList<SalesDiscountLimitPersonV1> Items, bool HasMore);

public sealed record SalesDiscountLimitPersonV1(Guid UserId, string DisplayName);

/// <summary>Server-derived command metadata. Company and actor never come from a payload.</summary>
public sealed record SettingsCommandMetadataV1(
    Guid CompanyId,
    string ActorId,
    string CorrelationId,
    string? CausationId,
    string SourceChannel,
    string? ClientIpAddress,
    string? UserAgent,
    string IdempotencyKey);

/// <summary>
/// A command's terminal answer (stored for replay). <c>Code</c> is null for a success and for the code-less 404;
/// <c>CompanyId</c> / <c>GroupCode</c> name the company or code a 422 refusal is about.
/// </summary>
public sealed record SettingsCommandAnswerV1(
    int StatusCode,
    string? Code,
    string Title,
    string Detail,
    SalesDiscountLimitGroupCreatedV1? Created = null,
    Guid? CompanyId = null,
    string? GroupCode = null);

public sealed record SettingsCommandResultV1(SettingsCommandAnswerV1 Answer, bool IsReplay, string OriginalCorrelationId);

/// <summary>
/// The limit administration use cases (SAT-D61…D70). Every read and command is authorized first on
/// <see cref="SettingsPermissionManifestV1.SalesDiscountLimitsManage"/> company-wide in the server-resolved company (and, for
/// the binding, in every company it reads or writes), and every command re-measures that authority before it saves.
/// <see cref="UnauthorizedAccessException"/> means 403.
/// </summary>
public interface ISettingsSalesDiscountLimitAdministrationV1
{
    Task<SalesDiscountLimitGroupListV1> GetGroupsAsync(CancellationToken cancellationToken = default);

    Task<SettingsCommandResultV1> CreateGroupAsync(CreateSalesDiscountLimitGroupRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<SettingsCommandResultV1> RenameGroupAsync(Guid groupId, RenameSalesDiscountLimitGroupRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<SettingsCommandResultV1> DeactivateGroupAsync(Guid groupId, DeactivateSalesDiscountLimitGroupRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<SettingsCommandResultV1> SetLimitAsync(Guid groupId, SetSalesDiscountLimitRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<SettingsCommandResultV1> WithdrawLimitAsync(Guid groupId, WithdrawSalesDiscountLimitRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<SettingsCommandResultV1> CorrectLimitAsync(Guid groupId, CorrectSalesDiscountLimitRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<SalesDiscountLimitPersonListV1> SearchPeopleAsync(string? query, int? limit,
        CancellationToken cancellationToken = default);

    Task<SalesDiscountLimitBindingViewV1> GetBindingAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<SettingsCommandResultV1> SetBindingAsync(Guid userId, SetSalesDiscountLimitBindingRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}

/// <summary>
/// The port the Sales limit check's host adapter reads (API-12 <c>ISalesDiscountLimitGroupSourceV1</c>): the ACTIVE limit
/// groups the current actor is bound to in the server-resolved company (SAT-D63/D66). The company and the actor are the
/// server's. Failure is a <see cref="SettingsPrerequisiteException"/> (<c>settings_access_denied</c> /
/// <c>settings_provider_unavailable</c>), never an empty answer.
/// </summary>
public interface ISettingsSalesDiscountLimitGroupMembershipReaderV1
{
    Task<IReadOnlyList<Guid>> GetCurrentActorLimitGroupIdsAsync(CancellationToken cancellationToken = default);
}

public enum SettingsPersonDirectoryOutcomeV1
{
    Unavailable = 0,
    Denied = 1,
    Found = 2,
}

public sealed record SettingsPersonSearchAnswerV1(
    SettingsPersonDirectoryOutcomeV1 Outcome,
    IReadOnlyList<SalesDiscountLimitPersonV1> Items,
    bool HasMore);

public sealed record SettingsPersonCompaniesAnswerV1(
    SettingsPersonDirectoryOutcomeV1 Outcome,
    IReadOnlyList<Guid> ActiveCompanyIds);

/// <summary>
/// The Settings consumer port onto the Identity owner's people (composed by the host; Settings never reads Identity's
/// tables): the active people of the server-resolved company, and in which of the given companies a person is an active
/// member now. Company and actor are copies of the server context; every company must be one the actor can access.
/// </summary>
public interface ISettingsPersonDirectoryV1
{
    Task<SettingsPersonSearchAnswerV1> SearchActiveAsync(Guid companyId, string actorId, string? query, int limit,
        CancellationToken cancellationToken = default);

    Task<SettingsPersonCompaniesAnswerV1> GetActiveCompaniesAsync(Guid contextCompanyId, string actorId, Guid userId,
        IReadOnlyList<Guid> companyIds, CancellationToken cancellationToken = default);
}
