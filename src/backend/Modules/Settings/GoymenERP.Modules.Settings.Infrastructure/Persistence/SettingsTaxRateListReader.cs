using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// SAT-D181: the VAT rate / withholding ratio list port. Access first (authenticated actor, member of the server company); the
/// lists are the reviewed source (<see cref="ApprovedTaxRateListCatalog"/>, empty until entered, SAT-D148). No permission is
/// measured here: the answer is reference data and the consumer fences its own operation.
/// </summary>
internal sealed class SettingsTaxRateListReader(ICompanyContext companyContext, ICurrentUser currentUser) : ISettingsTaxRateListReaderV1
{
    public Task<SettingsTaxRateListResolutionV1> GetRateListAsync(string kind, DateOnly on, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.AccessDenied);
        }

        if (!TaxRateListKindsV1.IsKnown(kind))
        {
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.RequestInvalid);
        }

        var companyId = companyContext.CompanyId;
        var list = ApprovedTaxRateListCatalog.InForce(ApprovedTaxRateListCatalog.For(companyId), kind, on);
        return Task.FromResult(list is null
            ? new SettingsTaxRateListResolutionV1(companyId, kind, on, TaxResolutionStatesV1.Unresolved, null, null, [],
                TaxRateListReasonsV1.RateListMissing)
            : new SettingsTaxRateListResolutionV1(companyId, kind, on, TaxResolutionStatesV1.Resolved, list.ListVersion,
                list.EffectiveFrom, list.Entries, null));
    }
}
