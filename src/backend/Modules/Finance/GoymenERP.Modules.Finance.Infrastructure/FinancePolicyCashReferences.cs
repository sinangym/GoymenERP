using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>Real active cash identities only; no balance, bank account, IBAN or assignment leaves this purpose.</summary>
public sealed class FinancePolicyCashReferences(FinanceDbContext database, ICompanyContext company,
    ICurrentUser user, IErpAuthorityRevalidationService authority) : IFinancePolicyCashReferencesV1
{
    private const string Prepare = "settings.policies.prepare";
    private const int Approved = 2;
    private const int MaximumAccounts = 512;
    public async Task<FinancePolicyCashReferencesV1> GetAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var actor = user.UserId;
        await FenceAsync(companyId, actor, cancellationToken);
        var rows = await database.CashAccounts.AsNoTracking().Where(x => x.CompanyId == companyId && x.Status == Approved)
            .OrderBy(x => x.Id).Select(x => new FinancePolicyCashReferenceV1(x.Id, x.CurrencyId, x.DisplayName, x.Version))
            .Take(MaximumAccounts + 1).ToArrayAsync(cancellationToken);
        await FenceAsync(companyId, actor, cancellationToken);
        if (rows.Length > MaximumAccounts || rows.Any(x => x.CashAccountId == Guid.Empty || x.CurrencyId == Guid.Empty
            || x.Version < 1 || string.IsNullOrWhiteSpace(x.Name)))
            throw new InvalidOperationException("finance_policy_cash_source_unavailable");
        return new(companyId, rows);
    }
    private async Task FenceAsync(Guid companyId, string? actor, CancellationToken cancellationToken)
    {
        Same(companyId, actor, cancellationToken);
        if (!ErpPermissions.IsKnown(Prepare)) throw new InvalidOperationException("finance_policy_cash_source_unavailable");
        var scope = await authority.RevalidatePermissionScopeAsync(Prepare, companyId, cancellationToken);
        Same(companyId, actor, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide) throw new UnauthorizedAccessException();
    }
    private void Same(Guid companyId, string? actor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (companyId == Guid.Empty || !company.IsResolved || company.CompanyId != companyId || !user.IsAuthenticated
            || string.IsNullOrWhiteSpace(actor) || user.UserId != actor || !user.CanAccessCompany(companyId))
            throw new UnauthorizedAccessException();
    }
}
