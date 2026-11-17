using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;

namespace GoymenERP.Modules.Settings.Infrastructure.Shell;

public sealed partial class SettingsShellCapabilityProvider
{
    public const string FinancePolicyPrepareCapabilityId = "settings.finance-policies.prepare";
    public const string FinancePolicyPublishCapabilityId = "settings.finance-policies.publish";
    public const string FinancePolicyPublishCriticalCapabilityId = "settings.finance-policies.publish-critical";
    public const string CashDifferencePolicyCapabilityId = "settings.cash-count-difference-policies.prepare";
    public const string BankInstitutionsCapabilityId = "settings.bank-institutions.prepare";
    public const string CollateralRatePolicyCapabilityId = "settings.collateral-rate-policies.prepare";

    // No133 records names only. These staged routes use the existing company-wide policy purposes.
    private static IReadOnlyCollection<ShellCapabilityDeclaration> FinancePolicyDeclarations()
    {
        var prepare = FinancePolicyRoute(FinancePolicyPrepareCapabilityId, "/ayarlar/finans-onay-politikalari", "AY-FOP-01");
        return
        [
            prepare,
            prepare with
            {
                CapabilityId = FinancePolicyPublishCapabilityId,
                ParentCapabilityId = FinancePolicyPrepareCapabilityId,
                RequiredPermissions = [ErpPermissions.SettingsPoliciesPrepare, ErpPermissions.SettingsPoliciesPublish],
            },
            prepare with
            {
                CapabilityId = FinancePolicyPublishCriticalCapabilityId,
                ParentCapabilityId = FinancePolicyPrepareCapabilityId,
                RequiredPermissions = [ErpPermissions.SettingsPoliciesPrepare, ErpPermissions.SettingsPoliciesPublish,
                    ErpPermissions.SettingsPoliciesPublishCritical],
            },
            FinancePolicyRoute(CashDifferencePolicyCapabilityId, "/ayarlar/kasa-sayim-fark-politikalari", "AY-KSF-01"),
            FinancePolicyRoute(BankInstitutionsCapabilityId, "/ayarlar/banka-kurumlari", "AY-BNK-01"),
            FinancePolicyRoute(CollateralRatePolicyCapabilityId, "/ayarlar/teminat-oran-politikalari", "AY-TOP-01"),
        ];
    }

    private static ShellCapabilityDeclaration FinancePolicyRoute(string capability, string path, string screen) => new(
        capability, capability, path, screen, ShellCapabilityRoutePrecedence.Static,
        ShellCapabilityContextRequirement.SingleCompany, ShellCapabilityScopeRequirement.CompanyWide,
        [ErpPermissions.SettingsPoliciesPrepare], ImplementationAvailable: false);
}
