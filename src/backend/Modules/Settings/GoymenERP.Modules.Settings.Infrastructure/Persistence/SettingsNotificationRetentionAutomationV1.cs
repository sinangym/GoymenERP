using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

internal sealed class SettingsNotificationRetentionAutomationV1(SettingsDbContext database, ICompanyContext company)
    : ISettingsNotificationRetentionAutomationV1
{
    public async Task<SettingsNotificationRetentionPolicyV1> ReadAsync(CancellationToken cancellationToken = default)
    {
        var companyId = company.IsResolved ? company.CompanyId : Guid.Empty;
        if (companyId == Guid.Empty) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.AccessDenied);
        CompanyPolicyVersionRow? row;
        try { row = await CompanyPolicyReads.LatestAsync(database, CompanyPolicyKeysV1.NotificationActiveDays, cancellationToken); }
        catch (DbException) { throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable); }
        catch (InvalidOperationException) { throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable); }
        if (!company.IsResolved || company.CompanyId != companyId) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.AccessDenied);
        if (row is null) return new SettingsNotificationRetentionPolicyV1(companyId, 90, 0);
        if (row.CompanyId != companyId || row.PolicyKey != CompanyPolicyKeysV1.NotificationActiveDays || row.Version < 1
            || row.WholeNumberValue is not { } days || days < 0 || row.BooleanValue is not null || row.PercentValue is not null
            || row.VatApplies is not null || row.VatPercent is not null || row.ExemptionCode is not null
            || row.WithholdingApplies is not null || row.WithholdingPercent is not null || row.WithholdingCode is not null)
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
        return new SettingsNotificationRetentionPolicyV1(companyId, days, row.Version);
    }
}
