using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Api.ManufacturingRuntime;

/// <summary>Only the existing accounting.periods.read authority; no service permission or period/date policy.
/// The final read borrows the actual reopening transaction and leaves its Accounting locks with that transaction.</summary>
internal sealed class ManufacturingReopeningAccountingPeriodStatusPortV1(ManufacturingDbContext manufacturing,
    IAccountingPeriodStatusPortV1 advisory, ICompanyContext company, ICurrentUser user, IErpAuthorityRevalidationService authority)
    : IManufacturingReopeningAccountingPeriodStatusPortV1
{
    public async Task<AccountingPeriodStatusAnswerV1> GetStatusAsync(DateOnly on, CancellationToken cancellationToken = default)
    {
        var capturedCompany = company.IsResolved ? company.CompanyId : Guid.Empty;
        var capturedActor = user.UserId;
        var capturedTransaction = manufacturing.Database.CurrentTransaction;
        if (!await Allowed()) return Denied;
        AccountingPeriodStatusAnswerV1 answer;
        if (capturedTransaction is null)
            answer = await advisory.GetStatusAsync(on, cancellationToken); // Advisory only; never the final owner fence.
        else
        {
            await using var session = await AccountingPeriodStatusReadSession.EnlistAsync(manufacturing.Database.GetDbConnection(),
                capturedTransaction.GetDbTransaction(), company, cancellationToken);
            answer = await session.ReadAsync(on, cancellationToken);
        }
        return await Allowed() ? answer : Denied;

        async Task<bool> Allowed()
        {
            if (!Current()) return false;
            var scope = await authority.RevalidatePermissionScopeAsync(AccountingPermissionManifestV1.PeriodsRead,
                capturedCompany, cancellationToken);
            return scope.IsGranted && scope.IsCompanyWide && Current();
        }
        bool Current() => capturedCompany != Guid.Empty && company.IsResolved && company.CompanyId == capturedCompany
            && !string.IsNullOrWhiteSpace(capturedActor) && user.IsAuthenticated && user.UserId == capturedActor
            && user.CanAccessCompany(capturedCompany) && ReferenceEquals(manufacturing.Database.CurrentTransaction, capturedTransaction);
    }
    private static AccountingPeriodStatusAnswerV1 Denied => new AccountingPeriodStatusAnswerV1(AccountingPeriodStatusOutcomeV1.Denied, null, null);
}
