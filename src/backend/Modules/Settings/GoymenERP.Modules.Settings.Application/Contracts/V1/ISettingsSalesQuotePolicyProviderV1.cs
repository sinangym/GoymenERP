namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// The Settings-owned Sales QUOTE policies of the server-resolved company (SAT-D25, SAT-D39;
/// <c>docs/decisions/SINAN_KARARLARI_2026-10-27_SATIS_EK3.md</c> and <c>…_EK4.md</c>), versioned and
/// company-scoped like every Settings policy (DEC-SET-001). A port of its own, beside
/// <see cref="ISettingsSalesPolicyProviderV1"/>, so that no existing implementer of that port changes.
/// <para>
/// Outcomes, in order (the same as the Sales policy port): cancelled → <see cref="OperationCanceledException"/>;
/// no actor or company access → <c>settings_access_denied</c>; a company without an approved Sales
/// policy source → <c>settings_provider_unavailable</c>; a reachable source without a record of the
/// family → <see cref="SettingsPrerequisiteCodesV1.SalesPolicyNotFound"/>; a malformed catalog →
/// <c>settings_profile_invalid</c>; otherwise the exact version for the date with its real window — the
/// consumer checks activity and window itself.
/// </para>
/// <para>
/// No value is a code constant anywhere: the list content and the day count are Settings records
/// that Sinan enters (SAT-D25, SAT-D39). Without a record the loss is refused (SAT-D25: "liste kaydı
/// yoksa kayıp kaydı fail-closed reddedilir") and no validity is suggested (SAT-D39: "Ayarlar'da değer
/// yoksa varsayılan önerilmez").
/// </para>
/// </summary>
public interface ISettingsSalesQuotePolicyProviderV1
{
    /// <summary>The company's closed list of quote loss reasons in force on the date (SAT-D25).</summary>
    Task<SalesQuoteLossReasonListV1> GetSalesQuoteLossReasonsAsync(
        DateOnly effectiveOn, CancellationToken cancellationToken = default);

    /// <summary>The company's default quote validity, in days, in force on the date (SAT-D39).</summary>
    Task<SalesQuoteDefaultValidityPolicyV1> GetSalesQuoteDefaultValidityAsync(
        DateOnly effectiveOn, CancellationToken cancellationToken = default);
}

/// <summary>
/// One entry of the closed loss reason list. <see cref="ReasonId"/> is the stable identity a quote
/// records; <see cref="Code"/> is a stable report key and <see cref="Label"/> the Turkish text shown.
/// <see cref="RequiresExplanation"/> is the list's own flag for the entries that need a free-text
/// explanation (SAT-D25's "Diğer seçilirse açıklama zorunlu"); no entry is recognised by its label or
/// code. A passive entry (<see cref="IsActive"/> false) is not selectable but keeps its meaning for
/// quotes that recorded it.
/// </summary>
public sealed record SalesQuoteLossReasonV1(
    Guid ReasonId,
    string Code,
    string Label,
    bool RequiresExplanation,
    bool IsActive);

/// <summary>One approved version of a company's loss reason list (SAT-D25).</summary>
public sealed record SalesQuoteLossReasonListV1(
    Guid CompanyId,
    ProfileVersionV1 Profile,
    IReadOnlyList<SalesQuoteLossReasonV1> Reasons,
    bool IsActive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough);

/// <summary>
/// One approved version of a company's default quote validity (SAT-D39): a positive number of days.
/// The suggested validity date of a quote is its business date plus <see cref="ValidityDays"/>; the
/// person may change it, and a quote without a validity date stays valid (the date is not mandatory).
/// </summary>
public sealed record SalesQuoteDefaultValidityPolicyV1(
    Guid CompanyId,
    ProfileVersionV1 Profile,
    int ValidityDays,
    bool IsActive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough);
