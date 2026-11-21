using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// FNS-D31: the collateral type catalog and the rate per type are owned by Settings. Until a Settings adapter registers
/// its own <see cref="IFinanceCollateralTypeCatalogReader"/>, Finance answers Unavailable: no type and no rate is assumed,
/// so recording a collateral and the weighted collateral figure fail closed.
/// </summary>
internal sealed class UnavailableFinanceCollateralTypeCatalogReader : IFinanceCollateralTypeCatalogReader
{
    public Task<FinanceCollateralTypeCatalogAnswer> ListAsync(Guid companyId, DateOnly asOf,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(FinanceCollateralTypeCatalogAnswer.NotAvailable);
    }
}
