using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

internal sealed partial class SettingsTaxMasterReader
{
    public async Task<SettingsTaxDefaultResolutionV1> GetSubcontractDefaultAsync(SubcontractTaxSideV1 side, Guid productId,
        string buyerSituation, DateOnly on, string? exemptionCode = null, CancellationToken cancellationToken = default)
    {
        var companyId = RequireAccess(); var actor = currentUser.UserId;
        if (!Enum.IsDefined(side) || productId == Guid.Empty || !ValidBuyerInput(buyerSituation, exemptionCode))
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.RequestInvalid);
        var key = side == SubcontractTaxSideV1.Sales ? CompanyPolicyKeysV1.SalesSubcontractTaxDefault : CompanyPolicyKeysV1.PurchasingSubcontractTaxDefault;
        try
        {
            var row = await CompanyPolicyReads.LatestAsync(database, key, cancellationToken);
            if (RequireAccess() != companyId || currentUser.UserId != actor) throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.AccessDenied);
            var data = await ReadAsync(companyId, cancellationToken);
            if (RequireAccess() != companyId || currentUser.UserId != actor) throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.AccessDenied);
            if (row is null) return Map(companyId, productId, TaxItemTypesV1.Service, buyerSituation, on,
                new(TaxResolutionStatesV1.Unresolved, "subcontract_tax_default_missing", null, null, null, null, null, null, null, null, null, null, null));
            var value = CompanyPolicyRowValues.TaxDefault(row) ?? throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.ProviderUnavailable);
            // A company policy has a composite key rather than a Guid record id. The calculator receives its real value;
            // the published result retains that key/version and explicitly has no fictitious Guid identity.
            var input = new TaxItemTypeDefaultDataV1(Guid.Empty, companyId, TaxItemTypesV1.Service, key, row.Version, on,
                value.VatApplies, value.VatPercent, value.ExemptionCode, value.WithholdingApplies, value.WithholdingPercent, value.WithholdingCode);
            return Map(companyId, productId, TaxItemTypesV1.Service, buyerSituation, on,
                TaxMasterCalculatorV1.Resolve(data with { Defaults = [input] }, TaxItemTypesV1.Service, buyerSituation, on, Normalized(exemptionCode)))
                with { RecordId = null, Source = key, RecordVersion = row.Version };
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        { throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.ProviderUnavailable); }
    }
}
