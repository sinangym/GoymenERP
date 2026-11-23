using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Shell;

/// <summary>FNS-D70: host-owned composition. The Settings owner supplies current explicit types/rates; no fallback catalog.</summary>
public sealed class SettingsFinanceCollateralTypeCatalogReader(ISettingsCollateralTypeCatalogV1 catalog)
    : IFinanceCollateralTypeCatalogReader
{
    public async Task<FinanceCollateralTypeCatalogAnswer> ListAsync(Guid companyId, DateOnly asOf, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (companyId == Guid.Empty || asOf == default) return FinanceCollateralTypeCatalogAnswer.NotAvailable;
        try
        {
            var answer = await catalog.ListAsync(companyId, cancellationToken);
            if (answer.CompanyId != companyId) return FinanceCollateralTypeCatalogAnswer.NotAvailable;
            return new FinanceCollateralTypeCatalogAnswer(FinanceCollateralTypeCatalogState.Available,
                answer.Entries.Select(entry => new FinanceCollateralTypeCatalogItem(entry.Id, entry.Code, entry.Name,
                    entry.Active, entry.RatePercent, entry.Version, IsNegotiableInstrument: false)).ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return FinanceCollateralTypeCatalogAnswer.NotAvailable; }
    }
}
