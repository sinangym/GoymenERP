namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// API-98 / B-14 (ALS-D79, BS-10; KIM-D49, KIM-D51): the Settings owner's names for maintaining the company's reference lists.
/// Both are registered, NOT granted: no role, seed or user receives them here (a human grant gate). Company-wide only
/// (measured in the server-resolved company the person works in). Not Critical: no approved source adds grant-time dual
/// control.
/// </summary>
public static class SettingsReferenceListPermissionManifestV1
{
    /// <summary>
    /// The purchasing lists: expense types and service types (KIM-D51). The decided personas are the purchasing manager
    /// ("Satınalma Müdürü") and the administrator; that is a persona note only.
    /// </summary>
    public const string PurchasingListsManage = "settings.reference-lists.manage";

    /// <summary>
    /// The production lists: paint colors and scrap kinds (B-14). Who edits them is not decided (NEEDS-SINAN); the name is
    /// registered so the list can be granted once decided, and until then nobody holds it.
    /// </summary>
    public const string ProductionListsManage = "settings.production-reference-lists.manage";
}

/// <summary>
/// The reference lists the Settings owner knows. A kind that is known but not enabled (<see cref="OperationType"/>) has no
/// route, no write and no stored row: the reader answers it as not enabled (empty list) and every lookup as
/// <see cref="SettingsReferenceListLookupOutcomeV1.Unavailable"/>.
/// </summary>
public enum SettingsReferenceListKindV1
{
    /// <summary>Masraf tipi (KIM-D49: opens now, starts empty, no seed).</summary>
    ExpenseType = 1,

    /// <summary>Hizmet tipi (KIM-D49/KIM-D51: opens now, starts empty, no seed; "Satış fasonu" is not a service type).</summary>
    ServiceType = 2,

    /// <summary>İşlem türü (KIM-D49: NOT opened; known only so consumers can name it and be refused uniformly).</summary>
    OperationType = 3,

    /// <summary>Boya rengi (B-14).</summary>
    PaintColor = 4,

    /// <summary>Hurda türü (B-14).</summary>
    ScrapKind = 5,
}

/// <summary>
/// The stored code and route slug of every enabled kind, and the permission that maintains it. Only these kinds are
/// enabled; the stored code is the database check's closed list.
/// </summary>
public static class SettingsReferenceListKindsV1
{
    public const string ExpenseTypeCode = "ExpenseType";
    public const string ServiceTypeCode = "ServiceType";
    public const string PaintColorCode = "PaintColor";
    public const string ScrapKindCode = "ScrapKind";

    public const string ExpenseTypeSlug = "expense-types";
    public const string ServiceTypeSlug = "service-types";
    public const string PaintColorSlug = "paint-colors";
    public const string ScrapKindSlug = "scrap-kinds";

    /// <summary>The enabled kinds in a stable order.</summary>
    public static IReadOnlyList<SettingsReferenceListKindV1> Enabled { get; } =
    [
        SettingsReferenceListKindV1.ExpenseType,
        SettingsReferenceListKindV1.ServiceType,
        SettingsReferenceListKindV1.PaintColor,
        SettingsReferenceListKindV1.ScrapKind,
    ];

    public static bool IsEnabled(SettingsReferenceListKindV1 kind) => Enabled.Contains(kind);

    /// <summary>The stored code of an enabled kind; null for a kind that is not enabled or unknown.</summary>
    public static string? CodeOf(SettingsReferenceListKindV1 kind) => kind switch
    {
        SettingsReferenceListKindV1.ExpenseType => ExpenseTypeCode,
        SettingsReferenceListKindV1.ServiceType => ServiceTypeCode,
        SettingsReferenceListKindV1.PaintColor => PaintColorCode,
        SettingsReferenceListKindV1.ScrapKind => ScrapKindCode,
        _ => null,
    };

    /// <summary>The route slug of an enabled kind; null for a kind that is not enabled or unknown.</summary>
    public static string? SlugOf(SettingsReferenceListKindV1 kind) => kind switch
    {
        SettingsReferenceListKindV1.ExpenseType => ExpenseTypeSlug,
        SettingsReferenceListKindV1.ServiceType => ServiceTypeSlug,
        SettingsReferenceListKindV1.PaintColor => PaintColorSlug,
        SettingsReferenceListKindV1.ScrapKind => ScrapKindSlug,
        _ => null,
    };

    /// <summary>The permission that reads and maintains an enabled kind; null for a kind that is not enabled or unknown.</summary>
    public static string? PermissionOf(SettingsReferenceListKindV1 kind) => kind switch
    {
        SettingsReferenceListKindV1.ExpenseType or SettingsReferenceListKindV1.ServiceType =>
            SettingsReferenceListPermissionManifestV1.PurchasingListsManage,
        SettingsReferenceListKindV1.PaintColor or SettingsReferenceListKindV1.ScrapKind =>
            SettingsReferenceListPermissionManifestV1.ProductionListsManage,
        _ => null,
    };

    /// <summary>The enabled kind of a stored code (exact spelling), or null.</summary>
    public static SettingsReferenceListKindV1? FromCode(string? code) => code switch
    {
        ExpenseTypeCode => SettingsReferenceListKindV1.ExpenseType,
        ServiceTypeCode => SettingsReferenceListKindV1.ServiceType,
        PaintColorCode => SettingsReferenceListKindV1.PaintColor,
        ScrapKindCode => SettingsReferenceListKindV1.ScrapKind,
        _ => null,
    };
}

/// <summary>Technical bounds of the inputs (storage and transport shape, not policy).</summary>
public static class SettingsReferenceListInputLimitsV1
{
    /// <summary>SAT-D68 precedent: the mandatory reason of every change.</summary>
    public const int ReasonMaximumLength = 4000;

    /// <summary>The code column width (varchar, binary collation).</summary>
    public const int CodeMaximumLength = 32;

    /// <summary>The display name column width (nvarchar).</summary>
    public const int NameMaximumLength = 200;
}

/// <summary>Published problem codes of the reference list routes.</summary>
public static class SettingsReferenceListProblemCodesV1
{
    public const string RequestInvalid = "settings_reference_list_request_invalid";
    public const string CommandMetadataInvalid = "settings_reference_list_command_metadata_invalid";
    public const string ReasonRequired = "settings_reference_list_reason_required";
    public const string ReasonTooLong = "settings_reference_list_reason_too_long";
    public const string CodeConflict = "settings_reference_list_code_conflict";
    public const string EntryNotFound = "settings_reference_list_entry_not_found";
    public const string IdempotencyConflict = "settings_reference_list_idempotency_conflict";
    public const string VersionConflict = "settings_reference_list_version_conflict";
    public const string ProviderUnavailable = "settings_provider_unavailable";
    public const string ProfileInvalid = "settings_profile_invalid";
    public const string AccessDenied = "settings_access_denied";
}

/// <summary>
/// A new entry of a list (the kind is the route's). <c>Code</c>: mandatory, 1…32 characters of A–Z, 0–9, '.', '_', '-'
/// starting with a letter or digit, exactly as written (no case folding), unique within the company's list and immutable
/// once created. <c>Name</c>: mandatory display name, trimmed, 1…200 characters, no control characters. <c>Reason</c>: the
/// mandatory reason (≤ 4000). The entry is created active at version 1.
/// </summary>
public sealed record CreateSettingsReferenceListEntryRequestV1(string? Code, string? Name, string? Reason);

/// <summary>
/// A new version of an entry: rename, deactivate or reactivate (the code never changes). <c>ExpectedVersion</c> is the entry's
/// latest stored version as decimal text; <c>Active</c> is mandatory. An identical name and state is a no-op (204, no audit).
/// </summary>
public sealed record RecordSettingsReferenceListEntryRequestV1(
    string? ExpectedVersion,
    string? Name,
    bool? Active,
    string? Reason);

/// <summary>One entry as the administration shows it (its latest version). <c>Version</c> is decimal text.</summary>
public sealed record SettingsReferenceListEntryViewV1(
    Guid Id,
    string Code,
    string Name,
    bool Active,
    string Version,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc);

/// <summary>Every entry of one list of the company (active and inactive), ordered by code (ordinal).</summary>
public sealed record SettingsReferenceListViewV1(string Kind, IReadOnlyList<SettingsReferenceListEntryViewV1> Entries);

/// <summary>A command's terminal answer (stored for replay). <c>Code</c> is null for a success.</summary>
public sealed record SettingsReferenceListCommandAnswerV1(int StatusCode, string? Code, string Title, string Detail);

public sealed record SettingsReferenceListCommandResultV1(
    SettingsReferenceListCommandAnswerV1 Answer,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>
/// The reference list administration (API-98/B-14). Every read and command is authorized first on the kind's permission
/// (<see cref="SettingsReferenceListKindsV1.PermissionOf"/>) company-wide in the server-resolved company and re-measured
/// before every save; a kind that is not enabled is refused as <see cref="ArgumentOutOfRangeException"/> (the host maps no
/// route to it). <see cref="UnauthorizedAccessException"/> means 403. One person decides (SAT-D68 precedent): a mandatory
/// reason, Idempotency-Key, expected version for a new version and one audit event per real change (none for a no-op).
/// </summary>
public interface ISettingsReferenceListAdministrationV1
{
    Task<SettingsReferenceListViewV1> GetAsync(SettingsReferenceListKindV1 kind, CancellationToken cancellationToken = default);

    Task<SettingsReferenceListCommandResultV1> CreateAsync(SettingsReferenceListKindV1 kind,
        CreateSettingsReferenceListEntryRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default);

    Task<SettingsReferenceListCommandResultV1> RecordAsync(SettingsReferenceListKindV1 kind, Guid entryId,
        RecordSettingsReferenceListEntryRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default);
}

/// <summary>One entry as a consuming module sees it (its latest version).</summary>
public sealed record SettingsReferenceListEntryV1(Guid Id, string Code, string Name, bool Active, long Version);

/// <summary>
/// One list of the company. <c>Enabled</c> is false for a kind that is not opened (its <c>Entries</c> are empty); an enabled
/// list may be empty (nothing is seeded) — a consumer that requires an active entry (e.g. ALS-D107) answers that itself.
/// </summary>
public sealed record SettingsReferenceListV1(
    Guid CompanyId,
    SettingsReferenceListKindV1 Kind,
    bool Enabled,
    IReadOnlyList<SettingsReferenceListEntryV1> Entries);

/// <summary>The answer of a single entry lookup.</summary>
public enum SettingsReferenceListLookupOutcomeV1
{
    /// <summary>The entry exists in the company's list and is active.</summary>
    Found = 1,

    /// <summary>No such entry in the company's list (unknown, of another list or of another company).</summary>
    NotFound = 2,

    /// <summary>The entry exists but its latest version is inactive; it must not be chosen for new records.</summary>
    Inactive = 3,

    /// <summary>
    /// The owner could not answer (unreadable store, malformed data or a kind that is not enabled); the consumer fails closed.
    /// </summary>
    Unavailable = 4,
}

/// <summary>A lookup answer; <c>Entry</c> is set for <c>Found</c> and <c>Inactive</c> only.</summary>
public sealed record SettingsReferenceListLookupV1(SettingsReferenceListLookupOutcomeV1 Outcome, SettingsReferenceListEntryV1? Entry);

/// <summary>
/// The read port the purchasing, stock/logistics, production and finance modules ask for a reference list. The caller passes
/// the company of its record; it must equal the server-resolved company, otherwise the answer is <c>settings_access_denied</c>.
/// It applies no Settings permission: the calling module authorizes its own read.
/// <list type="bullet">
/// <item><see cref="ListAsync"/>: every entry (active and inactive) ordered by code. Errors are
/// <see cref="SettingsPrerequisiteException"/> codes, in this order: cancelled → <see cref="OperationCanceledException"/>;
/// no authenticated user, no access to the company or another company → <c>settings_access_denied</c>; an unreadable store →
/// <c>settings_provider_unavailable</c>; malformed stored data → <c>settings_profile_invalid</c>.</item>
/// <item><see cref="GetAsync"/>: Found / NotFound / Inactive, or Unavailable for an unreadable store, malformed data or a kind
/// that is not enabled. Access is still refused with <c>settings_access_denied</c>.</item>
/// </list>
/// </summary>
public interface ISettingsReferenceListReaderV1
{
    Task<SettingsReferenceListV1> ListAsync(Guid companyId, SettingsReferenceListKindV1 kind,
        CancellationToken cancellationToken = default);

    Task<SettingsReferenceListLookupV1> GetAsync(Guid companyId, SettingsReferenceListKindV1 kind, Guid entryId,
        CancellationToken cancellationToken = default);
}
