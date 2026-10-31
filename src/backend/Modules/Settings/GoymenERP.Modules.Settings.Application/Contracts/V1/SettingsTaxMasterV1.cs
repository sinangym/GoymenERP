namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// MOD-03 tax master (SAT-D73/D74/D82/D83, <c>docs/decisions/SINAN_KARARLARI_2026-10-28_SATIS_EK12.md</c>; SAT-D93/D110,
/// <c>…_EK13.md</c>): the Settings owner's permission name for the VAT classes, the buyer categories, the withholding (tevkifat)
/// classes, their effective-dated rate records and the UBL-TR exemption / withholding code lists.
/// </summary>
public static class SettingsTaxMasterPermissionManifestV1
{
    /// <summary>
    /// SAT-D83: ONE name for reading and editing the tax master. The Finance (Muhasebe) and Admin personas are the intended
    /// holders; the name is registered, NOT granted — no role, seed or user receives it here (a human grant gate).
    /// Company-wide only. Not Critical: no approved source adds grant-time dual control.
    /// </summary>
    public const string TaxMasterManage = "settings.tax-master.manage";
}

/// <summary>
/// The three category kinds of the tax master. Categories are company-scoped records an authorized person enters; none is
/// seeded (EK12 "kodla tohumlanmaz").
/// </summary>
public static class TaxCategoryKindsV1
{
    /// <summary>SAT-D82: a VAT class the product card refers to (e.g. "genel", "indirimli"); its rate is a dated record.</summary>
    public const string VatClass = "VatClass";

    /// <summary>SAT-D74: a withholding class the product card refers to (e.g. demir-çelik, hurda, fason hizmet).</summary>
    public const string WithholdingClass = "WithholdingClass";

    /// <summary>
    /// SAT-D74/D93: a buyer category (e.g. KDV mükellefi, belirlenmiş alıcı, nihai tüketici). The buyer's category is read
    /// from the Cari card by the consumer (Counterparty owner, follow-up) and passed in.
    /// </summary>
    public const string BuyerCategory = "BuyerCategory";

    public static bool IsKnown(string? kind) => kind is VatClass or WithholdingClass or BuyerCategory;
}

/// <summary>The two UBL-TR code lists (EK13 implementation notes: effective-dated Settings code lists, never hard-coded).</summary>
public static class TaxCodeListKindsV1
{
    /// <summary>KDV istisna (exemption) reason codes.</summary>
    public const string Exemption = "Exemption";

    /// <summary>KDV tevkifat (withholding) codes.</summary>
    public const string Withholding = "Withholding";

    public static bool IsKnown(string? kind) => kind is Exemption or Withholding;
}

/// <summary>Technical bounds of the inputs (storage and transport shape, not tax policy).</summary>
public static class TaxMasterInputLimitsV1
{
    public const int CategoryCodeMaximumLength = 32;
    public const int CategoryNameMaximumLength = 100;
    public const int ListCodeMaximumLength = 16;
    public const int ListEntryNameMaximumLength = 200;
    public const int ListMaximumEntries = 500;

    /// <summary>SAT-D68 precedent: the mandatory reason of every change.</summary>
    public const int ReasonMaximumLength = 4000;

    /// <summary>Decimal places a rate may carry; a longer value is refused, never rounded.</summary>
    public const int PercentMaximumScale = 4;
}

/// <summary>Published problem codes of the tax master routes and ports.</summary>
public static class TaxMasterProblemCodesV1
{
    public const string RequestInvalid = "settings_tax_master_request_invalid";
    public const string CommandMetadataInvalid = "settings_tax_master_command_metadata_invalid";
    public const string ReasonRequired = "settings_tax_master_reason_required";
    public const string ReasonTooLong = "settings_tax_master_reason_too_long";
    public const string IdempotencyConflict = "settings_tax_master_idempotency_conflict";
    public const string VersionConflict = "settings_tax_master_version_conflict";
    public const string CategoryCodeTaken = "settings_tax_master_category_code_taken";
    public const string CategoryNameTaken = "settings_tax_master_category_name_taken";
    public const string CategoryKindMismatch = "settings_tax_master_category_kind_mismatch";
    public const string BuyerCategoryNotFound = "settings_tax_master_buyer_category_not_found";
    public const string WithholdingCodeUnknown = "settings_tax_master_withholding_code_unknown";
    public const string EffectiveFromPast = "settings_tax_master_effective_from_past";
    public const string RecordInEffect = "settings_tax_master_record_in_effect";
    public const string RecordWithdrawn = "settings_tax_master_record_withdrawn";
    public const string ProviderUnavailable = "settings_provider_unavailable";
    public const string AccessDenied = "settings_access_denied";
}

/// <summary>Create a category of <c>Kind</c> in the server-resolved company. <c>Code</c> is stable; it is never renamed.</summary>
public sealed record CreateTaxCategoryRequestV1(string? Kind, string? Code, string? Name, string? Reason);

/// <summary>Rename a category; <c>ExpectedVersion</c> is the category's version as decimal text.</summary>
public sealed record RenameTaxCategoryRequestV1(string? ExpectedVersion, string? Name, string? Reason);

/// <summary>
/// SAT-D82: record the VAT rate of a VAT class as a new immutable record. <c>RatePercent</c> is lossless decimal text,
/// 0…100 with at most 4 decimals. <c>EffectiveFrom</c> omitted = today's Europe/Istanbul business date; a later date
/// applies by itself on that date; a date before today is refused. A record on the same date as a not-yet-effective one
/// replaces it (that record is withdrawn in the same save and the change is audited as a correction, SAT-D84 precedent).
/// <c>ExpectedVersion</c> is the class's version.
/// </summary>
public sealed record RecordVatRateRequestV1(string? ExpectedVersion, string? RatePercent, string? Reason,
    DateOnly? EffectiveFrom = null);

/// <summary>
/// SAT-D74: record the withholding of a withholding class for one buyer category as a new immutable record.
/// <c>Applies</c> true: <c>RatePercent</c> (the withheld share of the VAT in percent, &gt; 0 and ≤ 100, e.g. 50 for 5/10)
/// and a <c>WithholdingCode</c> from the withholding code list in force on the record's date are required. <c>Applies</c>
/// false: an explicit "no withholding for this buyer category" (no rate, no code). The same date rules as a VAT rate.
/// <c>ExpectedVersion</c> is the withholding class's version.
/// </summary>
public sealed record RecordWithholdingRateRequestV1(
    string? ExpectedVersion,
    Guid? BuyerCategoryId,
    bool? Applies,
    string? RatePercent,
    string? WithholdingCode,
    string? Reason,
    DateOnly? EffectiveFrom = null);

/// <summary>
/// SAT-D84 precedent: withdraw a rate record BEFORE it takes effect. A record in force never changes; a new record is
/// entered instead. <c>ExpectedVersion</c> is the owning class's version.
/// </summary>
public sealed record WithdrawTaxRateRecordRequestV1(string? ExpectedVersion, string? Reason);

/// <summary>One entry of a code list version.</summary>
public sealed record TaxCodeListEntryV1(string? Code, string? Name);

/// <summary>
/// Publish a whole code list version (it replaces the previous one from <c>EffectiveFrom</c> on; the same date rules as a
/// rate record). <c>ExpectedVersion</c> is the list's version ("0" while the company has none).
/// </summary>
public sealed record PublishTaxCodeListRequestV1(
    string? ExpectedVersion,
    IReadOnlyList<TaxCodeListEntryV1>? Entries,
    string? Reason,
    DateOnly? EffectiveFrom = null);

/// <summary>Withdraw a code list version before it takes effect.</summary>
public sealed record WithdrawTaxCodeListRequestV1(string? ExpectedVersion, string? Reason);

/// <summary>The tax master of the server-resolved company on <c>BusinessDate</c> (today, Europe/Istanbul).</summary>
public sealed record TaxMasterViewV1(
    Guid CompanyId,
    DateOnly BusinessDate,
    IReadOnlyList<TaxCategoryViewV1> Categories,
    IReadOnlyList<TaxRateSeriesViewV1> RateSeries,
    IReadOnlyList<TaxCodeListViewV1> CodeLists);

public sealed record TaxCategoryViewV1(Guid Id, string Kind, string Code, string Name, string Version);

/// <summary>
/// One rate series: a VAT class (<c>Kind</c> <c>Vat</c>, no buyer category) or a withholding class + buyer category
/// (<c>Kind</c> <c>Withholding</c>). <c>InForce</c> is null when nothing is in force today ("kaynak yok").
/// </summary>
public sealed record TaxRateSeriesViewV1(
    string Kind,
    Guid ClassId,
    Guid? BuyerCategoryId,
    TaxRateRecordViewV1? InForce,
    IReadOnlyList<TaxRateRecordViewV1> Scheduled);

public static class TaxRateKindsV1
{
    public const string Vat = "Vat";
    public const string Withholding = "Withholding";
}

public sealed record TaxRateRecordViewV1(
    Guid RecordId,
    string RecordVersion,
    DateOnly EffectiveFrom,
    bool Applies,
    string? RatePercent,
    string? WithholdingCode,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc,
    string Reason);

/// <summary>A code list: its own version (the edit fence), the version in force today and the scheduled ones.</summary>
public sealed record TaxCodeListViewV1(
    string Kind,
    string Version,
    TaxCodeListVersionViewV1? InForce,
    IReadOnlyList<TaxCodeListVersionViewV1> Scheduled);

public sealed record TaxCodeListVersionViewV1(
    string ListVersion,
    DateOnly EffectiveFrom,
    IReadOnlyList<TaxCodeListEntryViewV1> Entries,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc,
    string Reason);

public sealed record TaxCodeListEntryViewV1(string Code, string Name);

public sealed record TaxCategoryCreatedV1(Guid Id, string Version);

/// <summary>A tax master command's terminal answer (stored for replay). <c>Code</c> is null for a success and the 404.</summary>
public sealed record TaxMasterCommandAnswerV1(
    int StatusCode,
    string? Code,
    string Title,
    string Detail,
    TaxCategoryCreatedV1? Created = null);

public sealed record TaxMasterCommandResultV1(TaxMasterCommandAnswerV1 Answer, bool IsReplay, string OriginalCorrelationId);

/// <summary>
/// The tax master administration (SAT-D82/D83). Every read and command is authorized first on
/// <see cref="SettingsTaxMasterPermissionManifestV1.TaxMasterManage"/> company-wide in the server-resolved company and
/// re-measured before every save. <see cref="UnauthorizedAccessException"/> means 403. One person decides (SAT-D68
/// precedent): a mandatory reason, Idempotency-Key, expected version and one audit event per real change.
/// </summary>
public interface ISettingsTaxMasterAdministrationV1
{
    Task<TaxMasterViewV1> GetAsync(CancellationToken cancellationToken = default);

    Task<TaxMasterCommandResultV1> CreateCategoryAsync(CreateTaxCategoryRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default);

    Task<TaxMasterCommandResultV1> RenameCategoryAsync(Guid categoryId, RenameTaxCategoryRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<TaxMasterCommandResultV1> RecordVatRateAsync(Guid vatClassId, RecordVatRateRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<TaxMasterCommandResultV1> RecordWithholdingRateAsync(Guid withholdingClassId, RecordWithholdingRateRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<TaxMasterCommandResultV1> WithdrawRateRecordAsync(Guid recordId, WithdrawTaxRateRecordRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<TaxMasterCommandResultV1> PublishCodeListAsync(string listKind, PublishTaxCodeListRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<TaxMasterCommandResultV1> WithdrawCodeListAsync(string listKind, long listVersion, WithdrawTaxCodeListRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}

/// <summary>A category the Product card may refer to (the owner's answer to <see cref="ISettingsTaxCategoryReaderV1"/>).</summary>
public sealed record SettingsTaxCategoryV1(Guid CompanyId, Guid Id, string Kind, string Code, string Name);

/// <summary>
/// The reference read the Product owner uses to check a product's VAT / withholding class before it stores it, and the
/// screens use to label them. Company and actor are the server's; an actor who cannot access the company gets
/// <c>settings_access_denied</c>, an unreadable store <c>settings_provider_unavailable</c> (both a
/// <see cref="SettingsPrerequisiteException"/>), never an empty answer. A category of another company is "not found" (null).
/// </summary>
public interface ISettingsTaxCategoryReaderV1
{
    Task<SettingsTaxCategoryV1?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SettingsTaxCategoryV1>> ListAsync(string kind, CancellationToken cancellationToken = default);
}

/// <summary>Whether a tax value was found. <c>Unresolved</c> is "kaynak yok": never 0, never a default.</summary>
public static class TaxResolutionStatesV1
{
    /// <summary>The VAT rate (or the withholding) is in force on the date.</summary>
    public const string Resolved = "Resolved";

    /// <summary>The product carries no withholding class: withholding does not apply to it (SAT-D74).</summary>
    public const string NotApplicable = "NotApplicable";

    /// <summary>An explicit record says the buyer category bears no withholding for this class on the date.</summary>
    public const string NotApplied = "NotApplied";

    /// <summary>No source answers (see the reason): the consumer shows "kaynak yok" and computes no tax (fail-closed).</summary>
    public const string Unresolved = "Unresolved";
}

/// <summary>Why a value is <see cref="TaxResolutionStatesV1.Unresolved"/>.</summary>
public static class TaxResolutionReasonsV1
{
    public const string ProductNotClassified = "product_tax_class_missing";
    public const string ProductNotFound = "product_not_found";
    public const string VatClassUnknown = "vat_class_unknown";
    public const string VatRateMissing = "vat_rate_missing";
    public const string WithholdingClassUnknown = "withholding_class_unknown";
    public const string BuyerCategoryMissing = "buyer_category_missing";
    public const string BuyerCategoryUnknown = "buyer_category_unknown";
    public const string WithholdingRateMissing = "withholding_rate_missing";
    public const string WithholdingCodeNotInList = "withholding_code_not_in_list";
    public const string CodeListMissing = "code_list_missing";
}

/// <summary>
/// The tax of one product for one buyer category on one business date, in the server-resolved company. The VAT part:
/// <c>VatState</c> <c>Resolved</c> with <c>VatPercent</c> (0…100) and the record id/version that answered, or
/// <c>Unresolved</c> with <c>VatReason</c>. The withholding part: <c>Resolved</c> with <c>WithholdingPercent</c> (the
/// withheld share of the line VAT in percent) and its UBL-TR <c>WithholdingCode</c>, <c>NotApplicable</c>, <c>NotApplied</c>
/// or <c>Unresolved</c> with <c>WithholdingReason</c>. The consumer keeps the record ids/versions as its applied snapshot
/// and computes the amounts with its ONE backend calculator (Sales: <c>SalesOrderTotalsCalculator</c> — withholding from the
/// rounded line VAT, SAT-D75; the unit price not rounded, SAT-D76).
/// </summary>
public sealed record SettingsLineTaxResolutionV1(
    Guid CompanyId,
    Guid ProductId,
    Guid? BuyerCategoryId,
    DateOnly On,
    Guid? VatClassId,
    string VatState,
    decimal? VatPercent,
    Guid? VatRecordId,
    long? VatRecordVersion,
    string? VatReason,
    Guid? WithholdingClassId,
    string WithholdingState,
    decimal? WithholdingPercent,
    string? WithholdingCode,
    Guid? WithholdingRecordId,
    long? WithholdingRecordVersion,
    string? WithholdingReason);

/// <summary>A code list in force on a date: <c>Resolved</c> with its entries, or <c>Unresolved</c> (<c>code_list_missing</c>).</summary>
public sealed record SettingsTaxCodeListResolutionV1(
    Guid CompanyId,
    string Kind,
    DateOnly On,
    string State,
    long? ListVersion,
    DateOnly? EffectiveFrom,
    IReadOnlyList<TaxCodeListEntryViewV1> Entries,
    string? Reason);

/// <summary>
/// The read port the Sales order line and invoice consume (SAT-D73/D74/D93). Company and actor are the server's: an actor
/// who cannot access the company is <c>settings_access_denied</c>, an unreadable source <c>settings_provider_unavailable</c>
/// (a <see cref="SettingsPrerequisiteException"/>). Every missing value is an explicit <c>Unresolved</c> with a reason —
/// never 0, never a default rate (EK13: "teyit edilene kadar Ayarlar değeri yoksa ilgili alan 'kaynak yok' kalır"). The
/// answer is reference data, not an authorization: the consumer checks its own permission. Exemptions are chosen on the
/// order line from the exemption code list (SAT-D93), so the port answers the list rather than picking one.
/// </summary>
public interface ISettingsLineTaxResolverV1
{
    Task<SettingsLineTaxResolutionV1> ResolveAsync(Guid productId, Guid? buyerCategoryId, DateOnly on,
        CancellationToken cancellationToken = default);

    Task<SettingsTaxCodeListResolutionV1> GetCodeListAsync(string kind, DateOnly on,
        CancellationToken cancellationToken = default);
}

public enum SettingsProductTaxClassOutcomeV1
{
    Unavailable = 0,
    Denied = 1,
    NotFound = 2,
    NotClassified = 3,
    Found = 4,
}

/// <summary>The Product owner's classification of a product in the company (VAT class required; withholding class optional).</summary>
public sealed record SettingsProductTaxClassAnswerV1(
    SettingsProductTaxClassOutcomeV1 Outcome,
    Guid? VatClassId,
    Guid? WithholdingClassId);

/// <summary>
/// The Settings consumer port onto the Product owner's tax classification (composed by the host; Settings never reads
/// Product's tables). Company and actor are copies of the server context.
/// </summary>
public interface ISettingsProductTaxClassSourceV1
{
    Task<SettingsProductTaxClassAnswerV1> GetAsync(Guid companyId, Guid productId, CancellationToken cancellationToken = default);
}
