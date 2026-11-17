using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

public sealed partial class SettingsCompanyPolicyProviderV1 : ISettingsManufacturingScrapCostPolicyProviderV1,
    ISettingsNotificationRetentionPolicyProviderV1
{
    public async Task<SettingsManufacturingScrapCostPolicyV1> GetScrapCostPolicyAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var actor = _currentUser.UserId;
        var row = await LatestAsync(companyId, CompanyPolicyKeysV1.ManufacturingIncludeScrapCost, cancellationToken);
        RequireAccess(companyId, cancellationToken);
        if (actor != _currentUser.UserId) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.AccessDenied);
        if (row is null) return new(companyId, true, 0); // SAT-D305 decided initial value, not a store-failure fallback.
        if (row.BooleanValue is not { } include || row.WholeNumberValue is not null || !NoTaxOrRate(row))
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
        return new(companyId, include, row.Version);
    }
    public async Task<SettingsNotificationRetentionPolicyV1> GetNotificationRetentionAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var actor = _currentUser.UserId;
        var row = await LatestAsync(companyId, CompanyPolicyKeysV1.NotificationActiveDays, cancellationToken);
        RequireAccess(companyId, cancellationToken);
        if (actor != _currentUser.UserId) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.AccessDenied);
        if (row is null) return new(companyId, 90, 0); // KLN-D114/D115 initial value; no deletion is implied.
        if (row.WholeNumberValue is not { } days || days < 0 || row.BooleanValue is not null || !NoTaxOrRate(row))
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
        return new(companyId, days, row.Version);
    }
    private static bool NoTaxOrRate(CompanyPolicyVersionRow row) => row.PercentValue is null && row.VatApplies is null
        && row.VatPercent is null && row.ExemptionCode is null && row.WithholdingApplies is null
        && row.WithholdingPercent is null && row.WithholdingCode is null;
}
