using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Shell;

/// <summary>
/// SAT-D61…SAT-D70: the Ayarlar screen of the Sales discount limit groups, their limits and the person → group bindings.
/// It stands on <see cref="SettingsPermissionManifestV1.SalesDiscountLimitsManage"/> alone, company-wide, in one company
/// (the read, the commands and the binding editor are all that one name; the API re-authorizes every request, and the
/// binding write in every company it writes). A declaration grants nothing: the name is given to no role here (SAT-D65,
/// SAT-D69 — a human gate). The id and the route are the frontend lane's (<c>src/settings/capabilities.ts</c>).
/// </summary>
public sealed class SettingsShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "SETTINGS";
    public const string OwnerContractVersion = "1.1";

    public const string SalesDiscountLimitsCapabilityId = "settings.sales-discount-limits.manage";
    public const string SalesDiscountLimitsRouteId = "settings.sales-discount-limits.manage";
    public const string SalesDiscountLimitsRoutePath = "/ayarlar/iskonto-limitleri";
    public const string SalesDiscountLimitsScreenId = "AY-ISK-01";

    // MOD-03 (SAT-D83, provider 1.1; EK19 SAT-D133…D139): the tax master screen (the default VAT and withholding per item
    // type, the UBL-TR code lists and the VUK 232 threshold) on settings.tax-master.manage alone, company-wide, in one
    // company; granted to no role here.
    public const string TaxMasterCapabilityId = "settings.tax-master.manage";
    public const string TaxMasterRouteId = "settings.tax-master.manage";
    public const string TaxMasterRoutePath = "/ayarlar/vergi";
    public const string TaxMasterScreenId = "AY-VRG-01";

    public string Owner => OwnerId;

    public string ContractVersion => OwnerContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations() =>
    [
        new ShellCapabilityDeclaration(
            SalesDiscountLimitsCapabilityId,
            SalesDiscountLimitsRouteId,
            SalesDiscountLimitsRoutePath,
            SalesDiscountLimitsScreenId,
            ShellCapabilityRoutePrecedence.Static,
            ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide,
            [SettingsPermissionManifestV1.SalesDiscountLimitsManage],
            ImplementationAvailable: true),
        new ShellCapabilityDeclaration(
            TaxMasterCapabilityId,
            TaxMasterRouteId,
            TaxMasterRoutePath,
            TaxMasterScreenId,
            ShellCapabilityRoutePrecedence.Static,
            ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide,
            [SettingsTaxMasterPermissionManifestV1.TaxMasterManage],
            ImplementationAvailable: true),
    ];
}
