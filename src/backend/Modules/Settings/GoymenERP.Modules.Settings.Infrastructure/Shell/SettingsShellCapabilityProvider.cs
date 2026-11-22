using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Shell;

/// <summary>
/// SAT-D61…SAT-D70: the Ayarlar screen of the Sales discount limit groups, their limits and the person → group bindings.
/// It stands on <see cref="SettingsPermissionManifestV1.SalesDiscountLimitsManage"/> alone, company-wide, in one company
/// (the read, the commands and the binding editor are all that one name; the API re-authorizes every request, and the
/// binding write in every company it writes). A declaration grants nothing: the name is given to no role here (SAT-D65,
/// SAT-D69 — a human gate). The id and the route are the frontend lane's (<c>src/settings/capabilities.ts</c>).
/// <para>
/// 1.1 (MOD-03): the tax master screen. 1.2 (F-025, B-11): the exchange rate screen (<c>/ayarlar/kurlar</c>) on
/// <see cref="SettingsExchangeRatePermissionManifestV1.Read"/>, with three controls that each also need their own
/// write name — the reasoned correction, the half-day mark and the import (which also carries the re-verification).
/// None of the four names is granted to a role here; the API re-authorizes every request on its own name.
/// 1.3 (F-029 S4b, SAT-D5/SAT-D6, URN-D2/D3): the delivery tolerance screen (<c>/ayarlar/teslim-toleransi</c>) on
/// <see cref="SettingsSalesPolicyPermissionManifestV1.DeliveryToleranceManage"/> and the approval amount threshold screen
/// (<c>/ayarlar/onay-esikleri</c>) on <see cref="SettingsSalesPolicyPermissionManifestV1.ApprovalThresholdManage"/>, each
/// company-wide, in one company, on its one name. Neither name is granted to a role here (a human gate).
/// 1.4 (KIM-D34, KIM-D37): the group-wide login attempt limit screen (<c>/ayarlar/giris-deneme-siniri</c>) on
/// <see cref="SettingsLoginRateLimitPermissionManifestV1.Manage"/> and the unit conversion policy screen
/// (<c>/ayarlar/birim-cevrim-politikasi</c>) on <see cref="SettingsUnitConversionPolicyPermissionManifestV1.Manage"/>, each
/// company-wide, in one company, on its one name. Neither name is granted to a role here, and neither is Critical.
/// 1.5 (F-029 S1/S2, URN-D2/D3): the unit profile screen (<c>/ayarlar/birim-profilleri</c>) on
/// <see cref="SettingsUnitProfilePermissionManifestV1.Manage"/>, company-wide, in one company, on its one name; granted to
/// no role here and not Critical.
/// 1.5 (URT-D89): the work center capacity screen (<c>/ayarlar/is-merkezi-kapasitesi</c>) on
/// <see cref="SettingsWorkCenterCapacityPermissionManifestV1.Manage"/>, company-wide, in one company, on its one name. The
/// name is granted to no role here (the production manager is a persona note only) and is not Critical.
/// 1.6 (API-98/B-14, KIM-D49/KIM-D51): the expense and service type lists screen (<c>/ayarlar/referans-listeleri</c>) on
/// <see cref="SettingsReferenceListPermissionManifestV1.PurchasingListsManage"/> and the paint color and scrap kind lists
/// screen (<c>/ayarlar/uretim-referans-listeleri</c>) on
/// <see cref="SettingsReferenceListPermissionManifestV1.ProductionListsManage"/>, each company-wide, in one company, on its
/// one name. Neither name is granted to a role here (the purchasing manager and the administrator are persona notes only)
/// and neither is Critical.
/// </para>
/// </summary>
public sealed partial class SettingsShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "SETTINGS";
    // 1.18 preserves independent template purposes under an unavailable shared workspace.
    // 1.19 (W1-A, YOL-D18): the approval duration screen (/ayarlar/onay-sureleri) on settings.work-approval-duration.manage.
    public const string OwnerContractVersion = "1.19";
    // Technical screen ids, not additional ERP permissions. Latest ALS-D105 authority decision.
    public const string SalesSubcontractTaxCapabilityId = "settings.sales-subcontract-tax-default.manage";
    public const string PurchasingSubcontractTaxCapabilityId = "settings.purchasing-subcontract-tax-default.manage";
    public const string SalesSubcontractTaxRoutePath = "/ayarlar/satis-fason-vergi-varsayilani";
    public const string PurchasingSubcontractTaxRoutePath = "/ayarlar/alis-fason-vergi-varsayilani";

    public const string ManufacturingParametersCapabilityId = ManufacturingParameterProfileActionsV1.Read;
    public const string ManufacturingParametersRoutePath = "/ayarlar/uretim-parametre-profilleri";
    public const string ManufacturingTolerancesCapabilityId = ManufacturingToleranceProfileReadActionV1.Read;
    public const string ManufacturingTolerancesRoutePath = "/ayarlar/uretim-tolerans-profilleri";

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

    // F-025 (B-11, provider 1.2): the exchange rate screen (day list, correction, half-day mark, re-verification, import)
    // on settings.exchange-rates.read, company-wide, in one company; each command control also needs its own name.
    public const string ExchangeRatesCapabilityId = SettingsExchangeRatePermissionManifestV1.Read;
    public const string ExchangeRatesRouteId = SettingsExchangeRatePermissionManifestV1.Read;
    public const string ExchangeRatesRoutePath = "/ayarlar/kurlar";
    public const string ExchangeRatesScreenId = "AY-KUR-01";
    public const string ExchangeRatesCorrectCapabilityId = SettingsExchangeRatePermissionManifestV1.Correct;
    public const string ExchangeRatesMarkHalfDayCapabilityId = SettingsExchangeRatePermissionManifestV1.MarkHalfDay;
    public const string ExchangeRatesImportCapabilityId = SettingsExchangeRatePermissionManifestV1.Import;

    // F-029 S4b (SAT-D5, provider 1.3): the delivery tolerance timeline (baseline transfer, new versions) on
    // settings.sales-delivery-tolerances.manage alone, company-wide, in one company; granted to no role here.
    public const string SalesDeliveryToleranceCapabilityId = SettingsSalesPolicyPermissionManifestV1.DeliveryToleranceManage;
    public const string SalesDeliveryToleranceRouteId = SettingsSalesPolicyPermissionManifestV1.DeliveryToleranceManage;
    public const string SalesDeliveryToleranceRoutePath = "/ayarlar/teslim-toleransi";
    public const string SalesDeliveryToleranceScreenId = "AY-TSL-01";

    // F-029 S4b (SAT-D6, provider 1.3): the approval amount thresholds per currency (baseline transfer, new versions) on
    // settings.sales-approval-thresholds.manage alone, company-wide, in one company; granted to no role here.
    public const string SalesApprovalThresholdsCapabilityId = SettingsSalesPolicyPermissionManifestV1.ApprovalThresholdManage;
    public const string SalesApprovalThresholdsRouteId = SettingsSalesPolicyPermissionManifestV1.ApprovalThresholdManage;
    public const string SalesApprovalThresholdsRoutePath = "/ayarlar/onay-esikleri";
    public const string SalesApprovalThresholdsScreenId = "AY-ONY-01";

    // KIM-D34 (provider 1.4): the group-wide login attempt limit (versions, a new version) on
    // settings.login-rate-limit.manage alone, company-wide, in one company; granted to no role here.
    public const string LoginRateLimitCapabilityId = SettingsLoginRateLimitPermissionManifestV1.Manage;
    public const string LoginRateLimitRouteId = SettingsLoginRateLimitPermissionManifestV1.Manage;
    public const string LoginRateLimitRoutePath = "/ayarlar/giris-deneme-siniri";
    public const string LoginRateLimitScreenId = "AY-GDS-01";

    // F-004, KIM-D37 (provider 1.4): the company's unit conversion policy (no seed; versions, a new version) on
    // settings.unit-conversion-policies.manage alone, company-wide, in one company; granted to no role here.
    public const string UnitConversionPolicyCapabilityId = SettingsUnitConversionPolicyPermissionManifestV1.Manage;
    public const string UnitConversionPolicyRouteId = SettingsUnitConversionPolicyPermissionManifestV1.Manage;
    public const string UnitConversionPolicyRoutePath = "/ayarlar/birim-cevrim-politikasi";
    public const string UnitConversionPolicyScreenId = "AY-BCP-01";

    // F-029 S1/S2, URN-D2/D3 (provider 1.5): the company's unit profiles (baseline transfer, new versions with an effect
    // preview) on settings.unit-profiles.manage alone, company-wide, in one company; granted to no role here.
    public const string UnitProfilesCapabilityId = SettingsUnitProfilePermissionManifestV1.Manage;
    public const string UnitProfilesRouteId = SettingsUnitProfilePermissionManifestV1.Manage;
    public const string UnitProfilesRoutePath = "/ayarlar/birim-profilleri";
    public const string UnitProfilesScreenId = "AY-BRP-01";

    public const string MoneyProfilesCapabilityId = SettingsMoneyProfilePermissionManifestV1.Manage;
    public const string MoneyProfilesRouteId = SettingsMoneyProfilePermissionManifestV1.Manage;
    public const string MoneyProfilesRoutePath = "/ayarlar/para-profilleri";
    public const string MoneyProfilesScreenId = "AY-PRP-01";
    // URT-D89 (provider 1.5): the company's work center capacities (input kg/hour and the optional safety buffer; no seed;
    // versions per work center, a new version) on settings.work-center-capacities.manage alone, company-wide, in one
    // company; granted to no role here.
    public const string WorkCenterCapacityCapabilityId = SettingsWorkCenterCapacityPermissionManifestV1.Manage;
    public const string WorkCenterCapacityRouteId = SettingsWorkCenterCapacityPermissionManifestV1.Manage;
    public const string WorkCenterCapacityRoutePath = "/ayarlar/is-merkezi-kapasitesi";
    public const string WorkCenterCapacityScreenId = "AY-IMK-01";

    // API-98, KIM-D49/KIM-D51 (provider 1.6): the company's expense type and service type lists (no seed; entries with
    // versions) on settings.reference-lists.manage alone, company-wide, in one company; granted to no role here.
    public const string PurchasingReferenceListsCapabilityId = SettingsReferenceListPermissionManifestV1.PurchasingListsManage;
    public const string PurchasingReferenceListsRouteId = SettingsReferenceListPermissionManifestV1.PurchasingListsManage;
    public const string PurchasingReferenceListsRoutePath = "/ayarlar/referans-listeleri";
    public const string PurchasingReferenceListsScreenId = "AY-RFL-01";

    // B-14 (provider 1.6): the company's paint color and scrap kind lists (no seed; entries with versions) on
    // settings.production-reference-lists.manage alone, company-wide, in one company; granted to no role here.
    public const string ProductionReferenceListsCapabilityId = SettingsReferenceListPermissionManifestV1.ProductionListsManage;
    public const string ProductionReferenceListsRouteId = SettingsReferenceListPermissionManifestV1.ProductionListsManage;
    public const string ProductionReferenceListsRoutePath = "/ayarlar/uretim-referans-listeleri";
    public const string ProductionReferenceListsScreenId = "AY-URL-01";

    public string Owner => OwnerId;

    public string ContractVersion => OwnerContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations()
    {
        var parameters = new ShellCapabilityDeclaration(ManufacturingParametersCapabilityId,
            ManufacturingParametersCapabilityId, ManufacturingParametersRoutePath, "AY-UPP-01",
            ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide, [ManufacturingParameterProfileActionsV1.Read],
            ImplementationAvailable: true);
        var tolerances = new ShellCapabilityDeclaration(ManufacturingTolerancesCapabilityId,
            ManufacturingTolerancesCapabilityId, ManufacturingTolerancesRoutePath, "AY-UTP-01",
            ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide, [ManufacturingToleranceProfileReadActionV1.Read],
            ImplementationAvailable: true);
        var exchangeRates = new ShellCapabilityDeclaration(
            ExchangeRatesCapabilityId,
            ExchangeRatesRouteId,
            ExchangeRatesRoutePath,
            ExchangeRatesScreenId,
            ShellCapabilityRoutePrecedence.Static,
            ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide,
            [SettingsExchangeRatePermissionManifestV1.Read],
            ImplementationAvailable: true);

        return
        [
            .. DocumentTemplateDeclarations(),
            .. FinancePolicyDeclarations(),
            new ShellCapabilityDeclaration(SalesSubcontractTaxCapabilityId, SalesSubcontractTaxCapabilityId,
                SalesSubcontractTaxRoutePath, "AY-SFV-01", ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany, ShellCapabilityScopeRequirement.CompanyWide,
                [ErpPermissions.SalesOrdersApprove], ImplementationAvailable: false),
            new ShellCapabilityDeclaration(PurchasingSubcontractTaxCapabilityId, PurchasingSubcontractTaxCapabilityId,
                PurchasingSubcontractTaxRoutePath, "AY-AFV-01", ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany, ShellCapabilityScopeRequirement.CompanyWide,
                [ErpPermissions.PurchasingOrdersApprove], ImplementationAvailable: false),
            new ShellCapabilityDeclaration(SettingsWarehouseTransferScaleV1.Manage,
                SettingsWarehouseTransferScaleV1.Manage, "/ayarlar/depo-transfer-tartisi", "STK-S8-SCALE",
                ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleCompany,
                ShellCapabilityScopeRequirement.CompanyWide, [SettingsWarehouseTransferScaleV1.Manage],
                ImplementationAvailable: true),
            new ShellCapabilityDeclaration(SettingsPurchaseInvoiceVariancePolicyPermissionsV1.Manage,
                SettingsPurchaseInvoiceVariancePolicyPermissionsV1.Manage, "/ayarlar/alis-fatura-fark-toleransi", "AY-AFF-01",
                ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleCompany,
                ShellCapabilityScopeRequirement.CompanyWide, [SettingsPurchaseInvoiceVariancePolicyPermissionsV1.Manage],
                ImplementationAvailable: true),
            new ShellCapabilityDeclaration(SettingsSalesContractDiscountLimitPermissionsV1.Manage, SettingsSalesContractDiscountLimitPermissionsV1.Manage,
                "/ayarlar/sozlesme-iskonto-siniri", "AY-SIS-01", ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany, ShellCapabilityScopeRequirement.CompanyWide,
                [SettingsSalesContractDiscountLimitPermissionsV1.Manage], ImplementationAvailable: true),
            new ShellCapabilityDeclaration(SettingsCollateralTypePermissionsV1.Manage, SettingsCollateralTypePermissionsV1.Manage,
                "/ayarlar/teminat-turleri", "AY-TMT-01", ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany, ShellCapabilityScopeRequirement.CompanyWide,
                [SettingsCollateralTypePermissionsV1.Manage], ImplementationAvailable: true),
            new ShellCapabilityDeclaration(SettingsCompanyPolicyPermissionManifestV1.FinanceOpeningPeriodManage,
                SettingsCompanyPolicyPermissionManifestV1.FinanceOpeningPeriodManage,
                "/ayarlar/finans-acilis-donemi", "AY-FAD-01", ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany, ShellCapabilityScopeRequirement.CompanyWide,
                [SettingsCompanyPolicyPermissionManifestV1.FinanceOpeningPeriodManage], ImplementationAvailable: true),
            new ShellCapabilityDeclaration(SettingsOperationalPolicyPermissionsV1.ScrapCostManage, SettingsOperationalPolicyPermissionsV1.ScrapCostManage,
                "/ayarlar/fire-maliyet-katilimi", "AY-FMK-01", ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany, ShellCapabilityScopeRequirement.CompanyWide,
                [SettingsOperationalPolicyPermissionsV1.ScrapCostManage], ImplementationAvailable: true),
            new ShellCapabilityDeclaration(SettingsOperationalPolicyPermissionsV1.NotificationRetentionManage, SettingsOperationalPolicyPermissionsV1.NotificationRetentionManage,
                "/ayarlar/bildirim-etkinlik-suresi", "AY-BES-01", ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany, ShellCapabilityScopeRequirement.CompanyWide,
                [SettingsOperationalPolicyPermissionsV1.NotificationRetentionManage], ImplementationAvailable: true),
            new ShellCapabilityDeclaration(SettingsOperationalPolicyPermissionsV1.WorkApprovalDurationManage, SettingsOperationalPolicyPermissionsV1.WorkApprovalDurationManage,
                "/ayarlar/onay-sureleri", "AY-OS-01", ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany, ShellCapabilityScopeRequirement.CompanyWide,
                [SettingsOperationalPolicyPermissionsV1.WorkApprovalDurationManage], ImplementationAvailable: true),
            new ShellCapabilityDeclaration(SettingsDocumentNumberSeriesPermissionsV1.Manage, SettingsDocumentNumberSeriesPermissionsV1.Manage,
                "/ayarlar/belge-numara-serileri", "AY-BNS-01", ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany, ShellCapabilityScopeRequirement.CompanyWide,
                [SettingsDocumentNumberSeriesPermissionsV1.Manage], ImplementationAvailable: true),
            parameters,
            parameters with { CapabilityId = ManufacturingParameterProfileActionsV1.Prepare,
                ParentCapabilityId = ManufacturingParametersCapabilityId,
                RequiredPermissions = [ManufacturingParameterProfileActionsV1.Read, ManufacturingParameterProfileActionsV1.Prepare] },
            parameters with { CapabilityId = ManufacturingParameterProfileActionsV1.Publish,
                ParentCapabilityId = ManufacturingParametersCapabilityId,
                RequiredPermissions = [ManufacturingParameterProfileActionsV1.Read, ManufacturingParameterProfileActionsV1.Publish] },
            tolerances,
            tolerances with { CapabilityId = ManufacturingToleranceProfileActionsV1.Prepare,
                ParentCapabilityId = ManufacturingTolerancesCapabilityId,
                RequiredPermissions = [ManufacturingToleranceProfileReadActionV1.Read, ManufacturingToleranceProfileActionsV1.Prepare] },
            tolerances with { CapabilityId = ManufacturingToleranceProfileActionsV1.Publish,
                ParentCapabilityId = ManufacturingTolerancesCapabilityId,
                RequiredPermissions = [ManufacturingToleranceProfileReadActionV1.Read, ManufacturingToleranceProfileActionsV1.Publish] },
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
            exchangeRates,
            // Controls of the exchange rate screen: the screen's read plus the command's own name (the re-verification
            // runs on the import name, as the API authorizes it).
            exchangeRates with
            {
                CapabilityId = ExchangeRatesCorrectCapabilityId,
                ParentCapabilityId = ExchangeRatesCapabilityId,
                RequiredPermissions =
                    [SettingsExchangeRatePermissionManifestV1.Read, SettingsExchangeRatePermissionManifestV1.Correct],
            },
            exchangeRates with
            {
                CapabilityId = ExchangeRatesMarkHalfDayCapabilityId,
                ParentCapabilityId = ExchangeRatesCapabilityId,
                RequiredPermissions =
                    [SettingsExchangeRatePermissionManifestV1.Read, SettingsExchangeRatePermissionManifestV1.MarkHalfDay],
            },
            exchangeRates with
            {
                CapabilityId = ExchangeRatesImportCapabilityId,
                ParentCapabilityId = ExchangeRatesCapabilityId,
                RequiredPermissions =
                    [SettingsExchangeRatePermissionManifestV1.Read, SettingsExchangeRatePermissionManifestV1.Import],
            },
            new ShellCapabilityDeclaration(
                SalesDeliveryToleranceCapabilityId,
                SalesDeliveryToleranceRouteId,
                SalesDeliveryToleranceRoutePath,
                SalesDeliveryToleranceScreenId,
                ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany,
                ShellCapabilityScopeRequirement.CompanyWide,
                [SettingsSalesPolicyPermissionManifestV1.DeliveryToleranceManage],
                ImplementationAvailable: true),
            new ShellCapabilityDeclaration(
                SalesApprovalThresholdsCapabilityId,
                SalesApprovalThresholdsRouteId,
                SalesApprovalThresholdsRoutePath,
                SalesApprovalThresholdsScreenId,
                ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany,
                ShellCapabilityScopeRequirement.CompanyWide,
                [SettingsSalesPolicyPermissionManifestV1.ApprovalThresholdManage],
                ImplementationAvailable: true),
            new ShellCapabilityDeclaration(
                LoginRateLimitCapabilityId,
                LoginRateLimitRouteId,
                LoginRateLimitRoutePath,
                LoginRateLimitScreenId,
                ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany,
                ShellCapabilityScopeRequirement.CompanyWide,
                [SettingsLoginRateLimitPermissionManifestV1.Manage],
                ImplementationAvailable: true),
            new ShellCapabilityDeclaration(
                UnitConversionPolicyCapabilityId,
                UnitConversionPolicyRouteId,
                UnitConversionPolicyRoutePath,
                UnitConversionPolicyScreenId,
                ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany,
                ShellCapabilityScopeRequirement.CompanyWide,
                [SettingsUnitConversionPolicyPermissionManifestV1.Manage],
                ImplementationAvailable: true),
            new ShellCapabilityDeclaration(
                MoneyProfilesCapabilityId,
                MoneyProfilesRouteId,
                MoneyProfilesRoutePath,
                MoneyProfilesScreenId,
                ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany,
                ShellCapabilityScopeRequirement.CompanyWide,
                [SettingsMoneyProfilePermissionManifestV1.Manage],
                ImplementationAvailable: true),
            new ShellCapabilityDeclaration(
                UnitProfilesCapabilityId,
                UnitProfilesRouteId,
                UnitProfilesRoutePath,
                UnitProfilesScreenId,
                ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany,
                ShellCapabilityScopeRequirement.CompanyWide,
                [SettingsUnitProfilePermissionManifestV1.Manage],
                ImplementationAvailable: true),
            new ShellCapabilityDeclaration(
                WorkCenterCapacityCapabilityId,
                WorkCenterCapacityRouteId,
                WorkCenterCapacityRoutePath,
                WorkCenterCapacityScreenId,
                ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany,
                ShellCapabilityScopeRequirement.CompanyWide,
                [SettingsWorkCenterCapacityPermissionManifestV1.Manage],
                ImplementationAvailable: true),
            new ShellCapabilityDeclaration(
                PurchasingReferenceListsCapabilityId,
                PurchasingReferenceListsRouteId,
                PurchasingReferenceListsRoutePath,
                PurchasingReferenceListsScreenId,
                ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany,
                ShellCapabilityScopeRequirement.CompanyWide,
                [SettingsReferenceListPermissionManifestV1.PurchasingListsManage],
                ImplementationAvailable: true),
            new ShellCapabilityDeclaration(
                ProductionReferenceListsCapabilityId,
                ProductionReferenceListsRouteId,
                ProductionReferenceListsRoutePath,
                ProductionReferenceListsScreenId,
                ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany,
                ShellCapabilityScopeRequirement.CompanyWide,
                [SettingsReferenceListPermissionManifestV1.ProductionListsManage],
                ImplementationAvailable: true),
        ];
    }
}
