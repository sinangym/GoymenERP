using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

internal sealed class SettingsSubcontractTaxCatalogV1(SettingsDbContext database, ICompanyContext company, ICurrentUser user)
    : ISettingsSubcontractTaxCatalogV1
{
    public async Task<SubcontractTaxOptionsV1> GetAsync(Guid companyId, SubcontractTaxSideV1 side, DateOnly on, CancellationToken token = default)
    {
        var actor = user.UserId; Require(companyId, actor);
        if (!Enum.IsDefined(side) || on == default) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.RequestInvalid);
        try
        {
            var row = await CompanyPolicyReads.LatestAsync(database, side == SubcontractTaxSideV1.Sales
                ? CompanyPolicyKeysV1.SalesSubcontractTaxDefault : CompanyPolicyKeysV1.PurchasingSubcontractTaxDefault, token);
            Require(companyId, actor);
            var data = (await TaxMasterRows.ReadAsync(database, track: false, token)).ToData(companyId); Require(companyId, actor);
            var versions = (await TaxRateListRows.ReadAsync(database, track: false, token)).ToVersions(companyId); Require(companyId, actor);
            var value = row is null ? null : CompanyPolicyRowValues.TaxDefault(row)
                ?? throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
            var vat = ApprovedTaxRateListCatalog.InForce(versions, TaxRateListKindsV1.VatRate, on);
            var withholding = ApprovedTaxRateListCatalog.InForce(versions, TaxRateListKindsV1.WithholdingRatio, on);
            return new(companyId, side, on, (row?.Version ?? 0).ToString(CultureInfo.InvariantCulture), value is null ? null :
                new(value.VatApplies, value.VatPercent is { } v ? TaxMasterCalculatorV1.Format(v) : null, value.ExemptionCode,
                    value.WithholdingApplies, value.WithholdingPercent is { } w ? TaxMasterCalculatorV1.Format(w) : null, value.WithholdingCode),
                Rates(vat), Rates(withholding), TaxMasterCalculatorV1.ListInForce(data, TaxCodeListKindsV1.Exemption, on)?.Entries ?? [],
                TaxMasterCalculatorV1.ListInForce(data, TaxCodeListKindsV1.Withholding, on)?.Entries ?? [],
                string.Create(CultureInfo.InvariantCulture, $"subcontract-tax-lists:{on:yyyy-MM-dd}:{vat?.ListVersion ?? 0}:{withholding?.ListVersion ?? 0}:{TaxMasterCalculatorV1.ListInForce(data, TaxCodeListKindsV1.Exemption, on)?.ListVersion ?? 0}:{TaxMasterCalculatorV1.ListInForce(data, TaxCodeListKindsV1.Withholding, on)?.ListVersion ?? 0}"));
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        { throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable); }
    }
    private static IReadOnlyList<SubcontractTaxRateOptionV1> Rates(TaxRateListVersionDataV1? list) => list?.Entries.Select(x =>
        new SubcontractTaxRateOptionV1(x.Code, x.Name, TaxMasterCalculatorV1.Format(x.Percent), x.TaxCode)).ToArray() ?? [];
    private void Require(Guid expected, string? actor)
    {
        if (expected == Guid.Empty || !company.IsResolved || company.CompanyId != expected || !user.IsAuthenticated
            || string.IsNullOrWhiteSpace(actor) || user.UserId != actor || !user.CanAccessCompany(expected))
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.AccessDenied);
    }
}
