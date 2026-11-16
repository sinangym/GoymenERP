using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// FNS-D23: the opening period key is owned by Settings. Until a Settings adapter registers its own
/// <see cref="IFinanceOpeningPeriodReader"/>, Finance answers Unavailable: the period is never assumed open, so every
/// opening balance proposal and approval fails closed and the opening screen shows only its notice.
/// </summary>
internal sealed class UnavailableFinanceOpeningPeriodReader : IFinanceOpeningPeriodReader
{
    public Task<FinanceOpeningPeriodAnswer> ReadAsync(Guid companyId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(FinanceOpeningPeriodAnswer.NotAvailable);
    }
}
