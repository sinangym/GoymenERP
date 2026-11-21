using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The host's <see cref="ISettingsUnitConversionPolicyProviderV1"/> (F-004; BS-9/BS-13/BS-16/BS-17, URN-D7…D9). The only
/// source is the company's stored unit conversion policy versions; there is no compiled value and no fallback. The version
/// actually in force on the date (<see cref="SalesPolicyTimelineV1.InForceOn{T}"/>) answers; none recorded, or a date before
/// the first version's start, is <see cref="SettingsPrerequisiteCodesV1.UnitConversionPolicyNotFound"/> — a consumer then
/// refuses to convert rather than guess. Order: cancelled → <see cref="OperationCanceledException"/>; no access →
/// <c>settings_access_denied</c>; an unreadable store → <c>settings_provider_unavailable</c>; malformed stored codes →
/// <c>settings_profile_invalid</c>.
/// </summary>
public sealed class SettingsUnitConversionPolicyProviderV1 : ISettingsUnitConversionPolicyProviderV1
{
    private const string ProfileInvalidCode = "settings_profile_invalid";
    private readonly SettingsDbContext _database;
    private readonly ICompanyContext _companyContext;
    private readonly ICurrentUser _currentUser;

    public SettingsUnitConversionPolicyProviderV1(SettingsDbContext database, ICompanyContext companyContext,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(companyContext);
        ArgumentNullException.ThrowIfNull(currentUser);
        _database = database;
        _companyContext = companyContext;
        _currentUser = currentUser;
    }

    public async Task<SettingsUnitConversionPolicyV1> GetUnitConversionPolicyAsync(DateOnly effectiveOn,
        CancellationToken cancellationToken = default)
    {
        RequireAccess(cancellationToken);
        var companyId = _companyContext.CompanyId;
        UnitConversionPolicyVersionRow[] rows;
        try
        {
            rows = await UnitConversionPolicyReads.LoadAsync(_database, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException(UnitConversionPolicyProblemCodesV1.ProviderUnavailable);
        }

        var entry = SalesPolicyTimelineV1.InForceOn(UnitConversionPolicyReads.Timeline(rows), effectiveOn)
                    ?? throw new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.UnitConversionPolicyNotFound);
        var row = entry.Value;
        if (row.CompanyId != companyId
            || row.DensityGramsPerCubicCentimetre <= 0m
            || !UnitConversionPolicyRulesV1.IsRoundingDecimals(row.CoefficientDecimals)
            || !UnitConversionPolicyRulesV1.IsRoundingDecimals(row.DocumentTotalDecimals)
            || !UnitConversionPolicyRulesV1.TryParseCoefficientRounding(row.CoefficientRounding, out var coefficientRounding)
            || !UnitConversionPolicyRulesV1.TryParseRounding(row.DocumentTotalRounding, out var documentTotalRounding)
            || !UnitConversionPolicyRulesV1.TryParseRounding(row.WholeUnitRounding, out var wholeUnitRounding)
            || row.WeighbridgeTolerancePercent < UnitConversionPolicyInputLimitsV1.MinimumPercent
            || row.WeighbridgeTolerancePercent > UnitConversionPolicyInputLimitsV1.MaximumPercent
            || !UnitConversionPolicyRulesV1.TryParseBoundary(row.ToleranceBoundary, out var boundary)
            || !UnitConversionPolicyRulesV1.TryParseBase(row.ToleranceBase, out var toleranceBase))
        {
            throw new SettingsPrerequisiteException(ProfileInvalidCode);
        }

        return new SettingsUnitConversionPolicyV1(
            companyId,
            new ProfileVersionV1(row.ProfileId, entry.Version),
            UnitConversionPolicyRulesV1.Canonical(row.DensityGramsPerCubicCentimetre),
            row.CoefficientDecimals,
            coefficientRounding,
            row.DocumentTotalDecimals,
            documentTotalRounding,
            wholeUnitRounding,
            UnitConversionPolicyRulesV1.Canonical(row.WeighbridgeTolerancePercent),
            boundary,
            toleranceBase,
            entry.EffectiveFrom,
            entry.EffectiveThrough);
    }

    /// <summary>Cancelled → <see cref="OperationCanceledException"/>; no access → <c>settings_access_denied</c>.</summary>
    private void RequireAccess(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(_currentUser.UserId)
            || !_companyContext.IsResolved || _companyContext.CompanyId == Guid.Empty
            || !_currentUser.CanAccessCompany(_companyContext.CompanyId))
        {
            throw new SettingsPrerequisiteException(UnitConversionPolicyProblemCodesV1.AccessDenied);
        }
    }
}
