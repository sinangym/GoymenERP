namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// MOD-03 tax master (EK12 SAT-D73/D74/D82/D83; EK13 SAT-D93; EK19 SAT-D133…D139,
/// <c>docs/decisions/SINAN_KARARLARI_2026-10-31_SATIS_EK19.md</c>): the Settings owner's permission name for the per-item-type
/// default tax, the UBL-TR exemption / withholding code lists and the VUK 232 withholding amount threshold.
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
/// SAT-D134/D139: the item types a product card carries (demir / hurda / hizmet). The tax defaults are keyed by them; the
/// Product owner's field uses the same wire values (<c>ProductItemTypesV1</c>, pinned equal by a host test).
/// </summary>
public static class TaxItemTypesV1
{
    /// <summary>Demir (steel goods).</summary>
    public const string Steel = "Steel";

    /// <summary>Hurda (scrap).</summary>
    public const string Scrap = "Scrap";

    /// <summary>Hizmet (service).</summary>
    public const string Service = "Service";

    public static IReadOnlyList<string> All { get; } = [Steel, Scrap, Service];

    public static bool IsKnown(string? itemType) => itemType is Steel or Scrap or Service;
}

/// <summary>
/// SAT-D140 (EK19) / SAT-D158 (EK21): the buyer's tax situation, chosen PER ORDER by Sales (order header; never read from the Cari
/// card) and passed to the line tax port as an input. The situations are NOT a fixed enum: they are an effective-dated Settings
/// list per company (<see cref="TaxBuyerSituationEntryViewV1"/>, SAT-D133 like the other tax tables), each with its tax effect as
/// data (SAT-D74/D93/D147). These constants are the codes of the reviewed version-0 catalog
/// (<c>ApprovedTaxBuyerSituationCatalog</c>); Müdür/Admin may add situations in Settings (SAT-D158). A code the list in force
/// does not carry (unknown or withdrawn) resolves <c>Unresolved</c> (<c>buyer_situation_unknown</c>), never a guess.
/// </summary>
public static class TaxBuyerSituationsV1
{
    /// <summary>Normal alıcı — the item-type default as it is (a new order starts here).</summary>
    public const string Normal = "Normal";

    /// <summary>Tevkifata tabi olmayan alıcı — no withholding (SAT-D74); VAT unchanged.</summary>
    public const string NotSubjectToWithholding = "NotSubjectToWithholding";

    /// <summary>Nihai tüketici — no withholding (SAT-D74); VAT unchanged.</summary>
    public const string FinalConsumer = "FinalConsumer";

    /// <summary>İhraç kayıtlı — KDV %0 with an exemption code from the list, no withholding, document required (SAT-D147).</summary>
    public const string ExportRegistered = "ExportRegistered";

    /// <summary>İstisna belgeli — KDV %0 with an exemption code from the list, no withholding, document required (SAT-D147).</summary>
    public const string ExemptionCertificate = "ExemptionCertificate";

    /// <summary>The codes of the reviewed version-0 catalog.</summary>
    public static IReadOnlyList<string> Catalog { get; } =
        [Normal, NotSubjectToWithholding, FinalConsumer, ExportRegistered, ExemptionCertificate];
}

/// <summary>SAT-D147/D158: how a buyer situation changes the VAT of the item-type default (data on the situation entry).</summary>
public static class TaxBuyerSituationVatModesV1
{
    /// <summary>The item-type default's VAT as it is.</summary>
    public const string Normal = "Normal";

    /// <summary>
    /// KDV %0 under an exemption code chosen from the company's exemption code list (SAT-D147); no withholding applies. No code
    /// chosen, no list, or a code the list does not carry: the code part is <c>Unresolved</c> and the invoice waits.
    /// </summary>
    public const string ZeroWithExemptionCode = "ZeroWithExemptionCode";

    public static bool IsKnown(string? mode) => mode is Normal or ZeroWithExemptionCode;
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

/// <summary>Where a default record comes from.</summary>
public static class TaxDefaultSourcesV1
{
    /// <summary>The reviewed default catalog version shipped as source (SAT-D139: Sinan's explicit values).</summary>
    public const string Catalog = "Catalog";

    /// <summary>A record an authorized person entered in Settings.</summary>
    public const string Record = "Record";
}

/// <summary>Technical bounds of the inputs (storage and transport shape, not tax policy).</summary>
public static class TaxMasterInputLimitsV1
{
    public const int ListCodeMaximumLength = 16;
    public const int ListEntryNameMaximumLength = 200;
    public const int ListMaximumEntries = 500;

    /// <summary>A buyer situation code: ASCII letters and digits, starting with a letter (e.g. "ExportRegistered").</summary>
    public const int BuyerSituationCodeMaximumLength = 64;

    /// <summary>Entries one buyer situation list version may carry.</summary>
    public const int BuyerSituationMaximumEntries = 100;

    /// <summary>SAT-D68 precedent: the mandatory reason of every change.</summary>
    public const int ReasonMaximumLength = 4000;

    /// <summary>Decimal places a rate or amount may carry; a longer value is refused, never rounded.</summary>
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
    public const string ExemptionCodeUnknown = "settings_tax_master_exemption_code_unknown";
    public const string WithholdingCodeUnknown = "settings_tax_master_withholding_code_unknown";
    public const string EffectiveFromPast = "settings_tax_master_effective_from_past";
    public const string RecordInEffect = "settings_tax_master_record_in_effect";
    public const string RecordWithdrawn = "settings_tax_master_record_withdrawn";
    public const string ProviderUnavailable = "settings_provider_unavailable";
    public const string AccessDenied = "settings_access_denied";
}

/// <summary>
/// SAT-D139: record the default tax of one item type as a new immutable record (it replaces the catalog or an earlier record
/// from <c>EffectiveFrom</c> on; omitted = today, Europe/Istanbul; a date before today is refused; a record on the same date as
/// a not-yet-effective one replaces it). VAT: <c>VatApplies</c> true with <c>VatPercent</c> (0…100, lossless text) or false
/// ("KDV yok") with an optional <c>ExemptionCode</c> from the exemption list in force on the date. Withholding (only when VAT
/// applies): <c>WithholdingApplies</c> true with <c>WithholdingPercent</c> (the withheld share of the VAT in percent, &gt; 0 and
/// ≤ 100, e.g. 50 for 5/10) and an optional <c>WithholdingCode</c> from the withholding list, or false. A code left empty
/// stays "kaynak yok" on the resolution (the invoice waits). <c>ExpectedVersion</c> is the item type's version ("0" while it
/// has no record).
/// </summary>
public sealed record RecordItemTypeDefaultRequestV1(
    string? ExpectedVersion,
    bool? VatApplies,
    string? VatPercent,
    string? ExemptionCode,
    bool? WithholdingApplies,
    string? WithholdingPercent,
    string? WithholdingCode,
    string? Reason,
    DateOnly? EffectiveFrom = null);

/// <summary>
/// SAT-D84 precedent: withdraw a record BEFORE it takes effect. A record in force never changes; a new record is entered
/// instead. <c>ExpectedVersion</c> is the owning series' version.
/// </summary>
public sealed record WithdrawTaxRecordRequestV1(string? ExpectedVersion, string? Reason);

/// <summary>One entry of a code list version.</summary>
public sealed record TaxCodeListEntryV1(string? Code, string? Name);

/// <summary>
/// Publish a whole code list version (it replaces the previous one from <c>EffectiveFrom</c> on; the same date rules as a
/// default record). <c>ExpectedVersion</c> is the list's version ("0" while the company has none).
/// </summary>
public sealed record PublishTaxCodeListRequestV1(
    string? ExpectedVersion,
    IReadOnlyList<TaxCodeListEntryV1>? Entries,
    string? Reason,
    DateOnly? EffectiveFrom = null);

/// <summary>Withdraw a code list version before it takes effect.</summary>
public sealed record WithdrawTaxCodeListRequestV1(string? ExpectedVersion, string? Reason);

/// <summary>
/// SAT-D158: one buyer situation with its tax effect as data. <c>Code</c>: ASCII letters/digits starting with a letter
/// (≤ 64); <c>Name</c>: the Turkish name (1…200); <c>WithholdingApplies</c>: false removes the default's withholding (SAT-D74);
/// <c>VatMode</c>: one of <see cref="TaxBuyerSituationVatModesV1"/> (<c>ZeroWithExemptionCode</c> requires
/// <c>WithholdingApplies</c> false, SAT-D147); <c>DocumentRequired</c>: the order must carry the buyer's document (no, date)
/// before the invoice (SAT-D147, an order-side check the resolver only flags).
/// </summary>
public sealed record TaxBuyerSituationEntryV1(
    string? Code,
    string? Name,
    bool? WithholdingApplies,
    string? VatMode,
    bool? DocumentRequired);

/// <summary>
/// SAT-D158: publish a whole buyer situation list version (it replaces the catalog or the previous version from
/// <c>EffectiveFrom</c> on; the same date rules as a code list). A situation left out is no longer in force from that date (an
/// order naming it resolves <c>Unresolved</c>). <c>ExpectedVersion</c> is the list's version ("0" while only the catalog answers).
/// </summary>
public sealed record PublishTaxBuyerSituationListRequestV1(
    string? ExpectedVersion,
    IReadOnlyList<TaxBuyerSituationEntryV1>? Entries,
    string? Reason,
    DateOnly? EffectiveFrom = null);

/// <summary>
/// Record the withholding amount threshold (VUK 232, SAT-D135) from <c>EffectiveFrom</c> (omitted = today; the same date rules).
/// <c>Amount</c> is lossless decimal text above 0 (at most 15 integer digits and 4 decimals, never rounded);
/// <c>CurrencyCode</c> is the ISO 4217 code the amount is in (entered, never assumed). <c>ExpectedVersion</c> is the
/// threshold's version ("0" while none is recorded).
/// </summary>
public sealed record RecordWithholdingThresholdRequestV1(
    string? ExpectedVersion,
    string? Amount,
    string? CurrencyCode,
    string? Reason,
    DateOnly? EffectiveFrom = null);

/// <summary>The tax master of the server-resolved company on <c>BusinessDate</c> (today, Europe/Istanbul).</summary>
public sealed record TaxMasterViewV1(
    Guid CompanyId,
    DateOnly BusinessDate,
    IReadOnlyList<TaxItemTypeDefaultSeriesViewV1> ItemTypeDefaults,
    IReadOnlyList<TaxCodeListViewV1> CodeLists,
    TaxWithholdingThresholdViewV1 WithholdingThreshold,
    TaxBuyerSituationListViewV1 BuyerSituations);

/// <summary>
/// One item type's default tax (SAT-D139): its edit fence (<c>Version</c>, "0" while only the catalog answers), the record in
/// force today (null = "kaynak yok") and the scheduled ones.
/// </summary>
public sealed record TaxItemTypeDefaultSeriesViewV1(
    string ItemType,
    string Version,
    TaxItemTypeDefaultRecordViewV1? InForce,
    IReadOnlyList<TaxItemTypeDefaultRecordViewV1> Scheduled);

/// <summary>
/// One default record. <c>Source</c> <c>Catalog</c>: the reviewed catalog version (its id is the catalog profile id, it has no
/// recorder and cannot be withdrawn); <c>Record</c>: entered in Settings. Percents are lossless text.
/// </summary>
public sealed record TaxItemTypeDefaultRecordViewV1(
    Guid RecordId,
    string Source,
    string RecordVersion,
    DateOnly EffectiveFrom,
    bool VatApplies,
    string? VatPercent,
    string? ExemptionCode,
    bool WithholdingApplies,
    string? WithholdingPercent,
    string? WithholdingCode,
    string? RecordedBy,
    DateTimeOffset? RecordedAtUtc,
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

/// <summary>
/// SAT-D158: the buyer situation list of the server-resolved company. <c>Version</c> is its edit fence ("0" while only the
/// reviewed catalog answers); <c>InForce</c> the version in force on the business date (null = "kaynak yok"); scheduled ones.
/// </summary>
public sealed record TaxBuyerSituationListViewV1(
    Guid CompanyId,
    DateOnly BusinessDate,
    string Version,
    TaxBuyerSituationListVersionViewV1? InForce,
    IReadOnlyList<TaxBuyerSituationListVersionViewV1> Scheduled);

/// <summary>
/// One buyer situation list version. <c>Source</c> <c>Catalog</c>: the reviewed version 0 (no recorder, cannot be withdrawn);
/// <c>Record</c>: published in Settings.
/// </summary>
public sealed record TaxBuyerSituationListVersionViewV1(
    string ListVersion,
    string Source,
    DateOnly EffectiveFrom,
    IReadOnlyList<TaxBuyerSituationEntryViewV1> Entries,
    string? RecordedBy,
    DateTimeOffset? RecordedAtUtc,
    string Reason);

/// <summary>One buyer situation and its tax effect (SAT-D158), see <see cref="TaxBuyerSituationEntryV1"/>.</summary>
public sealed record TaxBuyerSituationEntryViewV1(
    string Code,
    string Name,
    bool WithholdingApplies,
    string VatMode,
    bool DocumentRequired);

/// <summary>
/// The withholding amount threshold (VUK 232 fatura sınırı, SAT-D135: a dated Settings value that starts EMPTY).
/// <c>Version</c> is its edit fence ("0" while none is recorded); <c>InForce</c> null = "kaynak yok".
/// </summary>
public sealed record TaxWithholdingThresholdViewV1(
    string Version,
    TaxWithholdingThresholdRecordViewV1? InForce,
    IReadOnlyList<TaxWithholdingThresholdRecordViewV1> Scheduled);

/// <summary>One immutable threshold record: the amount as lossless decimal text and its ISO 4217 currency code.</summary>
public sealed record TaxWithholdingThresholdRecordViewV1(
    Guid RecordId,
    string RecordVersion,
    DateOnly EffectiveFrom,
    string Amount,
    string CurrencyCode,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc,
    string Reason);

/// <summary>A tax master command's terminal answer (stored for replay). <c>Code</c> is null for a success and the 404.</summary>
public sealed record TaxMasterCommandAnswerV1(int StatusCode, string? Code, string Title, string Detail);

public sealed record TaxMasterCommandResultV1(TaxMasterCommandAnswerV1 Answer, bool IsReplay, string OriginalCorrelationId);

/// <summary>
/// The tax master administration (SAT-D83/D133/D135/D139). Every read and command is authorized first on
/// <see cref="SettingsTaxMasterPermissionManifestV1.TaxMasterManage"/> company-wide in the server-resolved company and
/// re-measured before every save. <see cref="UnauthorizedAccessException"/> means 403. One person decides (SAT-D68
/// precedent): a mandatory reason, Idempotency-Key, expected version and one audit event per real change.
/// </summary>
public interface ISettingsTaxMasterAdministrationV1
{
    Task<TaxMasterViewV1> GetAsync(CancellationToken cancellationToken = default);

    Task<TaxMasterCommandResultV1> RecordItemTypeDefaultAsync(string itemType, RecordItemTypeDefaultRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<TaxMasterCommandResultV1> WithdrawItemTypeDefaultAsync(Guid recordId, WithdrawTaxRecordRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<TaxMasterCommandResultV1> PublishCodeListAsync(string listKind, PublishTaxCodeListRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<TaxMasterCommandResultV1> WithdrawCodeListAsync(string listKind, long listVersion, WithdrawTaxCodeListRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<TaxMasterCommandResultV1> RecordWithholdingThresholdAsync(RecordWithholdingThresholdRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    /// <summary>Withdraw a threshold record before it takes effect; <c>ExpectedVersion</c> is the threshold's version.</summary>
    Task<TaxMasterCommandResultV1> WithdrawWithholdingThresholdAsync(Guid recordId, WithdrawTaxRecordRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    /// <summary>SAT-D158: the buyer situation list (the same authority as <see cref="GetAsync"/>).</summary>
    Task<TaxBuyerSituationListViewV1> GetBuyerSituationsAsync(CancellationToken cancellationToken = default);

    /// <summary>SAT-D158: publish a whole buyer situation list version.</summary>
    Task<TaxMasterCommandResultV1> PublishBuyerSituationListAsync(PublishTaxBuyerSituationListRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    /// <summary>SAT-D158: withdraw a buyer situation list version before it takes effect.</summary>
    Task<TaxMasterCommandResultV1> WithdrawBuyerSituationListAsync(long listVersion, WithdrawTaxCodeListRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}

/// <summary>Whether a tax value was found. <c>Unresolved</c> is "kaynak yok": never 0, never a guessed default.</summary>
public static class TaxResolutionStatesV1
{
    /// <summary>The value is in force on the date.</summary>
    public const string Resolved = "Resolved";

    /// <summary>VAT: "KDV yok" (exempt) by the default; the exemption code is reported separately.</summary>
    public const string Exempt = "Exempt";

    /// <summary>
    /// VAT: KDV %0 by the buyer situation (SAT-D147, <see cref="TaxBuyerSituationVatModesV1.ZeroWithExemptionCode"/>); the
    /// exemption code part is reported separately and must be <c>Resolved</c> before the invoice.
    /// </summary>
    public const string ZeroRated = "ZeroRated";

    /// <summary>Withholding: the default says none applies.</summary>
    public const string NotApplied = "NotApplied";

    /// <summary>A code is not required by the default (VAT charged; no withholding).</summary>
    public const string NotRequired = "NotRequired";

    /// <summary>No source answers (see the reason): the consumer shows "kaynak yok" and the invoice waits (fail-closed).</summary>
    public const string Unresolved = "Unresolved";
}

/// <summary>Why a value is <see cref="TaxResolutionStatesV1.Unresolved"/>.</summary>
public static class TaxResolutionReasonsV1
{
    public const string ProductItemTypeMissing = "product_item_type_missing";
    public const string ProductNotFound = "product_not_found";
    public const string ItemTypeUnknown = "item_type_unknown";
    public const string ItemTypeDefaultMissing = "item_type_default_missing";
    public const string ExemptionCodeMissing = "exemption_code_missing";
    public const string WithholdingCodeMissing = "withholding_code_missing";
    public const string CodeNotInList = "code_not_in_list";
    public const string CodeListMissing = "code_list_missing";
    public const string WithholdingThresholdMissing = "withholding_threshold_missing";
    public const string BuyerNotSubjectToWithholding = "buyer_not_subject_to_withholding";

    /// <summary>SAT-D158: the situation code is not in the buyer situation list in force on the date (unknown or withdrawn).</summary>
    public const string BuyerSituationUnknown = "buyer_situation_unknown";

    /// <summary>SAT-D147: the buyer situation sets KDV %0 under an exemption code and removes the withholding.</summary>
    public const string BuyerZeroRatedWithExemptionCode = "buyer_zero_rated_with_exemption_code";

    /// <summary>No buyer situation list is in force on the date.</summary>
    public const string BuyerSituationListMissing = "buyer_situation_list_missing";
}

/// <summary>
/// The DEFAULT tax a line starts from (SAT-D134/D139) for one item type, adjusted for the order's buyer situation (SAT-D140/D158:
/// the entry of the Settings buyer situation list in force, its effect as data; <c>BuyerAdjustment</c> names the change, e.g.
/// <c>buyer_not_subject_to_withholding</c> or <c>buyer_zero_rated_with_exemption_code</c>; <c>BuyerSituationListVersion</c>,
/// <c>BuyerVatMode</c> and <c>BuyerDocumentRequired</c> are the applied entry; an unknown or withdrawn situation is
/// <c>Unresolved</c> with <c>buyer_situation_unknown</c>). SAT-D147: a <c>ZeroWithExemptionCode</c> situation answers VAT
/// <c>ZeroRated</c> (0 %), no withholding, and the exemption code the order chose, checked against the exemption list in force
/// (none chosen / no list / not listed: the code part is <c>Unresolved</c> and the invoice waits). <c>BuyerDocumentRequired</c>
/// true: the invoice also waits until the order carries the buyer's document (no, date) — the order's check, flagged here. On
/// one business date, in the server-resolved company; the order line may change it per line (SAT-D93). <c>State</c> <c>Unresolved</c> (with <c>Reason</c>) when the product has
/// no item type or no default is in force — then every other part is empty. Otherwise: VAT <c>Resolved</c> with
/// <c>VatPercent</c>, or <c>Exempt</c> ("KDV yok") with the exemption code part; withholding <c>Resolved</c> with
/// <c>WithholdingPercent</c> (the withheld share of the line VAT in percent) and the withholding code part, or
/// <c>NotApplied</c>. A code part is <c>NotRequired</c>, <c>Resolved</c> (in the list in force on the date) or
/// <c>Unresolved</c> (none entered, or not in the list) — an unresolved code makes the invoice wait, the rate stays usable.
/// The consumer keeps <c>RecordId</c>/<c>RecordVersion</c>/<c>Source</c> as its applied snapshot and computes the amounts with
/// its ONE calculator (Sales <c>SalesOrderTotalsCalculator</c>: withholding from the rounded line VAT, SAT-D75; the unit price
/// not rounded, SAT-D76).
/// </summary>
public sealed record SettingsTaxDefaultResolutionV1(
    Guid CompanyId,
    Guid? ProductId,
    string? ItemType,
    string BuyerSituation,
    DateOnly On,
    string State,
    string? Reason,
    string? Source,
    Guid? RecordId,
    long? RecordVersion,
    string? VatState,
    decimal? VatPercent,
    string? ExemptionCodeState,
    string? ExemptionCode,
    string? ExemptionCodeReason,
    string? WithholdingState,
    decimal? WithholdingPercent,
    string? WithholdingCodeState,
    string? WithholdingCode,
    string? WithholdingCodeReason,
    string? BuyerAdjustment,
    long? BuyerSituationListVersion,
    string? BuyerVatMode,
    bool BuyerDocumentRequired);

/// <summary>
/// SAT-D158: the buyer situation list in force on a date: <c>Resolved</c> with its entries (each with its tax effect), or
/// <c>Unresolved</c> (<c>buyer_situation_list_missing</c>).
/// </summary>
public sealed record SettingsTaxBuyerSituationListResolutionV1(
    Guid CompanyId,
    DateOnly On,
    string State,
    long? ListVersion,
    DateOnly? EffectiveFrom,
    IReadOnlyList<TaxBuyerSituationEntryViewV1> Entries,
    string? Reason);

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

/// <summary>The withholding amount threshold in force on a date (see <see cref="ISettingsLineTaxResolverV1.GetWithholdingThresholdAsync"/>).</summary>
public sealed record SettingsWithholdingThresholdResolutionV1(
    Guid CompanyId,
    DateOnly On,
    string State,
    decimal? Amount,
    string? CurrencyCode,
    Guid? RecordId,
    long? RecordVersion,
    DateOnly? EffectiveFrom,
    string? Reason);

/// <summary>
/// The read port the Sales order line and invoice consume (SAT-D93/D134/D135/D139). Company and actor are the server's: an
/// actor who cannot access the company is <c>settings_access_denied</c>, an unreadable source <c>settings_provider_unavailable</c>
/// (a <see cref="SettingsPrerequisiteException"/>). Every missing value is an explicit <c>Unresolved</c> with a reason — never 0
/// and never a guessed default. The answer is reference data, not an authorization: the consumer checks its own permission.
/// </summary>
public interface ISettingsLineTaxResolverV1
{
    /// <summary>
    /// The default tax for the product's item type (the Product owner answers the type) for the order's buyer situation code
    /// (SAT-D140/D158; a malformed code is <c>settings_tax_master_request_invalid</c>, one the list in force does not carry is
    /// <c>Unresolved</c>) and, for a <c>ZeroWithExemptionCode</c> situation, the exemption code the order chose (SAT-D147; ignored
    /// otherwise; a malformed code is <c>settings_tax_master_request_invalid</c>).
    /// </summary>
    Task<SettingsTaxDefaultResolutionV1> ResolveAsync(Guid productId, string buyerSituation, DateOnly on,
        string? exemptionCode = null, CancellationToken cancellationToken = default);

    /// <summary>The default tax for an item type and buyer situation, so a line can start from it (SAT-D139/D140/D147).</summary>
    Task<SettingsTaxDefaultResolutionV1> GetItemTypeDefaultAsync(string itemType, string buyerSituation, DateOnly on,
        string? exemptionCode = null, CancellationToken cancellationToken = default);

    /// <summary>SAT-D158: the buyer situation list in force on <paramref name="on"/>, for the order header's choice.</summary>
    Task<SettingsTaxBuyerSituationListResolutionV1> GetBuyerSituationsAsync(DateOnly on,
        CancellationToken cancellationToken = default);

    Task<SettingsTaxCodeListResolutionV1> GetCodeListAsync(string kind, DateOnly on,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The withholding amount threshold (VUK 232, SAT-D135) in force on <paramref name="on"/>: <c>Resolved</c> with the amount
    /// and its currency, or <c>Unresolved</c> (<c>withholding_threshold_missing</c>) while none is recorded — an invoice with a
    /// withholding line then waits; no number is ever assumed. The comparison against the transaction total (KDV dahil, the
    /// whole transaction, never split) is the consumer's.
    /// </summary>
    Task<SettingsWithholdingThresholdResolutionV1> GetWithholdingThresholdAsync(DateOnly on,
        CancellationToken cancellationToken = default);
}

public enum SettingsProductItemTypeOutcomeV1
{
    Unavailable = 0,
    Denied = 1,
    NotFound = 2,
    NotSet = 3,
    Found = 4,
}

/// <summary>The Product owner's item type of a product in the company (SAT-D134).</summary>
public sealed record SettingsProductItemTypeAnswerV1(SettingsProductItemTypeOutcomeV1 Outcome, string? ItemType);

/// <summary>
/// The Settings consumer port onto the Product owner's item type (composed by the host; Settings never reads Product's
/// tables). Company and actor are copies of the server context.
/// </summary>
public interface ISettingsProductItemTypeSourceV1
{
    Task<SettingsProductItemTypeAnswerV1> GetAsync(Guid companyId, Guid productId, CancellationToken cancellationToken = default);
}
