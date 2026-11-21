using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// SAT-D181: the VAT rate / withholding ratio list port. Access first (authenticated actor, member of the server company); the
/// lists are the company's rate list versions in Settings (F-029 S9, entered under the tax master authority; empty until
/// entered, SAT-D148 — the reviewed <see cref="ApprovedTaxRateListCatalog"/> is empty), read under the company filter. No
/// permission is measured here: the answer is reference data and the consumer fences its own operation. An unreadable store is
/// <c>settings_provider_unavailable</c>, never a guessed list.
/// </summary>
internal sealed class SettingsTaxRateListReader(
    SettingsDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser) : ISettingsTaxRateListReaderV1
{
    public async Task<SettingsTaxRateListResolutionV1> GetRateListAsync(string kind, DateOnly on,
        CancellationToken cancellationToken = default)
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

        TaxRateListRows rows;
        try
        {
            rows = await TaxRateListRows.ReadAsync(database, track: false, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.ProviderUnavailable);
        }

        var companyId = companyContext.CompanyId;
        var list = ApprovedTaxRateListCatalog.InForce(rows.ToVersions(companyId), kind, on);
        return list is null
            ? new SettingsTaxRateListResolutionV1(companyId, kind, on, TaxResolutionStatesV1.Unresolved, null, null, [],
                TaxRateListReasonsV1.RateListMissing)
            : new SettingsTaxRateListResolutionV1(companyId, kind, on, TaxResolutionStatesV1.Resolved, list.ListVersion,
                list.EffectiveFrom, list.Entries, null);
    }
}
