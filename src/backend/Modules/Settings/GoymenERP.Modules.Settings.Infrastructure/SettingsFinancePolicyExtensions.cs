using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Settings.Infrastructure;

public static class SettingsFinancePolicyExtensions
{
    public static IServiceCollection AddSettingsFinancePolicyAuthoring(this IServiceCollection services)
    {
        services.AddScoped<SettingsFinancePolicyAdministration>();
        services.AddScoped<ISettingsFinancePolicyAdministrationV1>(sp => sp.GetRequiredService<SettingsFinancePolicyAdministration>());
        services.AddScoped<ISettingsFinanceApprovalPolicyProviderV1>(sp => sp.GetRequiredService<SettingsFinancePolicyAdministration>());
        services.AddScoped<ISettingsCashCountDifferencePolicyProviderV1>(sp => sp.GetRequiredService<SettingsFinancePolicyAdministration>());
        services.AddScoped<ISettingsFinancePolicyPreviewV1>(sp => sp.GetRequiredService<SettingsFinancePolicyAdministration>());
        services.AddScoped<ISettingsBankInstitutionAdministrationV1>(sp => sp.GetRequiredService<SettingsFinancePolicyAdministration>());
        services.AddScoped<ISettingsBankInstitutionCatalogV1>(sp => sp.GetRequiredService<SettingsFinancePolicyAdministration>());
        services.AddScoped<ISettingsCollateralRateAdministrationV1>(sp => sp.GetRequiredService<SettingsFinancePolicyAdministration>());
        services.AddScoped<ISettingsCollateralRateProviderV1>(sp => sp.GetRequiredService<SettingsFinancePolicyAdministration>());
        return services;
    }
}
